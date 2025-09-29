using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shared.MCP.Interfaces;
using System.Text;
using System.Threading.Channels;

namespace Shared.MCP.Transport;

/// <summary>
/// HTTP Server-Sent Events transport for MCP communication
/// </summary>
public class HttpSseTransport : IMcpTransport
{
    private readonly ILogger<HttpSseTransport> _logger;
    private readonly Channel<string> _messageChannel;
    private readonly ChannelWriter<string> _messageWriter;
    private readonly ChannelReader<string> _messageReader;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _processingTask;

    public HttpSseTransport(ILogger<HttpSseTransport> logger)
    {
        _logger = logger;
        var channel = Channel.CreateUnbounded<string>();
        _messageChannel = channel;
        _messageWriter = channel.Writer;
        _messageReader = channel.Reader;
    }

    /// <summary>
    /// Event raised when a message is received
    /// </summary>
    public event Func<string, Task>? MessageReceived;

    /// <summary>
    /// Starts the transport layer
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting HTTP SSE transport");
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessMessagesAsync(_cancellationTokenSource.Token);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the transport layer
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping HTTP SSE transport");
        
        _cancellationTokenSource?.Cancel();
        _messageWriter.Complete();

        if (_processingTask != null)
        {
            try
            {
                await _processingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }

        _cancellationTokenSource?.Dispose();
    }

    /// <summary>
    /// Sends a message through the transport
    /// </summary>
    public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            await _messageWriter.WriteAsync(message, cancellationToken);
            _logger.LogDebug("Message queued for sending: {MessageLength} characters", message.Length);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to queue message - channel may be closed");
            throw;
        }
    }

    /// <summary>
    /// Handles incoming HTTP request for SSE connection
    /// </summary>
    public async Task HandleSseConnectionAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        context.Response.Headers["Content-Type"] = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Cache-Control";

        _logger.LogInformation("SSE connection established from {RemoteIpAddress}", context.Connection.RemoteIpAddress);

        try
        {
            // Send initial connection message
            await SendSseEventAsync(context.Response, "connected", "MCP Server connected", cancellationToken);

            // Process outgoing messages
            await foreach (var message in _messageReader.ReadAllAsync(cancellationToken))
            {
                await SendSseEventAsync(context.Response, "message", message, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("SSE connection cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SSE connection");
        }
        finally
        {
            _logger.LogInformation("SSE connection closed");
        }
    }

    /// <summary>
    /// Handles incoming HTTP POST request with MCP message
    /// </summary>
    public async Task HandleHttpPostAsync(HttpContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var message = await reader.ReadToEndAsync(cancellationToken);
            
            _logger.LogDebug("Received HTTP message: {MessageLength} characters", message.Length);

            if (MessageReceived != null)
            {
                await MessageReceived(message);
            }

            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling HTTP POST request");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal Server Error", cancellationToken);
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Started message processing task");
        
        try
        {
            await foreach (var message in _messageReader.ReadAllAsync(cancellationToken))
            {
                _logger.LogDebug("Processing outgoing message: {MessageLength} characters", message.Length);
                // Messages are handled by the SSE connection handler
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message processing cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in message processing");
        }
    }

    private static async Task SendSseEventAsync(HttpResponse response, string eventType, string data, CancellationToken cancellationToken)
    {
        var sseData = $"event: {eventType}\ndata: {data}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);
        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
