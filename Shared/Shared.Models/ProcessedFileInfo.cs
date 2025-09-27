namespace Processor.Base.Models;

/// <summary>
/// File metadata and content information
/// </summary>
public class ProcessedFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string DetectedMimeType { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public byte[] FileContent { get; set; } = Array.Empty<byte>();
    public string ContentHash { get; set; } = string.Empty;
    public string ContentEncoding { get; set; } = "binary"; // "binary" or "base64"
}
