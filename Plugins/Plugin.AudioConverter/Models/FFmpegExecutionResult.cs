namespace Plugin.AudioConverter.Models;

/// <summary>
/// Result of FFmpeg process execution
/// </summary>
public class FFmpegExecutionResult
{
    /// <summary>
    /// Indicates if the FFmpeg execution was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Exit code from the FFmpeg process
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Standard output from FFmpeg
    /// </summary>
    public string StandardOutput { get; set; } = "";

    /// <summary>
    /// Standard error from FFmpeg
    /// </summary>
    public string StandardError { get; set; } = "";

    /// <summary>
    /// Processing duration in milliseconds
    /// </summary>
    public double ProcessingDurationMs { get; set; }

    /// <summary>
    /// The FFmpeg command that was executed
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>
    /// Indicates if the process timed out
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// Converted audio data as byte array
    /// </summary>
    public byte[]? ConvertedAudioData { get; set; }
}
