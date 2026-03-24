namespace JekyllNet.Core.Translation;

public interface IAiTranslationClient
{
    Task<string> TranslateAsync(
        AiTranslationRequest request,
        CancellationToken cancellationToken = default);
}
