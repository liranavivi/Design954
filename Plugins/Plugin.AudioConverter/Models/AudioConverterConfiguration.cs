namespace Plugin.AudioConverter.Models;

/// <summary>
/// Configuration settings for AudioConverter operations extracted from DeliveryAssignmentModel
/// </summary>
public class AudioConverterConfiguration
{
    /// <summary>
    /// FFmpeg conversion arguments
    /// </summary>
    public string FFmpegConversionArguments { get; set; } = "-acodec libmp3lame -ab 320k -ar 44100 -ac 2";

    /// <summary>
    /// Custom path to FFmpeg executable. If null/empty, uses system PATH.
    /// Examples: "/usr/bin/ffmpeg", "C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe", "ffmpeg",
    /// </summary>
    public string? FFmpegPath { get; set; } = null;

    /// <summary>
    /// Type name of the metadata implementation to use for audio conversion
    /// If not specified, uses default ExampleAudioConverter implementation
    /// </summary>
    public string? MetadataImplementationType { get; set; }
}
