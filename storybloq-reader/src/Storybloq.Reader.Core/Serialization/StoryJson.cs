using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Serialization;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> used for every <c>.story/</c> read.
/// Deliberately reflection-based rather than source-generated: the app is not trimmed or
/// AOT-compiled, and the generic <see cref="StoryEnumConverterFactory"/> is simpler to keep
/// correct without a generated context.
/// </summary>
public static class StoryJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        options.Converters.Add(new StoryEnumConverterFactory());
        options.Converters.Add(new TolerantInt32Converter());
        return options;
    }

    /// <summary>
    /// Deserializes, translating an empty file into <c>null</c> rather than an exception —
    /// a zero-byte file is what a reader sees when it catches a writer mid-create.
    /// </summary>
    public static T? Deserialize<T>(string json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>(json, Options);
    }
}

/// <summary>
/// Accepts a fractional or string-encoded number where an integer is expected. Upstream
/// validates these as integers, but losing an entire ticket because <c>order</c> arrived as
/// <c>1.5</c> would be a poor trade for a read-only viewer.
/// </summary>
internal sealed class TolerantInt32Converter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var exact))
                {
                    return exact;
                }

                return reader.TryGetDouble(out var approximate) && approximate is >= int.MinValue and <= int.MaxValue
                    ? (int)approximate
                    : null;

            case JsonTokenType.String:
            {
                var text = reader.GetString();
                if (int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }

                return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedDouble)
                    && parsedDouble is >= int.MinValue and <= int.MaxValue
                    ? (int)parsedDouble
                    : null;
            }

            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}
