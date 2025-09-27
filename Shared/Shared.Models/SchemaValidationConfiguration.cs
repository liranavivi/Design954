namespace Shared.Services.Models;

/// <summary>
/// Configuration for schema validation
/// </summary>
public class SchemaValidationConfiguration
{
    /// <summary>
    /// Enable input schema validation for main processor input data
    /// </summary>
    public bool EnableInputValidation { get; set; } = true;

    /// <summary>
    /// Enable output schema validation
    /// </summary>
    public bool EnableOutputValidation { get; set; } = true;

    /// <summary>
    /// Log validation warnings
    /// </summary>
    public bool LogValidationWarnings { get; set; } = true;

    /// <summary>
    /// Log validation errors
    /// </summary>
    public bool LogValidationErrors { get; set; } = true;

    /// <summary>
    /// Include validation telemetry
    /// </summary>
    public bool IncludeValidationTelemetry { get; set; } = true;
}
