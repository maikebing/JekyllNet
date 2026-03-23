using System.Globalization;
using JekyllNet.Core.Models;
using YamlDotNet.Serialization;

namespace JekyllNet.Core.Parsers;

public sealed class FrontMatterParser
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public FrontMatterDocument Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("---", StringComparison.Ordinal))
        {
            return new FrontMatterDocument { Content = text };
        }

        using var reader = new StringReader(text);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine, "---", StringComparison.Ordinal))
        {
            return new FrontMatterDocument { Content = text };
        }

        var yamlLines = new List<string>();
        string? line;
        var foundClosingFence = false;

        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal) || string.Equals(line, "...", StringComparison.Ordinal))
            {
                foundClosingFence = true;
                break;
            }

            yamlLines.Add(line);
        }

        if (!foundClosingFence)
        {
            return new FrontMatterDocument { Content = text };
        }

        var yaml = string.Join(Environment.NewLine, yamlLines);
        var body = reader.ReadToEnd();

        var parsed = string.IsNullOrWhiteSpace(yaml)
            ? new Dictionary<object, object?>()
            : _deserializer.Deserialize<Dictionary<object, object?>>(yaml) ?? new Dictionary<object, object?>();

        return new FrontMatterDocument
        {
            FrontMatter = Normalize(parsed),
            Content = body.TrimStart('\r', '\n')
        };
    }

    private static Dictionary<string, object?> Normalize(Dictionary<object, object?> values)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            var key = Convert.ToString(pair.Key, CultureInfo.InvariantCulture) ?? string.Empty;
            result[key] = NormalizeValue(pair.Value);
        }

        return result;
    }

    private static object? NormalizeValue(object? value)
    {
        return value switch
        {
            Dictionary<object, object?> map => Normalize(map),
            IList<object?> list => list.Select(NormalizeValue).ToList(),
            _ => value
        };
    }
}