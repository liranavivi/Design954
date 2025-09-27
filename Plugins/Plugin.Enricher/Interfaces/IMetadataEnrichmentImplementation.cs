using Microsoft.Extensions.Logging;
using Plugin.Enricher.Models;
using Shared.Correlation;

namespace Plugin.Enricher.Interfaces;

/// <summary>
/// Interface for custom metadata enrichment implementations
/// </summary>
public interface IMetadataEnrichmentImplementation
{
    /// <summary>
    /// Mandatory file extension that this implementation expects to process
    /// Used by EnricherPlugin to identify the correct information file from extractedFiles
    /// </summary>
    string MandatoryFileExtension { get; }

    /// <summary>
    /// Enrich information content to metadata format and return complete file cache data object
    /// </summary>
    /// <param name="informationContent">Information content to enrich</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="config">Enrichment configuration</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete file cache data object with enriched extension, metadata, and content</returns>
    Task<object> EnrichToMetadataAsync(
        string informationContent,
        string fileName,
        EnrichmentConfiguration config,
        HierarchicalLoggingContext context,
        ILogger logger);
}
