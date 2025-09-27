namespace Plugin.PreFileReader.Models;

/// <summary>
/// Configuration extracted from PluginAssignmentModel for file discovery operations
/// </summary>
public class PreFileReaderConfiguration
{
    public string FolderPath { get; set; } = string.Empty;
    public string SearchPattern { get; set; } = "*.{txt,zip,rar,7z,gz,tar}";
    public int MaxFilesToProcess { get; set; } = 50; // Maximum number of files to discover
}
