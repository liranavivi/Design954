using System.Security.Cryptography;

namespace Plugin.AudioConverter.Utilities;

/// <summary>
/// AudioConverter-specific cache helper class for conversion operations
/// Provides conversion-specific functionality for creating converted audio file cache data objects
/// Aligned with EnricherCacheHelper pattern
/// </summary>
public static class AudioConverterCacheHelper
{
    /// <summary>
    /// Create a new file cache data object with converted audio content and metadata (schema-compliant)
    /// </summary>
    /// <param name="originalFileName">Original file name</param>
    /// <param name="convertedAudioData">Converted audio data as byte array</param>
    /// <param name="originalContent">Original content for reference</param>
    /// <returns>Complete schema-compliant file cache data object</returns>
    public static object CreateConvertedAudioFileCacheDataObject(
        string originalFileName,
        byte[] convertedAudioData,
        string originalContent)
    {
        try
        {
            // Generate converted file name with .mp3 extension (FFmpeg default output)
            var convertedFileName = Path.ChangeExtension(originalFileName, ".mp3");
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            
            // Convert audio data to base64 for storage
            var convertedBase64 = Convert.ToBase64String(convertedAudioData);

            // Create schema-compliant file cache data object
            var fileCacheDataObject = new
            {
                fileMetadata = new
                {
                    fileName = convertedFileName,
                    filePath = convertedFileName,
                    fileSize = convertedAudioData.Length,
                    createdDate = timestamp,
                    modifiedDate = timestamp,
                    fileExtension = ".mp3",
                    detectedMimeType = "audio/mpeg",
                    fileType = "Audio File",
                    contentHash = Convert.ToBase64String(SHA256.HashData(convertedAudioData))
                },
                fileContent = new
                {
                    binaryData = convertedBase64,
                    encoding = "base64"
                }
            };

            // Return schema-compliant structure with extractedFileCacheDataObject
            return new
            {
                fileCacheDataObject = fileCacheDataObject,
                extractedFileCacheDataObject = new object[0] // Empty array as required
            };
        }
        catch (Exception)
        {
            // Return error structure that matches schema on failure
            var errorTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var errorHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("conversion_error"));
            
            return new
            {
                fileCacheDataObject = new
                {
                    fileMetadata = new
                    {
                        fileName = "error_converted.mp3",
                        filePath = "error_converted.mp3",
                        fileSize = 0,
                        createdDate = errorTimestamp,
                        modifiedDate = errorTimestamp,
                        fileExtension = ".mp3",
                        detectedMimeType = "audio/mpeg",
                        fileType = "Error",
                        contentHash = errorHash
                    },
                    fileContent = new
                    {
                        binaryData = "",
                        encoding = "base64"
                    }
                },
                extractedFileCacheDataObject = new object[0]
            };
        }
    }
}
