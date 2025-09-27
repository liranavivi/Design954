using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plugin.PreFileReader.Interfaces;
using Plugin.PreFileReader.Models;
using Shared.Correlation;
using Shared.Services.Interfaces;

namespace Plugin.PreFileReader.Services;

/// <summary>
/// Service for managing file registration in cache
/// Specific to PreFileReaderPlugin for tracking discovered and processed files
/// </summary>
public class FileRegistrationService : IFileRegistrationService
{
    private readonly ICacheService _cacheService;
    private readonly ILogger _logger;
    private readonly string _mapName;
    private readonly TimeSpan _ttl;

    public FileRegistrationService(
        ICacheService cacheService,
        ILogger logger,
        IOptions<FileRegistrationCacheConfiguration> cacheConfig)
    {
        _cacheService = cacheService;
        _logger = logger;
        _mapName = cacheConfig.Value.MapName;
        _ttl = cacheConfig.Value.GetTtl();
    }

    public async Task<bool> IsFileRegisteredAsync(string filePath, HierarchicalLoggingContext context)
    {
        try
        {
            return await _cacheService.ExistsAsync(_mapName, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithHierarchy(context, ex,
                "Failed to check file registration for: {FilePath}", filePath);
            return false; // Assume not registered on error
        }
    }

    public async Task RegisterFileAsync(string filePath, Guid processorId, Guid executionId, Guid correlationId, HierarchicalLoggingContext context)
    {
        var registrationInfo = new
        {
            filePath = filePath,
            processorId = processorId.ToString(),
            executionId = executionId.ToString(),
            correlationId = correlationId.ToString(),
            registeredAt = DateTime.UtcNow,
            status = "registered"
        };

        try
        {
            await _cacheService.SetAsync(
                _mapName,
                filePath,
                JsonSerializer.Serialize(registrationInfo, new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                _ttl);

            _logger.LogDebugWithHierarchy(context,
                "Registered file for processing with TTL: {FilePath}, TTL: {TTL}s", filePath, _ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to register file: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<bool> TryToAddAsync(string filePath, Guid processorId, Guid executionId, Guid correlationId, HierarchicalLoggingContext context)
    {
        var registrationInfo = new
        {
            filePath = filePath,
            processorId = processorId.ToString(),
            executionId = executionId.ToString(),
            correlationId = correlationId.ToString(),
            registeredAt = DateTime.UtcNow,
            status = "registered"
        };

        try
        {
            // TTL is controlled by configuration
            // PutIfAbsentAsync returns null if the key was absent and value was set,
            // or the previous value if the key already existed
            var previousValue = await _cacheService.PutIfAbsentAsync(
                _mapName,
                filePath,
                JsonSerializer.Serialize(registrationInfo, new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                _ttl);

            var wasAdded = previousValue == null;

            if (wasAdded)
            {
                _logger.LogDebugWithHierarchy(context,
                    "Successfully registered file for processing with TTL: {FilePath}, TTL: {TTL}s", filePath, _ttl.TotalSeconds);
            }
            else
            {
                _logger.LogDebugWithHierarchy(context,
                    "File was already registered for processing: {FilePath}", filePath);
            }

            return wasAdded;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex,
                "Failed to try add file registration: {FilePath}", filePath);
            throw;
        }
    }

    public async Task UpdateFileProcessingStatusAsync(string filePath, FileProcessingResult processingResult, HierarchicalLoggingContext context)
    {
        try
        {
            // Get existing registration
            var existingData = await _cacheService.GetAsync(_mapName, filePath,context);
            if (existingData != null)
            {
                var registration = JsonSerializer.Deserialize<Dictionary<string, object>>(existingData);
                if (registration != null)
                {
                    // Update with processing result
                    registration["status"] = processingResult.Success ? "completed" : "failed";
                    registration["processedAt"] = processingResult.ProcessedAt;
                    if (!string.IsNullOrEmpty(processingResult.Error))
                    {
                        registration["error"] = processingResult.Error;
                    }

                    // Add any additional metadata
                    foreach (var metadata in processingResult.Metadata)
                    {
                        registration[metadata.Key] = metadata.Value;
                    }

                    await _cacheService.SetAsync(
                        _mapName,
                        filePath,
                        JsonSerializer.Serialize(registration),
                        _ttl);
                }
            }

            _logger.LogDebugWithHierarchy(context,
                "Updated file processing status: {FilePath}, Success: {Success}",
                filePath, processingResult.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarningWithHierarchy(context, ex,
                "Failed to update file processing status: {FilePath}", filePath);
            // Don't throw - this is just status tracking
        }
    }
}
