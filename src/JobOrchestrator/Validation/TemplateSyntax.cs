using System.Text.RegularExpressions;

namespace JobOrchestrator.Validation;

public static partial class TemplateSyntax
{
    public const string IdPattern = "[A-Za-z0-9][A-Za-z0-9_-]{0,99}";

    [GeneratedRegex(@"\$\{steps\.([A-Za-z0-9][A-Za-z0-9_-]{0,99})\.output\}", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegexCore();

    [GeneratedRegex(@"^\$\{steps\.([A-Za-z0-9][A-Za-z0-9_-]{0,99})\.output\}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactReferenceRegexCore();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegexCore();

    public static Regex ReferenceRegex => ReferenceRegexCore();
    public static Regex ExactReferenceRegex => ExactReferenceRegexCore();
    public static Regex IdRegex => IdRegexCore();

    public static IReadOnlyList<string> ExtractReferences(string? template)
    {
        if (string.IsNullOrEmpty(template))
        {
            return Array.Empty<string>();
        }

        return ReferenceRegex.Matches(template)
            .Select(match => match.Groups[1].Value)
            .ToArray();
    }

    public static bool HasMalformedStepReference(string? template)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("${steps.", StringComparison.Ordinal))
        {
            return false;
        }

        var withoutValidReferences = ReferenceRegex.Replace(template, string.Empty);
        return withoutValidReferences.Contains("${steps.", StringComparison.Ordinal);
    }
}
