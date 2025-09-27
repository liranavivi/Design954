using Shared.Correlation;

namespace Shared.Services.Interfaces;

/// <summary>
/// Base interface for standardized HTTP client operations across all managers
/// </summary>
public interface IBaseManagerHttpClient
{
    /// <summary>
    /// Executes an HTTP GET request with standardized resilience patterns, logging, and timing
    /// </summary>
    /// <param name="url">The URL to request</param>
    /// <param name="operationName">Name of the operation for logging and telemetry</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<HttpResponseMessage> ExecuteHttpRequestAsync(string url, string operationName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an HTTP GET request with standardized resilience patterns, logging, and timing using hierarchical context
    /// </summary>
    /// <param name="url">The URL to request</param>
    /// <param name="operationName">Name of the operation for logging and telemetry</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>HTTP response message</returns>
    Task<HttpResponseMessage> ExecuteHttpRequestAsync(string url, string operationName, HierarchicalLoggingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an HTTP request and processes the response to a specific type
    /// </summary>
    /// <typeparam name="T">Type to deserialize response to</typeparam>
    /// <param name="url">The URL to request</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="entityId">Entity ID for logging (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized entity or null if not found</returns>
    Task<T?> ExecuteAndProcessResponseAsync<T>(string url, string operationName, Guid? entityId = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Executes an HTTP request and processes the response to a specific type using hierarchical context
    /// </summary>
    /// <typeparam name="T">Type to deserialize response to</typeparam>
    /// <param name="url">The URL to request</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="entityId">Entity ID for logging (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized entity or null if not found</returns>
    Task<T?> ExecuteAndProcessResponseAsync<T>(string url, string operationName, HierarchicalLoggingContext context, Guid? entityId = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Executes an HTTP request and returns a boolean result (typically for existence checks)
    /// </summary>
    /// <param name="url">The URL to request</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="entityId">Entity ID for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Boolean result from the response</returns>
    Task<bool> ExecuteEntityCheckAsync(string url, string operationName, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an HTTP request and returns a boolean result (typically for existence checks) using hierarchical context
    /// </summary>
    /// <param name="url">The URL to request</param>
    /// <param name="operationName">Name of the operation for logging</param>
    /// <param name="entityId">Entity ID for logging</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Boolean result from the response</returns>
    Task<bool> ExecuteEntityCheckAsync(string url, string operationName, Guid entityId, HierarchicalLoggingContext context, CancellationToken cancellationToken = default);
}
