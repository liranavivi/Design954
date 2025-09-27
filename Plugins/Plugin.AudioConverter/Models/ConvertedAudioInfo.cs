namespace Plugin.AudioConverter.Models;

/// <summary>
/// Information about a converted audio file
/// </summary>
public class ConvertedAudioInfo
{
    /// <summary>
    /// Original file metadata
    /// </summary>
    public object? OriginalMetadata { get; set; }

    /// <summary>
    /// Fields added during conversion
    /// </summary>
    public Dictionary<string, object> ConversionFields { get; set; } = new();

    /// <summary>
    /// Timestamp when conversion was performed
    /// </summary>
    public DateTime ConversionTimestamp { get; set; }

    /// <summary>
    /// Version of the conversion process
    /// </summary>
    public string ConversionVersion { get; set; } = "1.0";

    /// <summary>
    /// Type of conversion performed
    /// </summary>
    public string ConversionType { get; set; } = "FFmpegConversion";

    /// <summary>
    /// Indicates if conversion was successful
    /// </summary>
    public bool ConversionSuccessful { get; set; }

    /// <summary>
    /// List of conversion messages
    /// </summary>
    public List<string> ConversionMessages { get; set; } = new();
}
