namespace Plugin.Enricher.Models;

/// <summary>
/// Model representing enriched file information
/// Contains both original metadata and enriched analysis results
/// </summary>
public class EnrichedFileInfo
{
    /// <summary>
    /// Original file metadata (preserved from previous plugins)
    /// </summary>
    public object? OriginalMetadata { get; set; }

    /// <summary>
    /// Enriched metadata fields to add
    /// </summary>
    public Dictionary<string, object> EnrichedFields { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Timestamp when enrichment was performed
    /// </summary>
    public DateTime EnrichmentTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version of the enrichment process
    /// </summary>
    public string EnrichmentVersion { get; set; } = "1.0";

    /// <summary>
    /// Type of enrichment applied
    /// </summary>
    public string EnrichmentType { get; set; } = "FileAnalysis";

    /// <summary>
    /// Indicates if enrichment was successful
    /// </summary>
    public bool EnrichmentSuccessful { get; set; } = true;

    /// <summary>
    /// Any messages or warnings during enrichment
    /// </summary>
    public List<string> EnrichmentMessages { get; set; } = new List<string>();
}

/// <summary>
/// Result of the enrichment process
/// </summary>
public class EnrichmentResult
{
    /// <summary>
    /// Indicates if the enrichment was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Enriched file information
    /// </summary>
    public EnrichedFileInfo? EnrichedFile { get; set; }

    /// <summary>
    /// Error message if enrichment failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Processing duration in milliseconds
    /// </summary>
    public double ProcessingDurationMs { get; set; }

    /// <summary>
    /// Additional processing details
    /// </summary>
    public Dictionary<string, object> ProcessingDetails { get; set; } = new Dictionary<string, object>();
}
