using System.Text.Json;

namespace TextToSqlAgent.Core.Tools;

/// <summary>
/// Input to a tool
/// </summary>
public class ToolInput
{
    public Dictionary<string, object> Parameters { get; set; } = new();

    public T Get<T>(string key)
    {
        return Get<T>(new[] { key });
    }

    public T Get<T>(params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            throw new ArgumentException("At least one parameter key must be provided");

        foreach (var key in keys)
        {
            if (!Parameters.TryGetValue(key, out var rawValue))
                continue;

            // P1-03: Safe parameter parsing with detailed error context
            if (TryConvertValue(rawValue, out T converted))
            {
                // Additional validation for reference types
                if (converted == null && !IsNullableType(typeof(T)))
                {
                    throw new ArgumentException(
                        $"Parameter '{key}' converted to null but {typeof(T).Name} is not nullable");
                }
                return converted;
            }

            // Provide detailed error context
            var rawType = rawValue?.GetType().Name ?? "null";
            throw new ArgumentException(
                $"Parameter '{key}' exists (type: {rawType}) but could not be safely converted to {typeof(T).Name}. " +
                $"Value: {GetSafeValuePreview(rawValue)}");
        }

        throw new ArgumentException(
            $"Required parameter not found. Expected one of: {string.Join(", ", keys)}");
    }

    public T? GetOrDefault<T>(string key, T? defaultValue = default)
    {
        if (!Parameters.TryGetValue(key, out var rawValue))
            return defaultValue;

        return TryConvertValue(rawValue, out T converted)
            ? converted
            : defaultValue;
    }

    public T? GetOrDefault<T>(T? defaultValue = default, params string[] keys)
    {
        if (keys == null || keys.Length == 0)
            return defaultValue;

        foreach (var key in keys)
        {
            if (!Parameters.TryGetValue(key, out var rawValue))
                continue;

            if (TryConvertValue(rawValue, out T converted))
                return converted;
        }

        return defaultValue;
    }

    public string GetString(string key) => Get<string>(key);
    public string GetString(params string[] keys) => Get<string>(keys);
    public int GetInt(string key) => Get<int>(key);
    public bool GetBool(string key) => Get<bool>(key);

    private static bool TryConvertValue<T>(object rawValue, out T converted)
    {
        var targetType = typeof(T);

        // P1-03: Handle null explicitly
        if (rawValue == null)
        {
            if (IsNullableType(targetType))
            {
                converted = default!;
                return true;
            }
            converted = default!;
            return false;
        }

        // Direct type match
        if (rawValue is T typedValue)
        {
            converted = typedValue;
            return true;
        }

        // JsonElement handling (from LLM responses)
        if (rawValue is JsonElement jsonElement)
        {
            return TryConvertFromJsonElement(jsonElement, out converted);
        }

        // String conversion
        if (rawValue is string str)
        {
            if (TryConvertFromString(str, targetType, out var fromString))
            {
                converted = (T)fromString!;
                return true;
            }
        }

        // Try Convert.ChangeType (for primitives)
        try
        {
            var changed = Convert.ChangeType(rawValue, targetType);
            if (changed != null)
            {
                converted = (T)changed;
                return true;
            }
        }
        catch
        {
            // Fall through to JSON-based conversion
        }

        // Last resort: JSON serialize/deserialize (for complex objects)
        try
        {
            var json = JsonSerializer.Serialize(rawValue);
            var fromJson = JsonSerializer.Deserialize<T>(json, SerializerOptions);
            if (fromJson != null)
            {
                converted = fromJson;
                return true;
            }
        }
        catch
        {
            // No-op
        }

        converted = default!;
        return false;
    }

    private static bool TryConvertFromJsonElement<T>(JsonElement element, out T converted)
    {
        var targetType = typeof(T);

        // P1-03: Handle null/undefined explicitly
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            if (IsNullableType(targetType))
            {
                converted = default!;
                return true;
            }
            converted = default!;
            return false;
        }

        // String conversion
        if (targetType == typeof(string))
        {
            converted = (T)(object)(element.ValueKind == JsonValueKind.String
                ? (element.GetString() ?? string.Empty)
                : element.ToString());
            return true;
        }

        // Deserialize to target type
        try
        {
            var value = element.Deserialize<T>(SerializerOptions);
            if (value != null)
            {
                converted = value;
                return true;
            }
        }
        catch
        {
            // No-op
        }

        converted = default!;
        return false;
    }

    private static bool TryConvertFromString(string str, Type targetType, out object? converted)
    {
        converted = null;

        if (targetType == typeof(string))
        {
            converted = str;
            return true;
        }

        if (targetType == typeof(int) && int.TryParse(str, out var parsedInt))
        {
            converted = parsedInt;
            return true;
        }

        if (targetType == typeof(bool) && bool.TryParse(str, out var parsedBool))
        {
            converted = parsedBool;
            return true;
        }

        if (targetType.IsEnum && Enum.TryParse(targetType, str, ignoreCase: true, out var enumValue))
        {
            converted = enumValue;
            return true;
        }

        try
        {
            converted = JsonSerializer.Deserialize(str, targetType, SerializerOptions);
            return converted != null;
        }
        catch
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// P1-03: Check if type is nullable
    /// </summary>
    private static bool IsNullableType(Type type)
    {
        if (!type.IsValueType) return true; // Reference types are nullable
        return Nullable.GetUnderlyingType(type) != null; // Nullable<T>
    }

    /// <summary>
    /// P1-03: Get safe preview of value for error messages (avoid logging sensitive data)
    /// </summary>
    private static string GetSafeValuePreview(object? value)
    {
        if (value == null) return "null";

        var str = value.ToString();
        if (str == null || str.Length <= 50) return str ?? "null";

        return str.Substring(0, 50) + "... (truncated)";
    }
}
