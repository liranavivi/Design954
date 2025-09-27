using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Shared.Correlation;
using Shared.Services.Interfaces;

namespace Shared.Services;

/// <summary>
/// Base HTTP client implementation with standardized resilience patterns, logging, and timing
/// Provides common functionality for all manager HTTP clients
/// </summary>
public abstract class BaseManagerHttpClient : IBaseManagerHttpClient
{
    protected readonly HttpClient _httpClient;
    protected readonly ILogger _logger;
    protected readonly IConfiguration _configuration;
    protected readonly IAsyncPolicy<HttpResponseMessage> _resilientPolicy;
    protected readonly JsonSerializerOptions _jsonOptions;

    protected BaseManagerHttpClient(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        // Configure resilience policy
        _resilientPolicy = CreateResilientPolicy();
    }

    /// <summary>
    /// Executes an HTTP GET request with standardized resilience patterns, logging, and timing
    /// </summary>
    public virtual async Task<HttpResponseMessage> ExecuteHttpRequestAsync(string url, string operationName, CancellationToken cancellationToken = default)
    {
        return await _resilientPolicy.ExecuteAsync(async () =>
        {
            var requestStopwatch = Stopwatch.StartNew();
            var httpResponse = await _httpClient.GetAsync(url, cancellationToken);
            requestStopwatch.Stop();

            _logger.LogInformationWithCorrelation(
                "HTTP request completed. Operation: {OperationName}, Url: {Url}, StatusCode: {StatusCode}, Duration: {DurationMs}ms",
                operationName, url, httpResponse.StatusCode, requestStopwatch.ElapsedMilliseconds);

            return httpResponse;
        });
    }

    /// <summary>
    /// Executes an HTTP GET request with standardized resilience patterns, logging, and timing using hierarchical context
    /// </summary>
    public virtual async Task<HttpResponseMessage> ExecuteHttpRequestAsync(string url, string operationName, HierarchicalLoggingContext context, CancellationToken cancellationToken = default)
    {
        return await _resilientPolicy.ExecuteAsync(async () =>
        {
            var requestStopwatch = Stopwatch.StartNew();
            var httpResponse = await _httpClient.GetAsync(url, cancellationToken);
            requestStopwatch.Stop();

            _logger.LogInformationWithHierarchy(context,
                "HTTP request completed. Operation: {OperationName}, Url: {Url}, StatusCode: {StatusCode}, Duration: {DurationMs}ms",
                operationName ?? "Unknown", url ?? "Unknown", httpResponse.StatusCode, requestStopwatch.ElapsedMilliseconds);

            return httpResponse;
        });
    }

