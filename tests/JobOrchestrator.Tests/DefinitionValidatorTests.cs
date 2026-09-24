using JobOrchestrator.Models;
using JobOrchestrator.Validation;

namespace JobOrchestrator.Tests;

public sealed class DefinitionValidatorTests
{
    private readonly DefinitionValidator _validator = new();

    [Fact]
    public void ValidDag_IsAccepted()
    {
        var definition = new ExecutionDefinition
        {
            MaxParallelism = 2,
            Jobs =
            [
                new JobDefinition
                {
                    Id = "job-1",
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

        var errors = _validator.Validate(definition);

        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownDependency_IsRejected()
    {
        var definition = SingleJob(
            new StepDefinition { Id = "publish", DependsOn = ["missing"] });

        var errors = _validator.Validate(definition);

        Assert.Contains(errors, error => error.Message.Contains("Unknown dependency 'missing'", StringComparison.Ordinal));
    }

    [Fact]
    public void Cycle_IsRejected()
    {
        var definition = SingleJob(
            new StepDefinition { Id = "a", DependsOn = ["b"] },
            new StepDefinition { Id = "b", DependsOn = ["a"] });

        var errors = _validator.Validate(definition);

        Assert.Contains(errors, error => error.Message.Contains("contains a cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void InputReferenceMustBeDependency_IsRejected()
    {
        var definition = SingleJob(
            new StepDefinition { Id = "load", Input = "x" },
            new StepDefinition { Id = "publish", Input = "${steps.load.output}" });

        var errors = _validator.Validate(definition);

        Assert.Contains(errors, error => error.Message.Contains("is not a dependency", StringComparison.Ordinal));
    }

    private static ExecutionDefinition SingleJob(params StepDefinition[] steps) =>
        new()
        {
            MaxParallelism = 2,
            Jobs =
            [
                new JobDefinition
                {
                    Id = "job",
                    Steps = [.. steps]
                }
            ]
        };
}
