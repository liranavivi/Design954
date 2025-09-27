using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Correlation;

namespace Shared.Services;

/// <summary>
/// Extension methods for configuring HTTP clients with standardized resilience patterns
/// </summary>
public static class HttpClientServiceExtensions
{
    /// <summary>
    /// Adds a standardized HTTP client with correlation ID support
    /// Note: Resilience patterns are handled by the BaseManagerHttpClient implementation
    /// </summary>
    /// <typeparam name="TClient">The HTTP client type</typeparam>
    /// <typeparam name="TImplementation">The HTTP client implementation type</typeparam>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="clientName">Optional client name (defaults to type name)</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddStandardizedHttpClient<TClient, TImplementation>(
        this IServiceCollection services,
        IConfiguration configuration,
        string? clientName = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        // Configure HTTP client options
        services.Configure<HttpClientConfiguration>(configuration.GetSection(HttpClientConfiguration.SectionName));

        // Register the implementation
        services.AddScoped<TClient, TImplementation>();

        // Configure HTTP client
        var httpClientBuilder = services.AddHttpClient<TImplementation>(clientName ?? typeof(TImplementation).Name, client =>
        {
            var httpConfig = configuration.GetSection(HttpClientConfiguration.SectionName).Get<HttpClientConfiguration>()
                           ?? new HttpClientConfiguration();

            client.Timeout = TimeSpan.FromSeconds(httpConfig.TimeoutSeconds);
        });

        // Add correlation ID handler
        httpClientBuilder.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();

        return services;
    }

    /// <summary>
    /// Adds correlation ID support to HTTP clients
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>The service collection for method chaining</returns>
    public static IServiceCollection AddHttpClientCorrelationSupport(this IServiceCollection services)
    {
        services.AddTransient<CorrelationIdDelegatingHandler>();
        return services;
    }
}
