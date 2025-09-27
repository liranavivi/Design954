using Microsoft.Extensions.Logging;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Shared.Correlation;
using System.Threading.Channels;

namespace Processor.Base.Services;

/// <summary>
/// Thread-safe in-memory queue implementation for response processing
/// Follows the same pattern as RequestProcessingQueue with concurrent worker support
/// </summary>
public class ResponseProcessingQueue : IResponseProcessingQueue
{
    private readonly ChannelWriter<ProcessedResponseItem> _writer;
    private readonly ChannelReader<ProcessedResponseItem> _reader;
    private readonly ILogger<ResponseProcessingQueue> _logger;
    private int _queueDepth = 0;

    public ResponseProcessingQueue(ILogger<ResponseProcessingQueue> logger)
    {
        _logger = logger;

        // Create bounded channel with backpressure (same configuration as RequestProcessingQueue)
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait, // Backpressure to consumer
            SingleReader = false, // Multiple processing threads can read
            SingleWriter = false  // Multiple consumer threads can write
        };

        var channel = Channel.CreateBounded<ProcessedResponseItem>(options);
        _writer = channel.Writer;
        _reader = channel.Reader;

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Gets the channel reader for the background service
    /// </summary>
    public ChannelReader<ProcessedResponseItem> Reader => _reader;

    public async Task EnqueueAsync(ProcessedResponseItem item, HierarchicalLoggingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _writer.WriteAsync(item, cancellationToken);
            // Thread-safe increment using Interlocked
            Interlocked.Increment(ref _queueDepth);

            _logger.LogDebugWithHierarchy(context,
                "Enqueued response processing item. QueueDepth: {QueueDepth}",
                _queueDepth);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to enqueue response processing item.");
            throw;
        }
    }

    public int GetQueueDepth()
    {
        // Thread-safe read - int reads are atomic on most platforms, but use CompareExchange for safety
        return Interlocked.CompareExchange(ref _queueDepth, 0, 0);
    }

    public void DecrementQueueDepth()
    {
        // Thread-safe decrement using Interlocked
        Interlocked.Decrement(ref _queueDepth);
    }
}
