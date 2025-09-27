using Plugin.PreFileReader.Models;
using Shared.Correlation;

namespace Plugin.PreFileReader.Interfaces;

/// <summary>
/// Interface for file registration in cache
/// Specific to PreFileReaderPlugin for tracking discovered and processed files
/// </summary>
public interface IFileRegistrationService
{
    /// <summary>
    /// Checks if a file is already registered for processing
    /// </summary>
    /// <param name="filePath">Full file path</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if file is already registered</returns>
    Task<bool> IsFileRegisteredAsync(string filePath, HierarchicalLoggingContext context);
    
    /// <summary>
    /// Registers a file for processing
    /// </summary>
    /// <param name="filePath">Full file path</param>
    /// <param name="processorId">Processor ID</param>
    /// <param name="executionId">Execution ID</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Task representing the operation</returns>
    Task RegisterFileAsync(string filePath, Guid processorId, Guid executionId, Guid correlationId, HierarchicalLoggingContext context);

    /// <summary>
    /// Atomically tries to register a file for processing if it's not already registered
    /// TTL is controlled by FileRegistrationCacheConfiguration
    /// </summary>
    /// <param name="filePath">Full file path</param>
    /// <param name="processorId">Processor ID</param>
    /// <param name="executionId">Execution ID</param>
    /// <param name="correlationId">Correlation ID</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if the file was successfully registered (wasn't already registered), false if it was already registered</returns>
    Task<bool> TryToAddAsync(string filePath, Guid processorId, Guid executionId, Guid correlationId, HierarchicalLoggingContext context);
    
    /// <summary>
    /// Updates file registration with processing completion
    /// </summary>
    /// <param name="filePath">Full file path</param>
    /// <param name="processingResult">Result of processing</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Task representing the operation</returns>
    Task UpdateFileProcessingStatusAsync(string filePath, FileProcessingResult processingResult, HierarchicalLoggingContext context);
}
