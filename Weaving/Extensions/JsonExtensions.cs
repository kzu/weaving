using System.Text.Json;
using System.Text.Json.Nodes;

namespace Weaving;

public static class JsonExtensions
{
    static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Recursively removes 'AdditionalProperties' and truncates long strings in an object before serialization.
    /// </summary>
    public static string ToJsonString(this object? value, int maxStringLength = 100)
    {
        if (value is null)
            return "{}";

        var node = JsonSerializer.SerializeToNode(value, value.GetType(), options);
        return FilterNode(node, maxStringLength)?.ToJsonString() ?? "{}";
    }

    static JsonNode? FilterNode(JsonNode? node, int maxStringLength)
    {
        if (node is JsonObject obj)
        {
            var filtered = new JsonObject();
            foreach (var prop in obj)
            {
                //if (prop.Key == "AdditionalProperties")
                //    continue;
                if (FilterNode(prop.Value, maxStringLength) is JsonNode value)
                    filtered[prop.Key] = value.DeepClone();
            }
            return filtered;
        }
        if (node is JsonArray arr)
        {
            var filtered = new JsonArray();
            foreach (var item in arr)
            {
                if (FilterNode(item, maxStringLength) is JsonNode value)
                    filtered.Add(value.DeepClone());
            }

            return filtered;
        }
        if (node is JsonValue val && val.TryGetValue(out string? str) && str is not null && str.Length > maxStringLength)
        {
            return str[..maxStringLength] + "...";
        }
        return node;
    }
}