    /// <summary>
    /// Executes an HTTP request and processes the response to a specific type
    /// </summary>
    public virtual async Task<T?> ExecuteAndProcessResponseAsync<T>(string url, string operationName, Guid? entityId = null, CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await ExecuteHttpRequestAsync(url, operationName, cancellationToken);
            var entity = await ProcessResponseAsync<T>(response, url, operationName, entityId);

            stopwatch.Stop();

            if (entity != null)
            {
                _logger.LogInformationWithCorrelation(
                    "Successfully retrieved {OperationName}. EntityId: {EntityId}, TotalDuration: {TotalDurationMs}ms",
                    operationName, entityId, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformationWithCorrelation(
                    "{OperationName} not found. EntityId: {EntityId}, TotalDuration: {TotalDurationMs}ms",
                    operationName, entityId, stopwatch.ElapsedMilliseconds);
            }

            return entity;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithCorrelation(ex,
                "Failed to retrieve {OperationName}. EntityId: {EntityId}, TotalDuration: {TotalDurationMs}ms",
                operationName, entityId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Executes an HTTP request and processes the response to a specific type using hierarchical context
    /// </summary>
    public virtual async Task<T?> ExecuteAndProcessResponseAsync<T>(string url, string operationName, HierarchicalLoggingContext context, Guid? entityId = null, CancellationToken cancellationToken = default) where T : class
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response = await ExecuteHttpRequestAsync(url, operationName, context, cancellationToken);
            var entity = await ProcessResponseAsync<T>(response, url, operationName, context, entityId);

            stopwatch.Stop();

            if (entity != null)
            {
                _logger.LogInformationWithHierarchy(context,
                    "Successfully retrieved {OperationName}. EntityId: {EntityId}, TotalDuration: {TotalDurationMs}ms",
                    operationName ?? "Unknown", entityId?.ToString() ?? "None", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformationWithHierarchy(context,
                    "{OperationName} not found. EntityId: {EntityId}, TotalDuration: {TotalDurationMs}ms",
                    operationName ?? "Unknown", entityId?.ToString() ?? "None", stopwatch.ElapsedMilliseconds);
            }

            return entity;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to retrieve {OperationName}. EntityId: {EntityId}, TotalDuration: {TotalDurationMs}ms",
                operationName ?? "Unknown", entityId?.ToString() ?? "None", stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// Executes an HTTP request and returns a boolean result (typically for existence checks)
    /// </summary>
    public virtual async Task<bool> ExecuteEntityCheckAsync(string url, string operationName, Guid entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebugWithCorrelation("Starting {Operation} for EntityId: {EntityId}, URL: {Url}",
                operationName, entityId, url);

            var response = await ExecuteHttpRequestAsync(url, operationName, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var hasReferences = bool.Parse(content);

                _logger.LogDebugWithCorrelation("Completed {Operation} for EntityId: {EntityId}. HasReferences: {HasReferences}",
                    operationName, entityId, hasReferences);

                return hasReferences;
            }
            else
            {
                _logger.LogErrorWithCorrelation("Failed {Operation} for EntityId: {EntityId}. StatusCode: {StatusCode}, URL: {Url}",
                    operationName, entityId, response.StatusCode, url);

                // Fail-safe approach: if we can't validate, assume there are references
                throw new InvalidOperationException($"Entity validation service unavailable. StatusCode: {response.StatusCode}");
            }
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Circuit breaker is open for {Operation}. EntityId: {EntityId}",
                operationName, entityId);

            // Fail-safe: assume references exist when circuit is open
            throw new InvalidOperationException($"Service temporarily unavailable due to circuit breaker. Operation: {operationName}");
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Unexpected error during {Operation} for EntityId: {EntityId}",
                operationName, entityId);
            throw;
        }
    }

    /// <summary>
    /// Executes an HTTP request and returns a boolean result (typically for existence checks) using hierarchical context
    /// </summary>
    public virtual async Task<bool> ExecuteEntityCheckAsync(string url, string operationName, Guid entityId, HierarchicalLoggingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebugWithHierarchy(context, "Starting {Operation} for EntityId: {EntityId}, URL: {Url}",
                operationName ?? "Unknown", entityId, url ?? "Unknown");

            var response = await ExecuteHttpRequestAsync(url ?? string.Empty, operationName ?? string.Empty, context, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var hasReferences = bool.Parse(content);

                _logger.LogDebugWithHierarchy(context, "Completed {Operation} for EntityId: {EntityId}. HasReferences: {HasReferences}",
                    operationName ?? "Unknown", entityId, hasReferences);

                return hasReferences;
            }
            else
            {
                _logger.LogErrorWithHierarchy(context, "Failed {Operation} for EntityId: {EntityId}. StatusCode: {StatusCode}, URL: {Url}",
                    operationName ?? "Unknown", entityId, response.StatusCode, url ?? "Unknown");

                // Fail-safe approach: if we can't validate, assume there are references
                throw new InvalidOperationException($"Entity validation service unavailable. StatusCode: {response.StatusCode}");
            }
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Circuit breaker is open for {Operation}. EntityId: {EntityId}",
                operationName ?? "Unknown", entityId);

            // Fail-safe: assume references exist when circuit is open
            throw new InvalidOperationException($"Service temporarily unavailable due to circuit breaker. Operation: {operationName}");
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "Unexpected error during {Operation} for EntityId: {EntityId}",
                operationName ?? "Unknown", entityId);
            throw;
        }
    }

    /// <summary>
    /// Standardized response processing with consistent error handling
    /// </summary>
    protected virtual async Task<T?> ProcessResponseAsync<T>(HttpResponseMessage response, string url, string operationName, Guid? entityId = null) where T : class
    {
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var entity = JsonSerializer.Deserialize<T>(content, _jsonOptions);
            return entity;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformationWithCorrelation(
                "{OperationName} returned 404 Not Found. EntityId: {EntityId}, URL: {Url}",
                operationName, entityId, url);
            return null;
        }

        _logger.LogWarningWithCorrelation(
            "{OperationName} returned non-success status. EntityId: {EntityId}, StatusCode: {StatusCode}, URL: {Url}",
            operationName, entityId, response.StatusCode, url);

        throw new HttpRequestException($"HTTP request failed with status {response.StatusCode}");
    }

    /// <summary>
    /// Standardized response processing with consistent error handling using hierarchical context
    /// </summary>
    protected virtual async Task<T?> ProcessResponseAsync<T>(HttpResponseMessage response, string url, string operationName, HierarchicalLoggingContext context, Guid? entityId = null) where T : class
    {
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var entity = JsonSerializer.Deserialize<T>(content, _jsonOptions);
            return entity;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformationWithHierarchy(context,
                "{OperationName} returned 404 Not Found. EntityId: {EntityId}, URL: {Url}",
                operationName ?? "Unknown", entityId?.ToString() ?? "None", url ?? "Unknown");
            return null;
        }

        _logger.LogWarningWithHierarchy(context,
            "{OperationName} returned non-success status. EntityId: {EntityId}, StatusCode: {StatusCode}, URL: {Url}",
            operationName ?? "Unknown", entityId?.ToString() ?? "None", response.StatusCode, url ?? "Unknown");

        throw new HttpRequestException($"HTTP request failed with status {response.StatusCode}");
    }

    /// <summary>
    /// Creates the resilient policy with retry and circuit breaker patterns
    /// Can be overridden by derived classes for custom resilience requirements
    /// </summary>
    protected virtual IAsyncPolicy<HttpResponseMessage> CreateResilientPolicy()
    {
        // Configure retry policy with exponential backoff
        var retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => 
                // Only retry on server errors (5xx) and specific transient client errors
                r.StatusCode >= System.Net.HttpStatusCode.InternalServerError || // 500+
                r.StatusCode == System.Net.HttpStatusCode.RequestTimeout || // 408
                r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)  // 429
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .WaitAndRetryAsync(
                retryCount: _configuration.GetValue<int>("HttpClient:MaxRetries", 3),
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
                    _configuration.GetValue<int>("HttpClient:RetryDelayMs", 1000) * Math.Pow(2, retryAttempt - 1)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarningWithCorrelation(
                        "HTTP request failed, retrying in {Delay}ms. Attempt {RetryCount}/{MaxRetries}. Reason: {Reason}",
                        timespan.TotalMilliseconds, retryCount, _configuration.GetValue<int>("HttpClient:MaxRetries", 3),
                        outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
                });

        // Configure circuit breaker policy
        var circuitBreakerPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .Or<HttpRequestException>()
            .Or<TaskCanceledException>()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: _configuration.GetValue<int>("HttpClient:CircuitBreakerThreshold", 3),
                durationOfBreak: TimeSpan.FromSeconds(_configuration.GetValue<int>("HttpClient:CircuitBreakerDurationSeconds", 30)),
                onBreak: (exception, duration) =>
                {
                    _logger.LogErrorWithCorrelation("Circuit breaker opened for {Duration}s. Reason: {Reason}",
                        duration.TotalSeconds, exception.Exception?.Message ?? exception.Result?.StatusCode.ToString());
                },
                onReset: () =>
                {
                    _logger.LogInformationWithCorrelation("Circuit breaker reset - service is healthy again");
                });

        // Combine policies: retry first, then circuit breaker
        return Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);
    }
}
