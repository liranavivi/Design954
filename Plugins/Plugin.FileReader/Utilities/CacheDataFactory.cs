using Processor.Base.Models;

namespace Plugin.FileReader.Utilities;

/// <summary>
/// Factory for creating universal cache data objects
/// Specific to FileReaderPlugin for creating consistent cache data structures
/// for compressed files and their extracted content
/// </summary>
public static class CacheDataFactory
{
    /// <summary>
    /// Creates a universal fileCacheDataObject from ProcessedFileInfo
    /// Used by FileReader and for individual extracted files in FileReader
    /// </summary>
    /// <param name="fileInfo">File information to convert to cache data</param>
    /// <returns>Universal cache data object with consistent structure</returns>
    public static object CreateFileCacheDataObject(ProcessedFileInfo fileInfo)
    {
        if (fileInfo == null)
            throw new ArgumentNullException(nameof(fileInfo));

        return new
        {
            fileCacheDataObject = new
            {
                fileMetadata = new
                {
                    fileName = fileInfo.FileName,
                    filePath = fileInfo.FilePath,
                    fileSize = fileInfo.FileSize,
                    createdDate = fileInfo.CreatedDate,
                    modifiedDate = fileInfo.ModifiedDate,
                    fileExtension = fileInfo.FileExtension,
                    detectedMimeType = fileInfo.DetectedMimeType,
                    fileType = fileInfo.FileType,
                    contentHash = fileInfo.ContentHash
                },
                fileContent = new
                {
                    binaryData = Convert.ToBase64String(fileInfo.FileContent), // Convert binary to Base64 for JSON storage
                    encoding = "base64" // Always base64 in cache
                }
            }
        };
    }

    /// <summary>
    /// Creates universal FileCacheData structure
    /// Used by FileReader to create consistent cache data
    /// </summary>
    /// <param name="originalFile">Original compressed file information</param>
    /// <param name="extractedFiles">List of extracted files from the archive</param>
    /// <returns>Universal compressed file cache data object</returns>
    public static object CreateFileCacheDataObject(
        ProcessedFileInfo originalFile,
        List<ProcessedFileInfo> extractedFiles)
    {
        if (originalFile == null)
            throw new ArgumentNullException(nameof(originalFile));
        if (extractedFiles == null)
            throw new ArgumentNullException(nameof(extractedFiles));

        return new
        {
            // Original compressed file as universal fileCacheDataObject
            FileCacheDataObject = CreateFileCacheDataObject(originalFile),

            // Extracted files as array of universal fileCacheDataObject
            ExtractedFileCacheDataObject = extractedFiles
                .Select(file => CreateFileCacheDataObject(file))
                .ToArray()
        };
    }
}
