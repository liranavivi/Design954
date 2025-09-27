using Plugin.Shared.Utilities;

namespace Plugin.FileReader.Models;

/// <summary>
/// Configuration extracted from PluginAssignmentModel for file reading operations
/// </summary>
public class FileReaderConfiguration : IFileProcessingConfiguration
{
    public long MinFileSize { get; set; } = 0; // Filter for main file size
    public long MaxFileSize { get; set; } = 100 * 1024 * 1024; // 100MB for main files
    public long MinExtractedSize { get; set; } = 0; // Filter for each extracted file
    public long MaxExtractedSize { get; set; } = 50 * 1024 * 1024; // 50MB for each extracted file

    public FileProcessingMode ProcessingMode { get; set; } = FileProcessingMode.LeaveUnchanged;
    public string ProcessedExtension { get; set; } = ".processed";
    public string BackupFolder { get; set; } = string.Empty;

    public int MinEntriesToList { get; set; } = 0; // Minimum extracted files to process
    public int MaxEntriesToList { get; set; } = 100; // Maximum extracted files to process
}
