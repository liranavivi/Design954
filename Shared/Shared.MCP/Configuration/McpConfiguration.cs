using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Configuration;
using Shared.MCP.Interfaces;
using Shared.MCP.Server;
using Shared.MCP.Transport;

namespace Shared.MCP.Configuration;

/// <summary>
/// Configuration extensions for MCP services
/// </summary>
public static class McpConfiguration
{
    /// <summary>
    /// Adds MCP server services to the service collection
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="serviceName">Service name for OpenTelemetry</param>
    /// <param name="serviceVersion">Service version for OpenTelemetry</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMcpServer(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion)
    {
        // Add OpenTelemetry observability
        services.AddOpenTelemetryObservability(configuration, serviceName, serviceVersion);

        // Add MCP transport
        services.AddSingleton<IMcpTransport, HttpSseTransport>();

        // Add base MCP server
        services.AddSingleton<IMcpServer, BaseMcpServer>();

        return services;
    }

    /// <summary>
    /// Adds MCP server services with custom providers
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="serviceName">Service name for OpenTelemetry</param>
    /// <param name="serviceVersion">Service version for OpenTelemetry</param>
    /// <param name="configureProviders">Action to configure MCP providers</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddMcpServer(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion,
        Action<IServiceCollection> configureProviders)
    {
        // Add base MCP services
        services.AddMcpServer(configuration, serviceName, serviceVersion);

        // Configure custom providers
        configureProviders(services);

        return services;
    }

    /// <summary>
    /// Adds HTTP client for communicating with Manager services
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="managerName">Name of the manager service</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddManagerHttpClient(
        this IServiceCollection services,
        IConfiguration configuration,
        string managerName)
    {
        var baseUrl = configuration[$"ManagerUrls:{managerName}"];
        if (string.IsNullOrEmpty(baseUrl))
        {
            throw new InvalidOperationException($"Manager URL not configured for: {managerName}");
        }

        services.AddHttpClient(managerName, client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "MCP.Schema/1.0.0");
        });

        return services;
    }
}

/// <summary>
/// MCP server configuration options
/// </summary>
public class McpServerOptions
{
    /// <summary>
    /// Server name
    /// </summary>
    public string Name { get; set; } = "MCP Server";

    /// <summary>
    /// Server version
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// HTTP endpoint configuration
    /// </summary>
    public McpHttpOptions Http { get; set; } = new();

    /// <summary>
    /// Whether to enable experimental features
    /// </summary>
    public bool EnableExperimental { get; set; } = false;
}

/// <summary>
/// HTTP endpoint configuration for MCP server
/// </summary>
public class McpHttpOptions
{
    /// <summary>
    /// Port to listen on
    /// </summary>
    public int Port { get; set; } = 8080;

    /// <summary>
    /// Host to bind to
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Base path for MCP endpoints
    /// </summary>
    public string BasePath { get; set; } = "/mcp";

    /// <summary>
    /// SSE endpoint path
    /// </summary>
    public string SsePath { get; set; } = "/sse";

    /// <summary>
    /// Message endpoint path
    /// </summary>
    public string MessagePath { get; set; } = "/message";
}
