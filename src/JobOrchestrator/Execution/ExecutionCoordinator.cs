using System.Collections.Concurrent;
using JobOrchestrator.Models;
using JobOrchestrator.Validation;
using JobOrchestrator.Worker;
using Microsoft.Extensions.Options;

namespace JobOrchestrator.Execution;

public sealed class ExecutionCoordinator : IExecutionCoordinator
{
    private readonly IDefinitionValidator _validator;
    private readonly ITemplateResolver _templateResolver;
    private readonly IWorkerClient _workerClient;
    private readonly WorkerOptions _workerOptions;
    private readonly ILogger<ExecutionCoordinator> _logger;

    public ExecutionCoordinator(
        IDefinitionValidator validator,
        ITemplateResolver templateResolver,
        IWorkerClient workerClient,
        IOptions<WorkerOptions> workerOptions,
        ILogger<ExecutionCoordinator> logger)
    {
        _validator = validator;
        _templateResolver = templateResolver;
        _workerClient = workerClient;
        _workerOptions = workerOptions.Value;
        _logger = logger;
    }

    public async Task<ExecutionOperationResult> ExecuteAsync(
        ExecutionDefinition definition,
        CancellationToken cancellationToken)
    {
        var validationErrors = _validator.Validate(definition);
        if (validationErrors.Count > 0)
        {
            return ExecutionOperationResult.Invalid(validationErrors);
        }

        var jobs = definition.Jobs!;
        using var workerCallGate = new SemaphoreSlim(definition.MaxParallelism, definition.MaxParallelism);

        var jobTasks = jobs.Select(job => ExecuteJobAsync(job, workerCallGate, cancellationToken)).ToArray();
        var jobResults = await Task.WhenAll(jobTasks);

        var succeeded = jobResults.Count(result => result.Status == JobExecutionStatus.Succeeded);
        var overallStatus = succeeded switch
        {
            0 => OverallExecutionStatus.Failed,
            var count when count == jobResults.Length => OverallExecutionStatus.Succeeded,
            _ => OverallExecutionStatus.PartiallySucceeded
        };

        return ExecutionOperationResult.Success(new ExecutionResponse(overallStatus, jobResults));
    }

    private async Task<JobExecutionResponse> ExecuteJobAsync(
        JobDefinition job,
        SemaphoreSlim workerCallGate,
        CancellationToken cancellationToken)
    {
        var steps = job.Steps!;
        var stepById = steps.ToDictionary(step => step.Id!, StringComparer.Ordinal);
        var tasks = new ConcurrentDictionary<string, Lazy<Task<StepExecutionResult>>>(StringComparer.Ordinal);

        Task<StepExecutionResult> GetStepTask(string stepId) =>
            tasks.GetOrAdd(
                stepId,
                id => new Lazy<Task<StepExecutionResult>>(
                    () => ExecuteStepAsync(stepById[id], GetStepTask, workerCallGate, cancellationToken),
                    LazyThreadSafetyMode.ExecutionAndPublication))
                .Value;

        var allStepTasks = steps.Select(step => GetStepTask(step.Id!)).ToArray();
        var results = await Task.WhenAll(allStepTasks);
        var resultById = results.ToDictionary(result => result.StepId, StringComparer.Ordinal);

        var jobStatus = results.All(result => result.Status == StepExecutionStatus.Succeeded)
            ? JobExecutionStatus.Succeeded
            : JobExecutionStatus.Failed;

        string? output = null;
        if (job.Output is not null)
        {
            var match = TemplateSyntax.ExactReferenceRegex.Match(job.Output);
            var outputStepId = match.Groups[1].Value;
            if (resultById.TryGetValue(outputStepId, out var outputStep) &&
                outputStep.Status == StepExecutionStatus.Succeeded)
            {
                output = outputStep.Output;
            }
        }

        return new JobExecutionResponse(job.Id!, jobStatus, output);
    }

