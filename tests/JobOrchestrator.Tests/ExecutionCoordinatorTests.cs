using JobOrchestrator.Execution;
using JobOrchestrator.Models;
using JobOrchestrator.Validation;
using JobOrchestrator.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobOrchestrator.Tests;

public sealed class ExecutionCoordinatorTests
{
    [Fact]
    public async Task ResolvesDependencyOutputIntoInput()
    {
        var worker = new RecordingWorkerClient(input => WorkerAttemptResult.Success($"out({input})"));
        var coordinator = CreateCoordinator(worker);
        var definition = new ExecutionDefinition
        {
            MaxParallelism = 2,
            Jobs =
            [
                new JobDefinition
                {
                    Id = "job",
                    Output = "${steps.publish.output}",
                    Steps =
                    [
                        new StepDefinition { Id = "load", Input = "customer-42" },
                        new StepDefinition
                        {
                            Id = "publish",
                            DependsOn = ["load"],
                            Input = "value=${steps.load.output}"
                        }
                    ]
                }
            ]
        };

        var result = await coordinator.ExecuteAsync(definition, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(OverallExecutionStatus.Succeeded, result.Response!.Status);
        Assert.Equal("out(value=out(customer-42))", result.Response.Jobs.Single().Output);
        Assert.Equal(new string?[] { "customer-42", "value=out(customer-42)" }, worker.Inputs);
    }

    [Fact]
    public async Task GlobalParallelism_AppliesAcrossJobs()
    {
        var worker = new ConcurrencyTrackingWorkerClient(TimeSpan.FromMilliseconds(30));
        var coordinator = CreateCoordinator(worker);
        var definition = new ExecutionDefinition
        {
            MaxParallelism = 2,
            Jobs =
            [
                IndependentJob("job-1", 4),
                IndependentJob("job-2", 4)
            ]
        };

        var result = await coordinator.ExecuteAsync(definition, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(OverallExecutionStatus.Succeeded, result.Response!.Status);
        Assert.InRange(worker.PeakConcurrency, 1, 2);
        Assert.Equal(8, worker.TotalCalls);
    }

    [Fact]
    public async Task RetryableFailures_AreRetried()
    {
        var attempts = 0;
        var worker = new RecordingWorkerClient(_ =>
        {
            attempts++;
            return attempts < 3
                ? WorkerAttemptResult.Retryable("temporary")
                : WorkerAttemptResult.Success("done");
        });

        var coordinator = CreateCoordinator(worker, new WorkerOptions
        {
            MaxAttempts = 4,
            InitialRetryDelayMilliseconds = 1,
            MaxRetryDelayMilliseconds = 2
        });

        var result = await coordinator.ExecuteAsync(
            new ExecutionDefinition
            {
                MaxParallelism = 1,
                Jobs =
                [
                    new JobDefinition
                    {
                        Id = "job",
                        Output = "${steps.step.output}",
                        Steps = [new StepDefinition { Id = "step", Input = "x" }]
                    }
                ]
            },
            CancellationToken.None);

        Assert.Equal(3, attempts);
        Assert.Equal(OverallExecutionStatus.Succeeded, result.Response!.Status);
        Assert.Equal("done", result.Response.Jobs.Single().Output);
    }

    [Fact]
    public async Task FailedDependency_BlocksDependentStep_AndOtherJobCanStillSucceed()
    {
        var worker = new RecordingWorkerClient(input => input switch
        {
            "fail" => WorkerAttemptResult.Permanent("bad request"),
            _ => WorkerAttemptResult.Success($"out({input})")
        });

        var coordinator = CreateCoordinator(worker);
        var definition = new ExecutionDefinition
        {
            MaxParallelism = 4,
            Jobs =
            [
                new JobDefinition
                {
                    Id = "bad-job",
                    Output = "${steps.after.output}",
                    Steps =
                    [
                        new StepDefinition { Id = "root", Input = "fail" },
                        new StepDefinition { Id = "after", DependsOn = ["root"], Input = "${steps.root.output}" }
                    ]
                },
                new JobDefinition
                {
                    Id = "good-job",
                    Output = "${steps.ok.output}",
                    Steps = [new StepDefinition { Id = "ok", Input = "good" }]
                }
            ]
        };

        var result = await coordinator.ExecuteAsync(definition, CancellationToken.None);

        Assert.Equal(OverallExecutionStatus.PartiallySucceeded, result.Response!.Status);
        Assert.Equal(JobExecutionStatus.Failed, result.Response.Jobs.Single(job => job.Id == "bad-job").Status);
        Assert.Null(result.Response.Jobs.Single(job => job.Id == "bad-job").Output);
        Assert.Equal(JobExecutionStatus.Succeeded, result.Response.Jobs.Single(job => job.Id == "good-job").Status);
        Assert.Equal(2, worker.Inputs.Count);
    }

    private static ExecutionCoordinator CreateCoordinator(
        IWorkerClient worker,
        WorkerOptions? options = null) =>
        new(
            new DefinitionValidator(),
            new TemplateResolver(),
            worker,
            Options.Create(options ?? new WorkerOptions
            {
                MaxAttempts = 3,
                InitialRetryDelayMilliseconds = 1,
                MaxRetryDelayMilliseconds = 2
            }),
            NullLogger<ExecutionCoordinator>.Instance);

    private static JobDefinition IndependentJob(string id, int stepCount) =>
        new()
        {
            Id = id,
            Steps = Enumerable.Range(1, stepCount)
                .Select(i => new StepDefinition { Id = $"step-{i}", Input = $"{id}-{i}" })
                .ToList()
        };

    private sealed class RecordingWorkerClient : IWorkerClient
    {
        private readonly Func<string?, WorkerAttemptResult> _handler;
        private readonly object _lock = new();

        public RecordingWorkerClient(Func<string?, WorkerAttemptResult> handler)
        {
            _handler = handler;
        }

        public List<string?> Inputs { get; } = [];

        public Task<WorkerAttemptResult> ExecuteOnceAsync(string? input, CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                Inputs.Add(input);
            }

            return Task.FromResult(_handler(input));
        }
    }

    private sealed class ConcurrencyTrackingWorkerClient : IWorkerClient
    {
        private readonly TimeSpan _delay;
        private int _active;
        private int _peak;
        private int _totalCalls;

        public ConcurrencyTrackingWorkerClient(TimeSpan delay)
        {
            _delay = delay;
        }

        public int PeakConcurrency => Volatile.Read(ref _peak);
        public int TotalCalls => Volatile.Read(ref _totalCalls);

        public async Task<WorkerAttemptResult> ExecuteOnceAsync(string? input, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _totalCalls);
            var active = Interlocked.Increment(ref _active);

            while (true)
            {
                var currentPeak = Volatile.Read(ref _peak);
                if (active <= currentPeak || Interlocked.CompareExchange(ref _peak, active, currentPeak) == currentPeak)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(_delay, cancellationToken);
                return WorkerAttemptResult.Success($"out({input})");
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
