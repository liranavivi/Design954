using Shared.MassTransit.Commands;

namespace Processor.Base.Models;

/// <summary>
/// Request model for queue-based processing handoff
/// </summary>
public class ProcessingRequest
{
    /// <summary>
    /// The original ExecuteActivityCommand received from RabbitMQ
    /// </summary>
    public ExecuteActivityCommand OriginalCommand { get; set; } = null!;

    /// <summary>
    /// The converted ProcessorActivityMessage for processing
    /// </summary>
    public ProcessorActivityMessage ActivityMessage { get; set; } = null!;

    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// When the message was received by the consumer
    /// </summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>
    /// Number of retry attempts for this request
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Maximum number of retries allowed
    /// </summary>
    public int MaxRetries { get; set; } = 3;
}
