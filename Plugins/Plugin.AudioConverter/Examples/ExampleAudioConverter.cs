using Microsoft.Extensions.Logging;
using Plugin.AudioConverter.Interfaces;
using Plugin.AudioConverter.Models;
using Plugin.AudioConverter.Services;
using Plugin.AudioConverter.Utilities;
using Shared.Correlation;

namespace Plugin.AudioConverter.Examples;

/// <summary>
/// Example implementation of IAudioConversionImplementation that demonstrates audio conversion functionality
/// Uses FFmpeg to convert audio files and creates converted audio file cache data object
/// This is a reference implementation showing how to implement custom audio conversion logic
/// </summary>
public class ExampleAudioConverter : IAudioConversionImplementation
{
    /// <summary>
    /// Mandatory file extension that this implementation expects to process
    /// This implementation processes WAV audio files
    /// </summary>
    public string MandatoryFileExtension => ".wav";

    /// <summary>
    /// Convert audio content using FFmpeg and return complete file cache data object
    /// </summary>
    /// <param name="audioContent">Audio binary content to convert</param>
    /// <param name="fileName">Original file name</param>
    /// <param name="config">Audio converter configuration</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete file cache data object with converted audio content</returns>
    public async Task<object> ConvertAudioAsync(
        byte[] audioContent,
        string fileName,
        AudioConverterConfiguration config,
        HierarchicalLoggingContext context,
        ILogger logger)
    {
        try
        {
            logger.LogInformationWithHierarchy(context, "Starting audio conversion for file: {FileName}", fileName);

            // Create FFmpeg service for conversion
            var ffmpegService = new FFmpegService(logger);

            // Check if FFmpeg is available at configured path
            if (!ffmpegService.IsFFmpegAvailable(context, config.FFmpegPath))
            {
                var pathInfo = config.FFmpegPath ?? "system PATH";
                throw new InvalidOperationException($"FFmpeg is not available at: {pathInfo}");
            }

            // Execute FFmpeg conversion with configuration
            var ffmpegResult = await ffmpegService.ConvertAudioAsync(audioContent, config.FFmpegConversionArguments, context, config.FFmpegPath);

            if (!ffmpegResult.Success)
            {
                throw new InvalidOperationException($"FFmpeg conversion failed: {ffmpegResult.StandardError}");
            }

            // Create conversion result with metadata
            var conversionResult = new ConversionResult
            {
                Success = true,
                ConvertedFile = new ConvertedAudioInfo
                {
                    OriginalMetadata = new { fileName = fileName, originalSize = audioContent.Length },
                    ConversionFields = new Dictionary<string, object>
                    {
                        ["ffmpegArguments"] = config.FFmpegConversionArguments,
                        ["ffmpegCommand"] = ffmpegResult.Command,
                        ["conversionTimestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                        ["conversionSuccessful"] = true,
                        ["processingDurationMs"] = ffmpegResult.ProcessingDurationMs,
                        ["convertedAudioData"] = ffmpegResult.ConvertedAudioData != null ? Convert.ToBase64String(ffmpegResult.ConvertedAudioData) : string.Empty
                    },
                    ConversionTimestamp = DateTime.UtcNow,
                    ConversionVersion = "1.0",
                    ConversionSuccessful = true,
                    ConversionMessages = new List<string> { $"Audio converted using FFmpeg with arguments: {config.FFmpegConversionArguments}" }
                }
            };

            // Extract converted audio data from the conversion result
            var convertedAudioDataBase64 = ffmpegResult.ConvertedAudioData != null ? Convert.ToBase64String(ffmpegResult.ConvertedAudioData) : string.Empty;
            var convertedAudioBytes = ffmpegResult.ConvertedAudioData ?? Array.Empty<byte>();
            var originalContentString = Convert.ToBase64String(audioContent);

            // Create complete file cache data object with converted content
            var fileCacheDataObject = AudioConverterCacheHelper.CreateConvertedAudioFileCacheDataObject(
                fileName, convertedAudioBytes, originalContentString);

            return await Task.FromResult(fileCacheDataObject);
        }
        catch (Exception ex)
        {
            logger.LogErrorWithHierarchy(context, ex, "Failed to convert audio file: {FileName}", fileName);

            // Create error result and return as file cache data object
            var errorResult = CreateErrorConversionResult(fileName, ex.Message);
            var errorBytes = Array.Empty<byte>();
            var originalContentString = Convert.ToBase64String(audioContent);
            var errorFileCacheDataObject = AudioConverterCacheHelper.CreateConvertedAudioFileCacheDataObject(
                fileName, errorBytes, originalContentString);
            return await Task.FromResult(errorFileCacheDataObject);
        }
    }

    /// <summary>
    /// Create error conversion result for failed conversions
    /// </summary>
    private ConversionResult CreateErrorConversionResult(string fileName, string errorMessage)
    {
        return new ConversionResult
        {
            Success = false,
            ConvertedFile = new ConvertedAudioInfo
            {
                OriginalMetadata = new { fileName = fileName, error = errorMessage },
                ConversionFields = new Dictionary<string, object>
                {
                    ["conversionTimestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["conversionSuccessful"] = false,
                    ["errorMessage"] = errorMessage
                },
                ConversionTimestamp = DateTime.UtcNow,
                ConversionVersion = "1.0",
                ConversionSuccessful = false,
                ConversionMessages = new List<string> { $"Audio conversion failed: {errorMessage}" }
            }
        };
    }
}
