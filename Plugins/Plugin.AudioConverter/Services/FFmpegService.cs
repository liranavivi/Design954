using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Plugin.AudioConverter.Models;
using Shared.Correlation;

namespace Plugin.AudioConverter.Services;

/// <summary>
/// Service for executing FFmpeg operations
/// </summary>
public class FFmpegService : IFFmpegService
{
    private readonly ILogger _logger;

    public FFmpegService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Convert audio data using FFmpeg
    /// </summary>
    public async Task<FFmpegExecutionResult> ConvertAudioAsync(byte[] audioData, string ffmpegArguments, HierarchicalLoggingContext context, string? ffmpegPath = null)
    {
        var ffmpegExecutable = GetFFmpegExecutable(ffmpegPath);

        _logger.LogDebugWithHierarchy(context,
            "Executing FFmpeg conversion with {DataLength} bytes using executable: {FFmpegExecutable}",
            audioData.Length, ffmpegExecutable);

        return await ExecuteFFmpegWithBytesAsync(audioData, ffmpegArguments, ffmpegExecutable, context);
    }

    /// <summary>
    /// Check if FFmpeg is available
    /// </summary>
    public bool IsFFmpegAvailable(HierarchicalLoggingContext context, string? ffmpegPath = null)
    {
        var ffmpegExecutable = GetFFmpegExecutable(ffmpegPath);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(5000); // 5 second timeout

            var available = process.ExitCode == 0;

            if (available)
            {
                _logger.LogDebugWithHierarchy(context, "FFmpeg available at: {FFmpegExecutable}", ffmpegExecutable);
            }
            else
            {
                _logger.LogWarningWithHierarchy(context, "FFmpeg check failed at: {FFmpegExecutable} (exit code: {ExitCode})", ffmpegExecutable, process.ExitCode);
            }

            return available;
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithHierarchy(context, ex, "FFmpeg availability check failed for: {FFmpegExecutable}", ffmpegExecutable);
            return false;
        }
    }

    /// <summary>
    /// Build FFmpeg command string for byte array processing
    /// </summary>
    public string BuildConversionCommand(string arguments)
    {
        var commandParts = new List<string>();

        // Add log level
        commandParts.Add("-loglevel error");

        // Add input from stdin
        commandParts.Add("-i pipe:0");

        // Add conversion arguments
        if (!string.IsNullOrEmpty(arguments))
        {
            commandParts.Add(arguments);
        }

        // Add output to stdout
        commandParts.Add("-f mp3 pipe:1");

        return string.Join(" ", commandParts);
    }

    /// <summary>
    /// Get FFmpeg executable path - custom path or default to system PATH
    /// </summary>
    private string GetFFmpegExecutable(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return customPath;
        }

        // Default to system PATH
        return "ffmpeg";
    }

    /// <summary>
    /// Execute FFmpeg process with byte array input/output and configurable path
    /// </summary>
    private async Task<FFmpegExecutionResult> ExecuteFFmpegWithBytesAsync(
        byte[] audioData,
        string ffmpegArguments,
        string ffmpegExecutable,
        HierarchicalLoggingContext context)
    {
        var command = BuildConversionCommand(ffmpegArguments);
        var startTime = DateTime.UtcNow;

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegExecutable,
                    Arguments = command,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Write audio data to stdin
            var stdinTask = Task.Run(async () =>
            {
                try
                {
                    await process.StandardInput.BaseStream.WriteAsync(audioData, 0, audioData.Length);
                    process.StandardInput.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarningWithHierarchy(context, ex, "Failed to write audio data to FFmpeg stdin");
                }
            });

            // Read output and error streams
            var outputTask = ReadAllBytesAsync(process.StandardOutput.BaseStream);
            var errorTask = process.StandardError.ReadToEndAsync();

            // Wait for process completion with timeout
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(300)); // Default 5 minutes
            var processTask = process.WaitForExitAsync();
            var completedTask = await Task.WhenAny(processTask, timeoutTask);
            var completed = completedTask == processTask;

            var duration = DateTime.UtcNow - startTime;

            // Wait for all tasks to complete
            await Task.WhenAll(stdinTask, outputTask, errorTask);

            var convertedAudioData = await outputTask;
            var standardError = await errorTask;

            return new FFmpegExecutionResult
            {
                Success = completed && process.ExitCode == 0,
                ExitCode = process.ExitCode,
                StandardOutput = $"Converted {audioData.Length} bytes to {convertedAudioData.Length} bytes",
                StandardError = standardError,
                ProcessingDurationMs = duration.TotalMilliseconds,
                Command = $"{ffmpegExecutable} {command}",
                TimedOut = !completed,
                ConvertedAudioData = convertedAudioData
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogErrorWithHierarchy(context, ex, "FFmpeg execution failed with executable: {FFmpegExecutable}", ffmpegExecutable);

            return new FFmpegExecutionResult
            {
                Success = false,
                ExitCode = -1,
                StandardError = ex.Message,
                ProcessingDurationMs = duration.TotalMilliseconds,
                Command = $"{ffmpegExecutable} {command}"
            };
        }
    }

   
    /// <summary>
    /// Helper method to read all bytes from a stream
    /// </summary>
    private static async Task<byte[]> ReadAllBytesAsync(Stream stream)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }
}
