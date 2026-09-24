namespace JobOrchestrator.Execution;

public interface ITemplateResolver
{
    string? Resolve(string? template, IReadOnlyDictionary<string, string> outputs);
}
