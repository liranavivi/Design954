namespace Plugin.Standardizer.Utilities;

/// <summary>
/// Standardizer-specific cache helper class for standardization operations
/// Provides standardization-specific functionality for creating XML file cache data objects
/// </summary>
public static class StandardizerCacheHelper
{
    /// <summary>
    /// Create a new file cache data object with XML content and metadata (schema-compliant)
    /// </summary>
    /// <param name="originalFileName">Original file name</param>
    /// <param name="xmlContent">XML content string</param>
    /// <param name="originalContent">Original content for reference</param>
    /// <returns>Complete schema-compliant file cache data object</returns>
    public static object CreateXmlFileCacheDataObject(
        string originalFileName,
        string xmlContent,
        string originalContent)
    {
        try
        {
            // Create XML file name by changing extension
            var xmlFileName = Path.ChangeExtension(originalFileName, ".xml");

            // Convert XML content to base64 for storage
            var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xmlContent);
            var xmlBase64 = Convert.ToBase64String(xmlBytes);

            var now = DateTime.UtcNow;
            var timestamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

            // Create schema-compliant file cache data object
            var fileCacheDataObject = new
            {
                fileMetadata = new
                {
                    fileName = xmlFileName,
                    filePath = xmlFileName,
                    fileSize = xmlBytes.Length,
                    createdDate = timestamp,
                    modifiedDate = timestamp,
                    fileExtension = ".xml",
                    detectedMimeType = "application/xml",
                    fileType = "XML Document",
                    contentHash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(xmlBytes))
                },
                fileContent = new
                {
                    binaryData = xmlBase64,
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
            var errorHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("standardization_error"));
            
            return new
            {
                fileCacheDataObject = new
                {
                    fileMetadata = new
                    {
                        fileName = "error_standardized.xml",
                        filePath = "error_standardized.xml",
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
