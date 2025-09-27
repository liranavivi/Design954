namespace Plugin.PreFileReader.Models;

/// <summary>
/// Configuration for file registration cache operations
/// Specific to PreFileReaderPlugin for tracking discovered and processed files
/// </summary>
public class FileRegistrationCacheConfiguration
{
    /// <summary>
    /// Name of the Hazelcast map for file registration cache
    /// </summary>
    public string MapName { get; set; } = "file-registration";

    /// <summary>
    /// Time-to-live for file registration entries in milliseconds
    /// Default: 30 seconds (30000 ms)
    /// </summary>
    public int TtlMilliseconds { get; set; } = 30000;

    /// <summary>
    /// Maximum number of retry attempts for cache operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether to use exponential backoff for retries
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Whether to continue processing if cache operations fail
    /// </summary>
    public bool ContinueOnCacheFailure { get; set; } = false;

    /// <summary>
    /// Whether to log cache operations for debugging
    /// </summary>
    public bool LogCacheOperations { get; set; } = true;

    /// <summary>
    /// Log level for cache operations (Information, Debug, Warning, etc.)
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// Gets the TTL as a TimeSpan
    /// </summary>
    public TimeSpan GetTtl() => TimeSpan.FromMilliseconds(TtlMilliseconds);
}
