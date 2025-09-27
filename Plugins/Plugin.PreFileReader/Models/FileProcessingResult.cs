namespace Plugin.PreFileReader.Models;

/// <summary>
/// Result of file processing operation
/// Specific to PreFileReaderPlugin for tracking file processing completion status
/// </summary>
public class FileProcessingResult
{
    /// <summary>
    /// Whether the processing was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if processing failed
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// When the processing completed
    /// </summary>
    public DateTime ProcessedAt { get; set; }
    
    /// <summary>
    /// Additional metadata about the processing
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}
