namespace Plugin.AudioConverter.Models;

/// <summary>
/// Result of audio conversion operation
/// </summary>
public class ConversionResult
{
    /// <summary>
    /// Indicates if the conversion was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The converted file information
    /// </summary>
    public ConvertedAudioInfo? ConvertedFile { get; set; }

    /// <summary>
    /// Error message if conversion failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Processing duration in milliseconds
    /// </summary>
    public double ProcessingDurationMs { get; set; }

    /// <summary>
    /// Additional processing details
    /// </summary>
    public Dictionary<string, object> ProcessingDetails { get; set; } = new();

    /// <summary>
    /// List of conversion messages
    /// </summary>
    public List<string> ConversionMessages { get; set; } = new();
}
