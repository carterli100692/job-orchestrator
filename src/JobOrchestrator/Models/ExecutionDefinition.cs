namespace JobOrchestrator.Models;

public sealed class ExecutionDefinition
{
    public int MaxParallelism { get; init; }
    public List<JobDefinition>? Jobs { get; init; }
}

public sealed class JobDefinition
{
    public string? Id { get; init; }
    public string? Output { get; init; }
    public List<StepDefinition>? Steps { get; init; }
}

public sealed class StepDefinition
{
    public string? Id { get; init; }
    public List<string>? DependsOn { get; init; }
    public string? Input { get; init; }
}
