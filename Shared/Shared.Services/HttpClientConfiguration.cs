namespace Shared.Services;

/// <summary>
/// Configuration options for HTTP client resilience patterns
/// </summary>
public class HttpClientConfiguration
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "HttpClient";

    /// <summary>
    /// Maximum number of retry attempts
    /// Default: 3
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff
    /// Default: 1000ms (1 second)
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Number of consecutive failures before circuit breaker opens
    /// Default: 3
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 3;

    /// <summary>
    /// Duration in seconds that circuit breaker stays open
    /// Default: 30 seconds
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    /// <summary>
    /// HTTP client timeout in seconds
    /// Default: 30 seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to include detailed timing metrics in logs
    /// Default: true
    /// </summary>
    public bool EnableDetailedMetrics { get; set; } = true;
}
