using JobOrchestrator.Models;

namespace JobOrchestrator.Execution;

public interface IExecutionCoordinator
{
    Task<ExecutionOperationResult> ExecuteAsync(ExecutionDefinition definition, CancellationToken cancellationToken);
}

public sealed record ExecutionOperationResult(
    ExecutionResponse? Response,
    IReadOnlyList<ValidationError> ValidationErrors)
{
    public bool IsValid => ValidationErrors.Count == 0;

    public static ExecutionOperationResult Invalid(IReadOnlyList<ValidationError> errors) =>
        new(null, errors);

    public static ExecutionOperationResult Success(ExecutionResponse response) =>
        new(response, Array.Empty<ValidationError>());
}
