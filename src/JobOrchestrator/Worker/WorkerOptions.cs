namespace JobOrchestrator.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public string BaseUrl { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string CandidateEmail { get; init; } = string.Empty;
    public int MaxAttempts { get; init; } = 4;
    public int InitialRetryDelayMilliseconds { get; init; } = 250;
    public int MaxRetryDelayMilliseconds { get; init; } = 5000;
    public int RequestTimeoutSeconds { get; init; } = 30;
}
