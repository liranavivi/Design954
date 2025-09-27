using Plugin.Shared.Utilities;

namespace Plugin.FileWriter.Models;

/// <summary>
/// Configuration extracted from PluginAssignmentModel for compressed file writing operations
/// </summary>
public class FileWriterConfiguration : IFileProcessingConfiguration
{
    public string FolderPath { get; set; } = string.Empty;
    public string SearchPattern { get; set; } = "*.{txt,zip,rar,7z,gz,tar}";

    public long MinExtractedContentSize { get; set; } = 0; // Filter for each extracted file content
    public long MaxExtractedContentSize { get; set; } = long.MaxValue; // Filter for each extracted file content

    public FileProcessingMode ProcessingMode { get; set; } = FileProcessingMode.LeaveUnchanged;
    public string ProcessedExtension { get; set; } = ".written";
    public string BackupFolder { get; set; } = string.Empty;
}
