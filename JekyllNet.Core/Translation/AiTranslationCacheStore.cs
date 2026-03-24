using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JekyllNet.Core.Translation;

public sealed class AiTranslationCacheStore
{
    private readonly string? _filePath;
    private readonly Dictionary<string, string> _entries;
    private bool _isDirty;

    private AiTranslationCacheStore(string? filePath, Dictionary<string, string> entries)
    {
        _filePath = filePath;
        _entries = entries;
    }

    public static async Task<AiTranslationCacheStore> LoadAsync(string? filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new AiTranslationCacheStore(null, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        if (!File.Exists(filePath))
        {
            return new AiTranslationCacheStore(filePath, new Dictionary<string, string>(StringComparer.Ordinal));
        }

        await using var stream = File.OpenRead(filePath);
        var persisted = await JsonSerializer.DeserializeAsync<CacheDocument>(stream, cancellationToken: cancellationToken);
        return new AiTranslationCacheStore(
            filePath,
            persisted?.Entries is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(persisted.Entries, StringComparer.Ordinal));
    }

    public bool TryGet(string provider, string model, string baseUrl, AiTranslationRequest request, out string translatedText)
        => _entries.TryGetValue(CreateKey(provider, model, baseUrl, request), out translatedText!);

    public void Set(string provider, string model, string baseUrl, AiTranslationRequest request, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var key = CreateKey(provider, model, baseUrl, request);
        if (_entries.TryGetValue(key, out var existing)
            && string.Equals(existing, translatedText, StringComparison.Ordinal))
        {
            return;
        }

        _entries[key] = translatedText;
        _isDirty = true;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDirty || string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        var document = new CacheDocument
        {
            Version = 1,
            Entries = _entries
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };

        await JsonSerializer.SerializeAsync(stream, document, new JsonSerializerOptions
        {
            WriteIndented = true
        }, cancellationToken);

        _isDirty = false;
    }

    private static string CreateKey(string provider, string model, string baseUrl, AiTranslationRequest request)
    {
        var payload = JsonSerializer.Serialize(new
        {
            provider = provider.Trim().ToLowerInvariant(),
            model = model.Trim(),
            base_url = baseUrl.Trim(),
            source_language = request.SourceLanguage.Trim().ToLowerInvariant(),
            target_language = request.TargetLanguage.Trim().ToLowerInvariant(),
            text_kind = request.TextKind.ToString(),
            text = NormalizeText(request.Text),
            glossary = (request.GlossaryEntries ?? [])
                .OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Target, StringComparer.Ordinal)
                .Select(entry => new
                {
                    source = entry.Source,
                    target = entry.Target
                })
                .ToArray()
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string NormalizeText(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class CacheDocument
    {
        public int Version { get; init; }

        public Dictionary<string, string>? Entries { get; init; }
    }
}
