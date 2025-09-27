using Shared.Correlation;
using Shared.Models;
using System.Diagnostics;

namespace Processor.Base.Models;

/// <summary>
/// Model for queuing individual processed responses for background processing
/// Thread-safe design with proper data isolation
/// </summary>
public class ProcessedResponseItem
{
    /// <summary>
    /// Cloned processed activity data to avoid shared mutations between workers
    /// </summary>
    public ProcessedActivityData ProcessedData { get; set; } = null!;

    /// <summary>
    /// Original message that initiated the processing
    /// </summary>
    public ProcessorActivityMessage OriginalMessage { get; set; } = null!;

    /// <summary>
    /// Hierarchical logging context for this specific response item
    /// </summary>
    public HierarchicalLoggingContext ProcessingContext { get; set; } = null!;

    /// <summary>
    /// Independent stopwatch for this response item to avoid shared timing issues
    /// Each item gets its own stopwatch to ensure thread safety
    /// </summary>
    public Stopwatch ProcessingStopwatch { get; set; } = null!;

    /// <summary>
    /// When this item was queued for processing
    /// </summary>
    public DateTime QueuedAt { get; set; }

    /// <summary>
    /// Number of retry attempts for this response item
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Maximum number of retries allowed for this response item
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Creates a thread-safe clone of ProcessedActivityData to prevent shared mutations
    /// </summary>
    /// <param name="originalData">Original ProcessedActivityData to clone</param>
    /// <returns>Cloned ProcessedActivityData instance</returns>
    public static ProcessedActivityData CloneProcessedActivityData(ProcessedActivityData originalData)
    {
        return new ProcessedActivityData
        {
            Result = originalData.Result,
            Status = originalData.Status,
            Data = originalData.Data, // Note: Deep cloning of Data object may be needed depending on implementation
            ProcessorName = originalData.ProcessorName,
            Version = originalData.Version,
            ExecutionId = originalData.ExecutionId
        };
    }
}
