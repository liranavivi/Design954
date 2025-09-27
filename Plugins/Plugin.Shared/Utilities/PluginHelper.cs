using System.Text.Json;

namespace Plugin.Shared.Utilities;

/// <summary>
/// Shared utility helper class for plugin operations
/// Provides common functionality for cache data manipulation and validation across all plugins
/// </summary>
public static class PluginHelper
{
    /// <summary>
    /// Extract file metadata from cache data object
    /// </summary>
    /// <param name="cacheDataItem">Cache data item from previous plugin</param>
    /// <returns>File metadata object or null if not found</returns>
    public static object? ExtractFileMetadata(JsonElement cacheDataItem)
    {
        try
        {
            if (cacheDataItem.TryGetProperty("fileCacheDataObject", out var fileCacheData))
            {
                if (fileCacheData.TryGetProperty("fileMetadata", out var metadata))
                {
                    return metadata;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extract file content from cache data object
    /// </summary>
    /// <param name="cacheDataItem">Cache data item from previous plugin</param>
    /// <returns>File content object or null if not found</returns>
    public static object? ExtractFileContent(JsonElement cacheDataItem)
    {
        try
        {
            if (cacheDataItem.TryGetProperty("fileCacheDataObject", out var fileCacheData))
            {
                if (fileCacheData.TryGetProperty("fileContent", out var content))
                {
                    return content;
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get file name from cache data for logging purposes
    /// </summary>
    /// <param name="cacheDataItem">Cache data item</param>
    /// <returns>File name or "Unknown" if not found</returns>
    public static string GetFileNameFromCacheData(JsonElement cacheDataItem)
    {
        try
        {
            var metadata = ExtractFileMetadata(cacheDataItem);
            if (metadata != null)
            {
                var metadataElement = (JsonElement)metadata;
                if (metadataElement.TryGetProperty("fileName", out var fileName))
                {
                    return fileName.GetString() ?? "Unknown";
                }
            }
            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Get file extension from file name
    /// </summary>
    /// <param name="fileName">File name</param>
    /// <returns>File extension (including dot) or empty string</returns>
    public static string GetFileExtension(string fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName)) return "";
            var lastDotIndex = fileName.LastIndexOf('.');
            return lastDotIndex >= 0 ? fileName.Substring(lastDotIndex) : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Get MIME type from cache data object
    /// </summary>
    /// <param name="cacheDataItem">Cache data item</param>
    /// <returns>MIME type or empty string if not found</returns>
    public static string GetMimeType(JsonElement cacheDataItem)
    {
        try
        {
            var metadata = ExtractFileMetadata(cacheDataItem);
            if (metadata != null)
            {
                var metadataElement = (JsonElement)metadata;
                if (metadataElement.TryGetProperty("detectedMimeType", out var mimeType))
                {
                    return mimeType.GetString() ?? "";
                }
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Get file size from cache data object
    /// </summary>
    /// <param name="cacheDataItem">Cache data item</param>
    /// <returns>File size in bytes or 0 if not found</returns>
    public static long GetFileSize(JsonElement cacheDataItem)
    {
        try
        {
            var metadata = ExtractFileMetadata(cacheDataItem);
            if (metadata != null)
            {
                var metadataElement = (JsonElement)metadata;
                if (metadataElement.TryGetProperty("fileSize", out var fileSize))
                {
                    return fileSize.GetInt64();
                }
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Check if cache data item has valid file metadata
    /// </summary>
    /// <param name="cacheDataItem">Cache data item</param>
    /// <returns>True if valid metadata exists, false otherwise</returns>
    public static bool HasValidFileMetadata(JsonElement cacheDataItem)
    {
        try
        {
            var metadata = ExtractFileMetadata(cacheDataItem);
            if (metadata == null) return false;

            var metadataElement = (JsonElement)metadata;
            return metadataElement.TryGetProperty("fileName", out _) &&
                   metadataElement.TryGetProperty("fileSize", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if cache data item has valid file content
    /// </summary>
    /// <param name="cacheDataItem">Cache data item</param>
    /// <returns>True if valid content exists, false otherwise</returns>
    public static bool HasValidFileContent(JsonElement cacheDataItem)
    {
        try
        {
            var content = ExtractFileContent(cacheDataItem);
            if (content == null) return false;

            var contentElement = (JsonElement)content;
            return contentElement.TryGetProperty("binaryData", out _) ||
                   contentElement.TryGetProperty("textContent", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validate cache data item structure
    /// </summary>
    /// <param name="cacheDataItem">Cache data item</param>
    /// <returns>True if structure is valid, false otherwise</returns>
    public static bool IsValidCacheDataItem(JsonElement cacheDataItem)
    {
        try
        {
            return cacheDataItem.TryGetProperty("fileCacheDataObject", out _) &&
                   HasValidFileMetadata(cacheDataItem) &&
                   HasValidFileContent(cacheDataItem);
        }
        catch
        {
            return false;
        }
    }
}
