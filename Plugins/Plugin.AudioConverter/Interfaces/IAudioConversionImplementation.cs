using Microsoft.Extensions.Logging;
using Plugin.AudioConverter.Models;
using Shared.Correlation;

namespace Plugin.AudioConverter.Interfaces;

/// <summary>
/// Interface for custom audio conversion implementations
/// </summary>
public interface IAudioConversionImplementation
{
    /// <summary>
    /// Mandatory file extension that this implementation expects to process
    /// Used by AudioConverterPlugin to identify the correct audio file from extractedFiles
    /// </summary>
    string MandatoryFileExtension { get; }

    /// <summary>
    /// Convert audio content and return complete file cache data object
    /// </summary>
    /// <param name="audioContent">Audio binary content to convert</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="config">Audio converter configuration</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete file cache data object with converted audio content</returns>
    Task<object> ConvertAudioAsync(
        byte[] audioContent,
        string fileName,
        AudioConverterConfiguration config,
        HierarchicalLoggingContext context,
        ILogger logger);
}
