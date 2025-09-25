using System.Text.Json.Serialization;
using System.Text.Json;

namespace NotifyService.Converters;

public class DictionaryObjectJsonConverter : JsonConverter<Dictionary<string, object?>?>
{
    public override Dictionary<string, object?> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException();

        using var doc = JsonDocument.ParseValue(ref reader);
        return ReadElement(doc.RootElement);
    }

    private static Dictionary<string, object?> ReadElement(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertElement(prop.Value);
        }
        return dict;
    }

    private static object? ConvertElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ReadElement(element);

            case JsonValueKind.Array:
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                    list.Add(ConvertElement(item));
                return list;

            case JsonValueKind.String:
                if (element.TryGetDateTime(out var dt))
                    return dt;
                return element.GetString();

            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                    return l;
                return element.GetDouble();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return element.GetBoolean();

            case JsonValueKind.Null:
                return null;

            default:
                return element.GetRawText();
        }
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object?>? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}