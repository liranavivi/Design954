using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.MCP.Configuration;
using Shared.MCP.Interfaces;
using Shared.MCP.Models;
using Shared.MCP.Transport;
using System.Text.Json;

namespace MCP.Schema.Services;

/// <summary>
/// Hosted service that runs the MCP server with HTTP/SSE endpoints
/// </summary>
public class McpServerHostedService : IHostedService
{
    private readonly ILogger<McpServerHostedService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly McpServerOptions _options;
    private WebApplication? _webApp;
    private readonly CancellationTokenSource _cancellationTokenSource;

    public McpServerHostedService(
        ILogger<McpServerHostedService> logger,
        IServiceProvider serviceProvider,
        IOptions<McpServerOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _cancellationTokenSource = new CancellationTokenSource();
    }

    /// <summary>
    /// Starts the MCP server
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MCP Server on {Host}:{Port}", _options.Http.Host, _options.Http.Port);

        try
        {
            var builder = WebApplication.CreateBuilder();
            
            // Configure services
            builder.Services.AddSingleton(_serviceProvider.GetRequiredService<IMcpServer>());
            builder.Services.AddSingleton(_serviceProvider.GetRequiredService<IMcpTransport>());
            builder.Services.AddLogging(logging => logging.AddConsole());

            // Configure Kestrel
            builder.WebHost.UseKestrel(options =>
            {
                options.ListenAnyIP(_options.Http.Port);
            });

            _webApp = builder.Build();

            // Configure middleware and endpoints
            ConfigureEndpoints(_webApp);

            // Start the transport
            var transport = _serviceProvider.GetRequiredService<IMcpTransport>();
            await transport.StartAsync(cancellationToken);

            // Start the web application
            await _webApp.StartAsync(cancellationToken);

            _logger.LogInformation("MCP Server started successfully on {Host}:{Port}", _options.Http.Host, _options.Http.Port);
            _logger.LogInformation("SSE endpoint: http://{Host}:{Port}{BasePath}{SsePath}", 
                _options.Http.Host, _options.Http.Port, _options.Http.BasePath, _options.Http.SsePath);
            _logger.LogInformation("Message endpoint: http://{Host}:{Port}{BasePath}{MessagePath}", 
                _options.Http.Host, _options.Http.Port, _options.Http.BasePath, _options.Http.MessagePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP Server");
            throw;
        }
    }

    /// <summary>
    /// Stops the MCP server
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MCP Server");

        try
        {
            _cancellationTokenSource.Cancel();

            if (_webApp != null)
            {
                await _webApp.StopAsync(cancellationToken);
                await _webApp.DisposeAsync();
            }

            var transport = _serviceProvider.GetRequiredService<IMcpTransport>();
            await transport.StopAsync(cancellationToken);

            _logger.LogInformation("MCP Server stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping MCP Server");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }

    /// <summary>
    /// Configures HTTP endpoints for the MCP server
    /// </summary>
    private void ConfigureEndpoints(WebApplication app)
    {
        // Add CORS for development
        app.UseCors(policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());

        var basePath = _options.Http.BasePath;

        // Health check endpoint
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "MCP.Schema" }));

        // Server info endpoint
        app.MapGet($"{basePath}/info", (IMcpServer mcpServer) =>
        {
            var capabilities = mcpServer.GetCapabilities();
            return Results.Ok(new
            {
                name = _options.Name,
                version = _options.Version,
                protocol = "2024-11-05",
                capabilities
            });
        });

        // SSE endpoint for real-time communication
        app.MapGet($"{basePath}{_options.Http.SsePath}", async (HttpContext context, IMcpTransport transport) =>
        {
            if (transport is HttpSseTransport sseTransport)
            {
                await sseTransport.HandleSseConnectionAsync(context, _cancellationTokenSource.Token);
            }
            else
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("SSE transport not available");
            }
        });

        // Message endpoint for JSON-RPC requests
        app.MapPost($"{basePath}{_options.Http.MessagePath}", async (HttpContext context, IMcpServer mcpServer, IMcpTransport transport) =>
        {
            try
            {
                if (transport is HttpSseTransport sseTransport)
                {
                    await sseTransport.HandleHttpPostAsync(context, _cancellationTokenSource.Token);
                }
                else
                {
                    // Fallback to direct processing
                    using var reader = new StreamReader(context.Request.Body);
                    var messageJson = await reader.ReadToEndAsync();
                    
                    var request = JsonSerializer.Deserialize<McpRequest>(messageJson);
                    if (request != null)
                    {
                        var response = await mcpServer.ProcessRequestAsync(request, _cancellationTokenSource.Token);
                        var responseJson = JsonSerializer.Serialize(response);
                        
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(responseJson);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid JSON-RPC request");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MCP message");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Internal server error");
            }
        });

        _logger.LogDebug("MCP endpoints configured with base path: {BasePath}", basePath);
    }
}
