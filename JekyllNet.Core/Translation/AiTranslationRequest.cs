namespace JekyllNet.Core.Translation;

public sealed record AiTranslationRequest(
    string SourceLanguage,
    string TargetLanguage,
    string Text,
    AiTextKind TextKind,
    IReadOnlyList<AiTranslationGlossaryEntry>? GlossaryEntries = null);
