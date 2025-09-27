using Microsoft.Extensions.Logging;
using Plugin.Standardizer.Models;
using Shared.Correlation;

namespace Plugin.Standardizer.Interfaces;

/// <summary>
/// Interface for custom metadata standardization implementations
/// </summary>
public interface IMetadataStandardizationImplementation
{
    /// <summary>
    /// Mandatory file extension that this implementation expects to process
    /// Used by StandardizerPlugin to identify the correct information file from extractedFiles
    /// </summary>
    string MandatoryFileExtension { get; }

    /// <summary>
    /// Standardize information content to metadata format and return complete file cache data object
    /// </summary>
    /// <param name="informationContent">Information content to standardize</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="config">Standardization configuration</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete file cache data object with XML extension, metadata, and content</returns>
    Task<object> StandardizeToMetadataAsync(
        string informationContent,
        string fileName,
        StandardizationConfiguration config,
        HierarchicalLoggingContext context,
        ILogger logger);
}
