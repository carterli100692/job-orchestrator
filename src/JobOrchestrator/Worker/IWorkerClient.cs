namespace JobOrchestrator.Worker;

public interface IWorkerClient
{
    Task<WorkerAttemptResult> ExecuteOnceAsync(string? input, CancellationToken cancellationToken);
}
