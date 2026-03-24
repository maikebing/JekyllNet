using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JekyllNet.Core.Translation;

public sealed class OpenAiCompatibleTranslationClient : IAiTranslationClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly bool _ownsHttpClient;

    public OpenAiCompatibleTranslationClient(string baseUrl, string model, string? apiKey, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _model = model;
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
        _httpClient.BaseAddress = new Uri(NormalizeBaseUrl(baseUrl), UriKind.Absolute);

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<string> TranslateAsync(
        AiTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return string.Empty;
        }

        var payload = new
        {
            model = _model,
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt(request)
                },
                new
                {
                    role = "user",
                    content = request.Text
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(responseText);
        var translated = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return translated?.Trim() ?? string.Empty;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/');
        return normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized + "/"
            : normalized + "/v1/";
    }

    private static string BuildSystemPrompt(AiTranslationRequest request)
    {
        var instructions = request.TextKind switch
        {
            AiTextKind.Markdown => "Translate markdown content faithfully. Preserve markdown syntax, Liquid tags, HTML tags, URLs, code fences, inline code, and placeholder tokens.",
            AiTextKind.Label => "Translate a short UI/legal label. Keep it concise and natural for website UI.",
            _ => "Translate plain website text faithfully. Preserve placeholders, brand names, URLs, and formatting where present."
        };

        var glossaryInstructions = BuildGlossaryInstructions(request.GlossaryEntries);
        return $"You are a professional website localization engine. Translate the provided text from {request.SourceLanguage} to {request.TargetLanguage}. {instructions}{glossaryInstructions} Return only the translated text with no commentary.";
    }

    private static string BuildGlossaryInstructions(IReadOnlyList<AiTranslationGlossaryEntry>? glossaryEntries)
    {
        if (glossaryEntries is not { Count: > 0 })
        {
            return string.Empty;
        }

        var lines = glossaryEntries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Source) && !string.IsNullOrWhiteSpace(entry.Target))
            .Select(static entry => $"- {entry.Source} => {entry.Target}")
            .ToList();

        return lines.Count == 0
            ? string.Empty
            : " Use the following glossary exactly when applicable:\n" + string.Join("\n", lines);
    }
}
