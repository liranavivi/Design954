namespace Plugin.Enricher.Models;

/// <summary>
/// Configuration settings for enrichment operations extracted from DeliveryAssignmentModel
/// </summary>
public class EnrichmentConfiguration
{
    /// <summary>
    /// Type name for custom metadata enrichment implementation
    /// </summary>
    public string? MetadataImplementationType { get; set; }
}
