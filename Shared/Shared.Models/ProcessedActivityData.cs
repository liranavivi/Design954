namespace Shared.Models
{

    /// <summary>
    /// Data structure for returning processed activity data from processors
    /// Public version of BaseProcessorApplication.ProcessedActivityData for use in consumers
    /// </summary>
    public class ProcessedActivityData
    {
        /// <summary>
        /// Result message from processing
        /// </summary>
        public string? Result { get; set; }

        /// <summary>
        /// Status of the processing
        /// </summary>
        public ActivityExecutionStatus? Status { get; set; }

        /// <summary>
        /// Processed data object (will be serialized to JSON)
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// Name of the processor that handled this activity
        /// </summary>
        public string? ProcessorName { get; set; }

        /// <summary>
        /// Version of the processor
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// Execution ID for this activity instance
        /// </summary>
        public Guid ExecutionId { get; set; }
    }
}
