using JobOrchestrator.Models;

namespace JobOrchestrator.Validation;

public interface IDefinitionValidator
{
    IReadOnlyList<ValidationError> Validate(ExecutionDefinition definition);
}
