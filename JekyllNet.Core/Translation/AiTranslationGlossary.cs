namespace JekyllNet.Core.Translation;

public sealed class AiTranslationGlossary
{
    public static AiTranslationGlossary Empty { get; } = new([]);

    private readonly IReadOnlyList<AiTranslationGlossaryTerm> _terms;

    public AiTranslationGlossary(IReadOnlyList<AiTranslationGlossaryTerm> terms)
    {
        _terms = terms;
    }

    public IReadOnlyList<AiTranslationGlossaryEntry> ResolveEntries(string targetLanguage)
    {
        if (_terms.Count == 0)
        {
            return [];
        }

        var normalizedTarget = NormalizeLanguageCode(targetLanguage);
        var fallbackTarget = normalizedTarget.Split('-', 2)[0];
        var entries = new List<AiTranslationGlossaryEntry>();
        foreach (var term in _terms)
        {
            if (string.IsNullOrWhiteSpace(term.Source))
            {
                continue;
            }

            if (TryResolveTarget(term, normalizedTarget, fallbackTarget, out var resolvedTarget))
            {
                entries.Add(new AiTranslationGlossaryEntry(term.Source, resolvedTarget));
            }
        }

        return entries;
    }

    private static bool TryResolveTarget(
        AiTranslationGlossaryTerm term,
        string normalizedTarget,
        string fallbackTarget,
        out string resolvedTarget)
    {
        if (term.Targets.TryGetValue(normalizedTarget, out var exactTarget)
            && !string.IsNullOrWhiteSpace(exactTarget))
        {
            resolvedTarget = exactTarget;
            return true;
        }

        if (term.Targets.TryGetValue(fallbackTarget, out var fallbackValue)
            && !string.IsNullOrWhiteSpace(fallbackValue))
        {
            resolvedTarget = fallbackValue;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(term.DefaultTarget))
        {
            resolvedTarget = term.DefaultTarget;
            return true;
        }

        resolvedTarget = string.Empty;
        return false;
    }

    private static string NormalizeLanguageCode(string language)
        => language.Trim().ToLowerInvariant();
}

public sealed record AiTranslationGlossaryTerm(
    string Source,
    string? DefaultTarget,
    IReadOnlyDictionary<string, string> Targets);

public sealed record AiTranslationGlossaryEntry(string Source, string Target);
