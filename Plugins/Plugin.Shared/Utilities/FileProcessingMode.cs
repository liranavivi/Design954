namespace Plugin.Shared.Utilities;

/// <summary>
/// Defines the different modes for processing files after they have been read
/// Each mode specifies what to do with the original file AFTER successfully reading it
/// Shared across all file processor types for consistency
/// </summary>
public enum FileProcessingMode
{
    /// <summary>
    /// Leave the original file unchanged after reading
    /// </summary>
    LeaveUnchanged,

    /// <summary>
    /// Rename file with processed extension (e.g., .processed)
    /// </summary>
    MarkAsProcessed,

    /// <summary>
    /// Delete the original file after reading
    /// </summary>
    Delete,

    /// <summary>
    /// Move file to backup folder (original file moved)
    /// </summary>
    MoveToBackup,

    /// <summary>
    /// Copy file to backup folder (original file remains)
    /// </summary>
    CopyToBackup,

    /// <summary>
    /// Create timestamped backup copy, then mark as processed
    /// </summary>
    BackupAndMarkProcessed,

    /// <summary>
    /// Create timestamped backup copy, then delete original
    /// </summary>
    BackupAndDelete
}
