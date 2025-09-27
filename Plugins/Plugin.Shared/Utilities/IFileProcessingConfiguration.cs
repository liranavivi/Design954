namespace Plugin.Shared.Utilities;

/// <summary>
/// Interface for file processing configurations to enable shared post-processing utilities
/// </summary>
public interface IFileProcessingConfiguration
{
    /// <summary>
    /// Processing mode that determines what to do with files after processing
    /// </summary>
    FileProcessingMode ProcessingMode { get; set; }

    /// <summary>
    /// Extension to add when marking files as processed
    /// </summary>
    string ProcessedExtension { get; set; }

    /// <summary>
    /// Backup folder path for backup operations
    /// </summary>
    string BackupFolder { get; set; }
}