    private async Task<StepExecutionResult> ExecuteStepAsync(
        StepDefinition step,
        Func<string, Task<StepExecutionResult>> getStepTask,
        SemaphoreSlim workerCallGate,
        CancellationToken cancellationToken)
    {
        var dependencyIds = step.DependsOn ?? [];
        var dependencyResults = await Task.WhenAll(dependencyIds.Select(getStepTask));

        if (dependencyResults.Any(result => result.Status != StepExecutionStatus.Succeeded))
        {
            return StepExecutionResult.Blocked(
                step.Id!,
                "One or more dependencies did not succeed.",
                dependencyResults);
        }

        var successfulOutputs = dependencyResults
            .Where(result => result.Output is not null)
            .ToDictionary(result => result.StepId, result => result.Output!, StringComparer.Ordinal);

        foreach (var dependencyResult in dependencyResults)
        {
            CollectSuccessfulDependencyOutputs(dependencyResult, successfulOutputs);
        }

        string? resolvedInput;
        try
        {
            resolvedInput = _templateResolver.Resolve(step.Input, successfulOutputs);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Template resolution failed for step {StepId}", step.Id);
            return StepExecutionResult.Failed(step.Id!, ex.Message, dependencyResults);
        }

        var executionResult = await ExecuteWithRetriesAsync(
            step.Id!,
            resolvedInput,
            workerCallGate,
            cancellationToken);

        return executionResult with { Dependencies = dependencyResults };
    }

    private async Task<StepExecutionResult> ExecuteWithRetriesAsync(
        string stepId,
        string? input,
        SemaphoreSlim workerCallGate,
        CancellationToken cancellationToken)
    {
        string? lastError = null;

        for (var attempt = 1; attempt <= _workerOptions.MaxAttempts; attempt++)
        {
            WorkerAttemptResult workerResult;
            await workerCallGate.WaitAsync(cancellationToken);
            try
            {
                workerResult = await _workerClient.ExecuteOnceAsync(input, cancellationToken);
            }
            finally
            {
                workerCallGate.Release();
            }

            if (workerResult.Kind == WorkerAttemptKind.Succeeded)
            {
                return StepExecutionResult.Succeeded(stepId, workerResult.Output!);
            }

            lastError = workerResult.Error ?? "Worker execution failed.";
            if (workerResult.Kind == WorkerAttemptKind.PermanentFailure)
            {
                return StepExecutionResult.Failed(stepId, lastError);
            }

            if (attempt == _workerOptions.MaxAttempts)
            {
                break;
            }

            var delay = workerResult.RetryAfter ?? GetBackoffDelay(attempt);
            _logger.LogWarning(
                "Retrying step {StepId} after attempt {Attempt}/{MaxAttempts} in {DelayMs} ms. Reason: {Reason}",
                stepId,
                attempt,
                _workerOptions.MaxAttempts,
                delay.TotalMilliseconds,
                lastError);

            await Task.Delay(delay, cancellationToken);
        }

        return StepExecutionResult.Failed(
            stepId,
            $"Worker execution failed after {_workerOptions.MaxAttempts} attempts. Last error: {lastError}");
    }

    private TimeSpan GetBackoffDelay(int completedAttempt)
    {
        var initial = Math.Max(0, _workerOptions.InitialRetryDelayMilliseconds);
        var max = Math.Max(initial, _workerOptions.MaxRetryDelayMilliseconds);
        var multiplier = Math.Pow(2, Math.Max(0, completedAttempt - 1));
        var milliseconds = Math.Min(max, initial * multiplier);
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    private static void CollectSuccessfulDependencyOutputs(
        StepExecutionResult result,
        IDictionary<string, string> outputs)
    {
        var stack = new Stack<StepExecutionResult>();
        stack.Push(result);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current.Status == StepExecutionStatus.Succeeded && current.Output is not null)
            {
                outputs[current.StepId] = current.Output;
            }

            foreach (var dependency in current.Dependencies)
            {
                stack.Push(dependency);
            }
        }
    }

    private enum StepExecutionStatus
    {
        Succeeded,
        Failed,
        Blocked
    }

    private sealed record StepExecutionResult(
        string StepId,
        StepExecutionStatus Status,
        string? Output,
        string? Error,
        IReadOnlyList<StepExecutionResult> Dependencies)
    {
        public static StepExecutionResult Succeeded(
            string stepId,
            string output,
            IReadOnlyList<StepExecutionResult>? dependencies = null) =>
            new(stepId, StepExecutionStatus.Succeeded, output, null, dependencies ?? []);

        public static StepExecutionResult Failed(
            string stepId,
            string error,
            IReadOnlyList<StepExecutionResult>? dependencies = null) =>
            new(stepId, StepExecutionStatus.Failed, null, error, dependencies ?? []);

        public static StepExecutionResult Blocked(
            string stepId,
            string error,
            IReadOnlyList<StepExecutionResult>? dependencies = null) =>
            new(stepId, StepExecutionStatus.Blocked, null, error, dependencies ?? []);
    }
}
