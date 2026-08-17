using System.Text.Json;
using System.Text.Json.Serialization;
using Storybloq.Reader.Core.Models;

namespace Storybloq.Reader.Core.Serialization;

/// <summary>
/// Reads a <see cref="StoryEnum{TEnum}"/> from any JSON token without throwing.
/// A number or boolean where a status was expected is preserved as raw text rather than
/// failing the whole file — one malformed field must not cost the reader an entire ticket.
/// </summary>
internal sealed class StoryEnumConverter<TEnum> : JsonConverter<StoryEnum<TEnum>>
    where TEnum : struct, Enum
{
    public override StoryEnum<TEnum> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return StoryEnum<TEnum>.Absent;

            case JsonTokenType.String:
                return StoryEnum<TEnum>.FromRaw(reader.GetString());

            case JsonTokenType.Number:
            case JsonTokenType.True:
            case JsonTokenType.False:
            {
                using var document = JsonDocument.ParseValue(ref reader);
                return StoryEnum<TEnum>.FromRaw(document.RootElement.ToString());
            }

            default:
                // Object or array where a scalar belongs: skip it and record absence.
                reader.Skip();
                return StoryEnum<TEnum>.Absent;
        }
    }

    public override void Write(Utf8JsonWriter writer, StoryEnum<TEnum> value, JsonSerializerOptions options)
    {
        if (value.Raw is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Raw);
    }
}

internal sealed class StoryEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(StoryEnum<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var enumType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(StoryEnumConverter<>).MakeGenericType(enumType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
