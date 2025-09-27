using Processor.Base.Models;
using Shared.Correlation;

namespace Processor.Base.Interfaces;

/// <summary>
/// Interface for the activity processing queue
/// </summary>
public interface IActivityProcessingQueue
{
    /// <summary>
    /// Enqueues a processing request for background processing
    /// </summary>
    /// <param name="request">The processing request to enqueue</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the enqueue operation</returns>
    Task EnqueueAsync(ProcessingRequest request, HierarchicalLoggingContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current queue depth (number of pending requests)
    /// </summary>
    /// <returns>Number of requests in the queue</returns>
    int GetQueueDepth();
}
