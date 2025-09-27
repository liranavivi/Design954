namespace Processor.Base.Models;

/// <summary>
/// Configuration for processor activity data cache operations
/// </summary>
public class ProcessorActivityDataCacheConfiguration
{
    /// <summary>
    /// Name of the Hazelcast map for processor activity data cache
    /// </summary>
    public string MapName { get; set; } = "processor-activity";



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
}
