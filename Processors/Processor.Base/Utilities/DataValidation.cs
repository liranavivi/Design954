namespace Processor.Base.Utilities;

/// <summary>
/// Shared utilities for data validation operations across all processors
/// Provides standardized data validation patterns and checks
/// </summary>
public static class DataValidation
{
    /// <summary>
    /// Determines if the data represents effectively empty content that should skip validation
    /// This method follows the exact same logic as ProcessorService.IsEffectivelyEmptyData
    /// </summary>
    /// <param name="data">The serialized data to check</param>
    /// <returns>True if the data is effectively empty</returns>
    public static bool IsEffectivelyEmptyData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return true;

        // Trim whitespace and check for common empty JSON patterns
        var trimmed = data.Trim();

        return trimmed == "{}" ||           // Empty object: new { }
               trimmed == "[]" ||           // Empty array: new List<object>()
               trimmed == "null" ||         // JSON null
               trimmed == "\"\"";           // Empty JSON string: ""
    }
}
