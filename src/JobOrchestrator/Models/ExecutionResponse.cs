namespace JobOrchestrator.Models;

public enum OverallExecutionStatus
{
    Succeeded,
    PartiallySucceeded,
    Failed
}

public enum JobExecutionStatus
{
    Succeeded,
    Failed
}

public sealed record ExecutionResponse(
    OverallExecutionStatus Status,
    IReadOnlyList<JobExecutionResponse> Jobs);

public sealed record JobExecutionResponse(
    string Id,
    JobExecutionStatus Status,
    string? Output);

public sealed record ValidationErrorResponse(
    string Title,
    IReadOnlyList<ValidationError> Errors);

public sealed record ValidationError(string Path, string Message);
