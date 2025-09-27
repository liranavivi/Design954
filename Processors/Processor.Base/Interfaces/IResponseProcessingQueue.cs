using Processor.Base.Models;
using Shared.Correlation;
using System.Threading.Channels;

namespace Processor.Base.Interfaces;

/// <summary>
/// Interface for the response processing queue
/// Thread-safe queue for processing individual response items concurrently
/// </summary>
public interface IResponseProcessingQueue
{
    /// <summary>
    /// Enqueues a response processing item for background processing
    /// Thread-safe operation using Channel writer
    /// </summary>
    /// <param name="item">The response processing item to enqueue</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the enqueue operation</returns>
    Task EnqueueAsync(ProcessedResponseItem item, HierarchicalLoggingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the channel reader for the background service
    /// Used by ResponseProcessingService workers to read items
    /// </summary>
    ChannelReader<ProcessedResponseItem> Reader { get; }

    /// <summary>
    /// Gets the current queue depth (number of pending response items)
    /// Thread-safe operation using Interlocked.Read
    /// </summary>
    /// <returns>Number of response items in the queue</returns>
    int GetQueueDepth();

    /// <summary>
    /// Decrements the queue depth counter
    /// Thread-safe operation using Interlocked.Decrement
    /// Called by workers when they finish processing an item
    /// </summary>
    void DecrementQueueDepth();
}
