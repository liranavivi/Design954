namespace Plugin.Enricher.Utilities;

/// <summary>
/// Enricher-specific cache helper class for enrichment operations
/// Provides enrichment-specific functionality for creating enriched XML file cache data objects
/// </summary>
public static class EnricherCacheHelper
{
    /// <summary>
    /// Create a new file cache data object with enriched content and metadata (schema-compliant)
    /// </summary>
    /// <param name="originalFileName">Original file name</param>
    /// <param name="enrichedContent">Enriched content string</param>
    /// <param name="originalContent">Original content for reference</param>
    /// <returns>Complete schema-compliant file cache data object</returns>
    public static object CreateEnrichedFileCacheDataObject(
        string originalFileName,
        string enrichedContent,
        string originalContent)
    {
        try
        {
            // Create enriched file name by changing extension to .xml (enriched format)
            var enrichedFileName = Path.ChangeExtension(originalFileName, ".xml");

            // Convert enriched content to base64 for storage
            var enrichedBytes = System.Text.Encoding.UTF8.GetBytes(enrichedContent);
            var enrichedBase64 = Convert.ToBase64String(enrichedBytes);

            var now = DateTime.UtcNow;
            var timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            // Create schema-compliant file cache data object
            var fileCacheDataObject = new
            {
                fileMetadata = new
                {
                    fileName = enrichedFileName,
                    filePath = enrichedFileName,
                    fileSize = enrichedBytes.Length,
                    createdDate = timestamp,
                    modifiedDate = timestamp,
                    fileExtension = ".xml",
                    detectedMimeType = "application/xml",
                    fileType = "XML Document",
                    contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(enrichedBytes))
                },
                fileContent = new
                {
                    binaryData = enrichedBase64,
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
            var errorHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("enrichment_error"));
            
            return new
            {
                fileCacheDataObject = new
                {
                    fileMetadata = new
                    {
                        fileName = "error_enriched.xml",
                        filePath = "error_enriched.xml",
                        fileSize = 0,
                        createdDate = errorTimestamp,
                        modifiedDate = errorTimestamp,
                        fileExtension = ".xml",
                        detectedMimeType = "application/xml",
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
