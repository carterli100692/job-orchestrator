namespace JobOrchestrator.Worker;

public enum WorkerAttemptKind
{
    Succeeded,
    RetryableFailure,
    PermanentFailure
}

public sealed record WorkerAttemptResult(
    WorkerAttemptKind Kind,
    string? Output = null,
    string? Error = null,
    TimeSpan? RetryAfter = null)
{
    public static WorkerAttemptResult Success(string output) =>
        new(WorkerAttemptKind.Succeeded, output);

    public static WorkerAttemptResult Retryable(string error, TimeSpan? retryAfter = null) =>
        new(WorkerAttemptKind.RetryableFailure, Error: error, RetryAfter: retryAfter);

    public static WorkerAttemptResult Permanent(string error) =>
        new(WorkerAttemptKind.PermanentFailure, Error: error);
}
