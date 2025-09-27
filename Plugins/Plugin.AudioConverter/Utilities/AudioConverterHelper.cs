using System.Text.Json;
using Plugin.AudioConverter.Models;

namespace Plugin.AudioConverter.Utilities;

/// <summary>
/// Helper utilities for audio conversion operations
/// </summary>
public static class AudioConverterHelper
{
    /// <summary>
    /// Extract file name from cache data
    /// </summary>
    public static string GetFileNameFromCacheData(JsonElement cacheData)
    {
        try
        {
            if (cacheData.TryGetProperty("fileCacheDataObject", out var fileCacheObj) &&
                fileCacheObj.TryGetProperty("fileMetadata", out var metadata) &&
                metadata.TryGetProperty("fileName", out var fileNameElement))
            {
                return fileNameElement.GetString() ?? "unknown";
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return "unknown";
    }

    /// <summary>
    /// Extract audio binary data from cache data
    /// </summary>
    public static byte[]? ExtractAudioBinaryData(JsonElement cacheData)
    {
        try
        {
            if (cacheData.TryGetProperty("fileCacheDataObject", out var fileCacheObj) &&
                fileCacheObj.TryGetProperty("fileContent", out var content) &&
                content.TryGetProperty("binaryData", out var binaryDataElement))
            {
                var binaryDataString = binaryDataElement.GetString();
                if (!string.IsNullOrEmpty(binaryDataString))
                {
                    return Convert.FromBase64String(binaryDataString);
                }
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    /// <summary>
    /// Create enhanced cache data object with conversion information
    /// </summary>
    public static object CreateConvertedCacheDataObject(JsonElement originalCacheData, ConvertedAudioInfo convertedInfo, byte[]? convertedAudioData = null)
    {
        try
        {
            // Parse original cache data
            var originalDict = JsonSerializer.Deserialize<Dictionary<string, object>>(originalCacheData.GetRawText());
            
            if (originalDict == null)
            {
                throw new InvalidOperationException("Failed to parse original cache data");
            }

            // Enhance the cache data with conversion info
            if (originalDict.TryGetValue("fileCacheDataObject", out var fileCacheObj))
            {
                var fileCacheDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    JsonSerializer.Serialize(fileCacheObj));

                if (fileCacheDict != null)
                {
                    // Enhance metadata with conversion info
                    if (fileCacheDict.TryGetValue("fileMetadata", out var metadata))
                    {
                        var metadataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            JsonSerializer.Serialize(metadata));

                        if (metadataDict != null)
                        {
                            // Add conversion fields to metadata
                            foreach (var conversionField in convertedInfo.ConversionFields)
                            {
                                metadataDict[conversionField.Key] = conversionField.Value;
                            }

                            fileCacheDict["fileMetadata"] = metadataDict;
                        }
                    }

                    // Enhance content with converted audio data if provided
                    if (fileCacheDict.TryGetValue("fileContent", out var content))
                    {
                        var contentDict = JsonSerializer.Deserialize<Dictionary<string, object>>(
                            JsonSerializer.Serialize(content));

                        if (contentDict != null)
                        {
                            // Check if converted audio data is available in conversion fields
                            if (convertedInfo.ConversionFields.TryGetValue("convertedAudioData", out var convertedDataObj) &&
                                convertedDataObj is string convertedDataString &&
                                !string.IsNullOrEmpty(convertedDataString))
                            {
                                // Replace binary data with converted audio
                                contentDict["binaryData"] = convertedDataString;
                                contentDict["convertedContent"] = true;
                                contentDict["conversionTimestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                            }
                            else if (convertedAudioData != null)
                            {
                                // Fallback to provided converted audio data
                                contentDict["binaryData"] = Convert.ToBase64String(convertedAudioData);
                                contentDict["convertedContent"] = true;
                                contentDict["conversionTimestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                            }

                            fileCacheDict["fileContent"] = contentDict;
                        }
                    }

                    originalDict["fileCacheDataObject"] = fileCacheDict;
                }
            }

            return originalDict;
        }
        catch (Exception ex)
        {
            // Return original data with error information if enhancement fails
            return new
            {
                originalCacheData = originalCacheData,
                conversionError = ex.Message,
                conversionTimestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };
        }
    }

}
