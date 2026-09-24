using JobOrchestrator.Validation;

namespace JobOrchestrator.Execution;

public sealed class TemplateResolver : ITemplateResolver
{
    public string? Resolve(string? template, IReadOnlyDictionary<string, string> outputs)
    {
        if (template is null)
        {
            return null;
        }

        return TemplateSyntax.ReferenceRegex.Replace(template, match =>
        {
            var stepId = match.Groups[1].Value;
            if (!outputs.TryGetValue(stepId, out var output))
            {
                throw new InvalidOperationException($"No successful output is available for referenced step '{stepId}'.");
            }

            return output;
        });
    }
}
