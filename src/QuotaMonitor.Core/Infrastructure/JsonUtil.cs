using System.Globalization;
using System.Text.Json;

namespace QuotaMonitor.Core.Infrastructure;

public static class JsonUtil
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions IndentedSerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string Serialize(object value)
    {
        return JsonSerializer.Serialize(value, SerializerOptions);
    }

    public static string SerializeIndented(object value)
    {
        return JsonSerializer.Serialize(value, IndentedSerializerOptions);
    }

    public static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, SerializerOptions);
    }

    public static Dictionary<string, object> ParseObject(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            return ToDictionary(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    public static object Value(Dictionary<string, object> dict, string name)
    {
        return dict != null && dict.TryGetValue(name, out var value) ? value : null;
    }

    public static Dictionary<string, object> Dict(object value)
    {
        return value as Dictionary<string, object>;
    }

    public static string String(Dictionary<string, object> dict, string name)
    {
        var value = Value(dict, name);
        return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static long? Long(Dictionary<string, object> dict, string name)
    {
        var value = Value(dict, name);
        if (value == null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt64(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static int? Int(Dictionary<string, object> dict, string name)
    {
        var value = Long(dict, name);
        return value.HasValue ? (int)value.Value : null;
    }

    public static double? Double(Dictionary<string, object> dict, string name)
    {
        var value = Value(dict, name);
        if (value == null)
        {
            return null;
        }

        try
        {
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public static DateTimeOffset? Date(Dictionary<string, object> dict, string name)
    {
        var text = String(dict, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed.ToLocalTime();
    }

    public static DateTimeOffset? UnixSeconds(Dictionary<string, object> dict, string name)
    {
        var seconds = Long(dict, name);
        if (!seconds.HasValue)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).ToLocalTime();
    }

    public static DateTimeOffset? FlexibleDate(object value)
    {
        if (value == null)
        {
            return null;
        }

        try
        {
            if (value is string)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                {
                    return UnixTimestampToLocal(numeric);
                }

                if (DateTimeOffset.TryParse(
                        text,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsed))
                {
                    return parsed.ToLocalTime();
                }

                return null;
            }

            return UnixTimestampToLocal(Convert.ToInt64(value, CultureInfo.InvariantCulture));
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? UnixTimestampToLocal(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        if (value > 100000000000L)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime();
        }

        return DateTimeOffset.FromUnixTimeSeconds(value).ToLocalTime();
    }

    private static Dictionary<string, object> ToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ToValue(property.Value);
        }

        return result;
    }

    private static object ToValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ToDictionary(element);
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(ToValue).ToArray();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var longValue))
                {
                    return longValue;
                }

                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}
