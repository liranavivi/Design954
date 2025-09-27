using System.Text.Json;

namespace Processor.Base.Utilities;

/// <summary>
/// Utility class for extracting configuration values from JSON payloads in processor entities
/// Provides safe JSON parsing with default values and nested property access
/// </summary>
public static class JsonConfigurationExtractor
{
    /// <summary>
    /// Extracts a string value from JSON using dot notation path (e.g., "fileFilters.excludeExtensions")
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>String value or default</returns>
    public static string GetStringValue(JsonElement root, string path, string defaultValue = "")
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts an integer value from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>Integer value or default</returns>
    public static int GetIntValue(JsonElement root, string path, int defaultValue = 0)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt32();
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts a long value from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>Long value or default</returns>
    public static long GetLongValue(JsonElement root, string path, long defaultValue = 0)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetInt64();
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts a boolean value from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>Boolean value or default</returns>
    public static bool GetBoolValue(JsonElement root, string path, bool defaultValue = false)
    {
        if (TryGetNestedProperty(root, path, out var element) && 
            (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
        {
            return element.GetBoolean();
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts a double value from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>Double value or default</returns>
    public static double GetDoubleValue(JsonElement root, string path, double defaultValue = 0.0)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts an enum value from JSON using dot notation path
    /// </summary>
    /// <typeparam name="T">Enum type</typeparam>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <param name="ignoreCase">Whether to ignore case when parsing enum</param>
    /// <returns>Enum value or default</returns>
    public static T GetEnumValue<T>(JsonElement root, string path, T defaultValue, bool ignoreCase = true) 
        where T : struct, Enum
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var stringValue = element.GetString();
            if (!string.IsNullOrEmpty(stringValue) && Enum.TryParse<T>(stringValue, ignoreCase, out var enumValue))
            {
                return enumValue;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts a string array from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <returns>String array or empty array if not found</returns>
    public static string[] GetStringArrayValue(JsonElement root, string path)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// Extracts an integer array from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <returns>Integer array or empty array if not found</returns>
    public static int[] GetIntArrayValue(JsonElement root, string path)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.GetInt32())
                .ToArray();
        }
        return Array.Empty<int>();
    }

    /// <summary>
    /// Extracts a DateTime value from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>DateTime value or default</returns>
    public static DateTime? GetDateTimeValue(JsonElement root, string path, DateTime? defaultValue = null)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var stringValue = element.GetString();
            if (!string.IsNullOrEmpty(stringValue) && DateTime.TryParse(stringValue, out var dateTime))
            {
                return dateTime;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts a TimeSpan value from JSON using dot notation path
    /// Supports formats like "00:05:00" or total seconds as number
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>TimeSpan value or default</returns>
    public static TimeSpan GetTimeSpanValue(JsonElement root, string path, TimeSpan defaultValue = default)
    {
        if (TryGetNestedProperty(root, path, out var element))
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var stringValue = element.GetString();
                if (!string.IsNullOrEmpty(stringValue) && TimeSpan.TryParse(stringValue, out var timeSpan))
                {
                    return timeSpan;
                }
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                // Treat as seconds
                var seconds = element.GetDouble();
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Extracts a Guid value from JSON using dot notation path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="defaultValue">Default value if property not found or invalid</param>
    /// <returns>Guid value or default</returns>
    public static Guid GetGuidValue(JsonElement root, string path, Guid defaultValue = default)
    {
        if (TryGetNestedProperty(root, path, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var stringValue = element.GetString();
            if (!string.IsNullOrEmpty(stringValue) && Guid.TryParse(stringValue, out var guid))
            {
                return guid;
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Checks if a property exists at the specified path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <returns>True if property exists, false otherwise</returns>
    public static bool HasProperty(JsonElement root, string path)
    {
        return TryGetNestedProperty(root, path, out _);
    }

    /// <summary>
    /// Gets the raw JsonElement at the specified path
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path to the property</param>
    /// <param name="element">Output element if found</param>
    /// <returns>True if property found, false otherwise</returns>
    public static bool TryGetElement(JsonElement root, string path, out JsonElement element)
    {
        return TryGetNestedProperty(root, path, out element);
    }

    /// <summary>
    /// Safely parses a JSON string and returns the root element
    /// </summary>
    /// <param name="jsonString">JSON string to parse</param>
    /// <param name="root">Output root element if parsing succeeds</param>
    /// <returns>True if parsing succeeds, false otherwise</returns>
    public static bool TryParseJson(string jsonString, out JsonElement root)
    {
        root = default;
        
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return false;
        }

        try
        {
            var document = JsonDocument.Parse(jsonString);
            root = document.RootElement;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Internal method to navigate nested JSON properties using dot notation
    /// </summary>
    /// <param name="root">Root JSON element</param>
    /// <param name="path">Dot-separated path (e.g., "parent.child.property")</param>
    /// <param name="element">Output element if found</param>
    /// <returns>True if property found, false otherwise</returns>
    private static bool TryGetNestedProperty(JsonElement root, string path, out JsonElement element)
    {
        element = default;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var parts = path.Split('.');
        var current = root;

        foreach (var part in parts)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return false;
            }
        }

        element = current;
        return true;
    }
}
