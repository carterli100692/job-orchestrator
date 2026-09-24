using JobOrchestrator.Models;

namespace JobOrchestrator.Validation;

public sealed class DefinitionValidator : IDefinitionValidator
{
    public IReadOnlyList<ValidationError> Validate(ExecutionDefinition definition)
    {
        var errors = new List<ValidationError>();

        if (definition.MaxParallelism <= 0)
        {
            errors.Add(new ValidationError("maxParallelism", "maxParallelism must be greater than zero."));
        }

        if (definition.Jobs is null || definition.Jobs.Count == 0)
        {
            errors.Add(new ValidationError("jobs", "At least one job is required."));
            return errors;
        }

        ValidateJobIds(definition.Jobs, errors);

        for (var jobIndex = 0; jobIndex < definition.Jobs.Count; jobIndex++)
        {
            ValidateJob(definition.Jobs[jobIndex], jobIndex, errors);
        }

        return errors;
    }

    private static void ValidateJobIds(IReadOnlyList<JobDefinition> jobs, List<ValidationError> errors)
    {
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < jobs.Count; i++)
        {
            var id = jobs[i].Id;
            var path = $"jobs[{i}].id";

            if (!IsValidId(id))
            {
                errors.Add(new ValidationError(path,
                    "Job id is required and must contain 1-100 letters, digits, underscores, or hyphens, starting with a letter or digit."));
                continue;
            }

            if (!seenIds.Add(id!))
            {
                errors.Add(new ValidationError(path, $"Duplicate job id '{id}'."));
            }
        }
    }

    private static void ValidateJob(JobDefinition job, int jobIndex, List<ValidationError> errors)
    {
        var stepsPath = $"jobs[{jobIndex}].steps";
        if (job.Steps is null || job.Steps.Count == 0)
        {
            errors.Add(new ValidationError(stepsPath, "Each job must contain at least one step."));
            return;
        }

        var stepById = new Dictionary<string, StepDefinition>(StringComparer.Ordinal);
        var stepIndexById = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var stepIndex = 0; stepIndex < job.Steps.Count; stepIndex++)
        {
            var step = job.Steps[stepIndex];
            var idPath = $"jobs[{jobIndex}].steps[{stepIndex}].id";

            if (!IsValidId(step.Id))
            {
                errors.Add(new ValidationError(idPath,
                    "Step id is required and must contain 1-100 letters, digits, underscores, or hyphens, starting with a letter or digit."));
                continue;
            }

            if (!stepById.TryAdd(step.Id!, step))
            {
                errors.Add(new ValidationError(idPath, $"Duplicate step id '{step.Id}'."));
            }
            else
            {
                stepIndexById[step.Id!] = stepIndex;
            }
        }

        if (stepById.Count != job.Steps.Count)
        {
            return;
        }

        ValidateDependencies(job, jobIndex, stepById, stepIndexById, errors);

        var hasCycle = HasCycle(stepById);
        if (hasCycle)
        {
            errors.Add(new ValidationError(stepsPath, "The step dependency graph contains a cycle."));
        }

        ValidateJobOutput(job, jobIndex, stepById, errors);
        ValidateInputReferences(job, jobIndex, stepById, stepIndexById, hasCycle, errors);
    }

    private static void ValidateDependencies(
        JobDefinition job,
        int jobIndex,
        IReadOnlyDictionary<string, StepDefinition> stepById,
        IReadOnlyDictionary<string, int> stepIndexById,
        List<ValidationError> errors)
    {
        foreach (var (stepId, step) in stepById)
        {
            var stepIndex = stepIndexById[stepId];
            var dependencies = step.DependsOn ?? [];
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var dependencyIndex = 0; dependencyIndex < dependencies.Count; dependencyIndex++)
            {
                var dependency = dependencies[dependencyIndex];
                var path = $"jobs[{jobIndex}].steps[{stepIndex}].dependsOn[{dependencyIndex}]";

                if (string.IsNullOrWhiteSpace(dependency))
                {
                    errors.Add(new ValidationError(path, "Dependency id cannot be empty."));
                    continue;
                }

                if (!seen.Add(dependency))
                {
                    errors.Add(new ValidationError(path, $"Duplicate dependency '{dependency}'."));
                }

                if (!stepById.ContainsKey(dependency))
                {
                    errors.Add(new ValidationError(path, $"Unknown dependency '{dependency}'."));
                }
                else if (string.Equals(stepId, dependency, StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError(path, "A step cannot depend on itself."));
                }
            }
        }
    }

    private static void ValidateJobOutput(
        JobDefinition job,
        int jobIndex,
        IReadOnlyDictionary<string, StepDefinition> stepById,
        List<ValidationError> errors)
    {
        if (job.Output is null)
        {
            return;
        }

        var match = TemplateSyntax.ExactReferenceRegex.Match(job.Output);
        if (!match.Success)
        {
            errors.Add(new ValidationError($"jobs[{jobIndex}].output",
                "Job output must be exactly one step output reference, for example '${steps.publish.output}'."));
            return;
        }

        var referencedStepId = match.Groups[1].Value;
        if (!stepById.ContainsKey(referencedStepId))
        {
            errors.Add(new ValidationError($"jobs[{jobIndex}].output",
                $"Job output references unknown step '{referencedStepId}'."));
        }
    }

    private static void ValidateInputReferences(
        JobDefinition job,
        int jobIndex,
        IReadOnlyDictionary<string, StepDefinition> stepById,
        IReadOnlyDictionary<string, int> stepIndexById,
        bool hasCycle,
        List<ValidationError> errors)
    {
        foreach (var (stepId, step) in stepById)
        {
            var stepIndex = stepIndexById[stepId];
            var inputPath = $"jobs[{jobIndex}].steps[{stepIndex}].input";

            if (TemplateSyntax.HasMalformedStepReference(step.Input))
            {
                errors.Add(new ValidationError(inputPath,
                    "Input contains a malformed step reference. Expected '${steps.<step-id>.output}'."));
            }

            var references = TemplateSyntax.ExtractReferences(step.Input);
            foreach (var referencedStepId in references.Distinct(StringComparer.Ordinal))
            {
                if (!stepById.ContainsKey(referencedStepId))
                {
                    errors.Add(new ValidationError(inputPath,
                        $"Input references unknown step '{referencedStepId}'."));
                    continue;
                }

                if (string.Equals(stepId, referencedStepId, StringComparison.Ordinal))
                {
                    errors.Add(new ValidationError(inputPath, "A step cannot reference its own output."));
                    continue;
                }

                if (!hasCycle && !IsTransitiveDependency(stepId, referencedStepId, stepById))
                {
                    errors.Add(new ValidationError(inputPath,
                        $"Input references step '{referencedStepId}', but that step is not a dependency of '{stepId}'. Add it to dependsOn directly or transitively."));
                }
            }
        }
    }

    private static bool HasCycle(IReadOnlyDictionary<string, StepDefinition> stepById)
    {
        var indegree = stepById.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var dependents = stepById.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var (stepId, step) in stepById)
        {
            foreach (var dependency in step.DependsOn ?? [])
            {
                if (!stepById.ContainsKey(dependency) || dependency == stepId)
                {
                    continue;
                }

                indegree[stepId]++;
                dependents[dependency].Add(stepId);
            }
        }

        var queue = new Queue<string>(indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visited++;

            foreach (var dependent in dependents[current])
            {
                indegree[dependent]--;
                if (indegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        return visited != stepById.Count;
    }

    private static bool IsTransitiveDependency(
        string stepId,
        string candidateDependency,
        IReadOnlyDictionary<string, StepDefinition> stepById)
    {
        var stack = new Stack<string>(stepById[stepId].DependsOn ?? []);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == candidateDependency)
            {
                return true;
            }

            if (!stepById.TryGetValue(current, out var dependencyStep))
            {
                continue;
            }

            foreach (var dependency in dependencyStep.DependsOn ?? [])
            {
                stack.Push(dependency);
            }
        }

        return false;
    }

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TemplateSyntax.IdRegex.IsMatch(id);
}
