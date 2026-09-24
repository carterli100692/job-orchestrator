using JobOrchestrator.Execution;

namespace JobOrchestrator.Tests;

public sealed class TemplateResolverTests
{
    [Fact]
    public void ResolvesMultipleAndRepeatedReferences_WithoutInterpretingDollarSigns()
    {
        var resolver = new TemplateResolver();
        var outputs = new Dictionary<string, string>
        {
            ["a"] = "value$1",
            ["b"] = "value-2"
        };

        var result = resolver.Resolve(
            "x=${steps.a.output}; y=${steps.b.output}; again=${steps.a.output}",
            outputs);

        Assert.Equal("x=value$1; y=value-2; again=value$1", result);
    }
}
