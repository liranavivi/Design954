using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Processor.Base.Utilities;
using Shared.Correlation;
using Shared.Entities;
using Shared.Extensions;
using Shared.MassTransit.Commands;
using Shared.Models;
using Shared.Services.Interfaces;

namespace Processor.Base.Services;

/// <summary>
/// Core service for managing processor functionality and activity processing
/// </summary>
public class ProcessorService : IProcessorService
{
    private readonly IActivityExecutor _activityExecutor;
    private readonly ICacheService _cacheService;
    private readonly ISchemaValidator _schemaValidator;
    private readonly IBus _bus;
    private readonly ProcessorConfiguration _config;
    private readonly Shared.Services.Models.SchemaValidationConfiguration _validationConfig;
    private readonly ProcessorInitializationConfiguration? _initializationConfig;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProcessorService> _logger;
    private readonly IPerformanceMetricsService? _performanceMetricsService;
    private readonly ProcessorActivityDataCacheConfiguration _activityCacheConfig;
    private readonly IProcessorHealthMetricsService? _healthMetricsService;
    private readonly ActivitySource _activitySource;
    private readonly DateTime _startTime;

    private Guid? _processorId;
    private readonly object _processorIdLock = new();

    // Schema health tracking - Initialize as unhealthy until validation completes
    private bool _inputSchemaHealthy = false;
    private bool _outputSchemaHealthy = false;
    private bool _schemaIdsValid = false;
    private string _inputSchemaErrorMessage = "Schema not yet validated";
    private string _outputSchemaErrorMessage = "Schema not yet validated";
    private string _schemaValidationErrorMessage = "Schema validation not yet performed";
    private readonly object _schemaHealthLock = new();

    // Implementation hash validation tracking - Initialize as unhealthy until validation completes
    private bool _implementationHashValid = false;
    private string _implementationHashErrorMessage = "Implementation hash not yet validated";

    // Initialization status tracking
    private bool _isInitialized = false;
    private bool _isInitializing = false;
    private string _initializationErrorMessage = string.Empty;
    private readonly object _initializationLock = new();


    public ProcessorService(
        IActivityExecutor activityExecutor,
        ICacheService cacheService,
        ISchemaValidator schemaValidator,
        IBus bus,
        IOptions<ProcessorConfiguration> config,
        IOptions<Shared.Services.Models.SchemaValidationConfiguration> validationConfig,
        IConfiguration configuration,
        ILogger<ProcessorService> logger,
        IPerformanceMetricsService? performanceMetricsService = null,
        IProcessorHealthMetricsService? healthMetricsService = null,
        IOptions<ProcessorInitializationConfiguration>? initializationConfig = null,
        IOptions<ProcessorActivityDataCacheConfiguration>? activityCacheConfig = null)
    {
        _activityExecutor = activityExecutor;
        _cacheService = cacheService;
        _schemaValidator = schemaValidator;
        _bus = bus;
        _config = config.Value;
        _validationConfig = validationConfig.Value;
        _initializationConfig = initializationConfig?.Value;
        _configuration = configuration;
        _logger = logger;
        _performanceMetricsService = performanceMetricsService;
        _healthMetricsService = healthMetricsService;
        _activityCacheConfig = activityCacheConfig?.Value ?? new ProcessorActivityDataCacheConfiguration();
        _activitySource = new ActivitySource(ActivitySources.Services);




        _startTime = DateTime.UtcNow;
    }

    public async Task InitializeAsync()
    {
        await InitializeAsync(CancellationToken.None);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Generate and set correlation ID for processor initialization
        var correlationId = Guid.NewGuid();
        CorrelationIdContext.SetCorrelationIdStatic(correlationId);

        using var activity = _activitySource.StartActivity("InitializeProcessor");
        activity?.SetTag("correlation.id", correlationId.ToString());

        // Set initialization status
        lock (_initializationLock)
        {
            _isInitializing = true;
            _isInitialized = false;
            _initializationErrorMessage = string.Empty;
        }

        _logger.LogInformationWithCorrelation(
            "Initializing processor - {ProcessorName} v{ProcessorVersion}",
            _config.Name, _config.Version);

        try
        {
            // Get initialization configuration
            var initConfig = _initializationConfig ?? new ProcessorInitializationConfiguration();

            if (!initConfig.RetryEndlessly)
            {
                // Legacy behavior: retry limited times then throw
                await InitializeWithLimitedRetriesAsync(activity, cancellationToken);
            }
            else
            {
                // New behavior: retry endlessly until successful
                await InitializeWithEndlessRetriesAsync(activity, cancellationToken);
            }

            // Mark as successfully initialized
            lock (_initializationLock)
            {
                _isInitialized = true;
                _isInitializing = false;
                _initializationErrorMessage = string.Empty;
            }

            _logger.LogInformationWithCorrelation(
                "Processor initialization completed successfully - {ProcessorName} v{ProcessorVersion}",
                _config.Name, _config.Version);
        }
        catch (Exception ex)
        {
            // Mark initialization as failed
            lock (_initializationLock)
            {
                _isInitialized = false;
                _isInitializing = false;
                _initializationErrorMessage = ex.Message;
            }

            // Record initialization exception
            _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

            _logger.LogErrorWithCorrelation(ex,
                "Processor initialization failed - {ProcessorName} v{ProcessorVersion}",
                _config.Name, _config.Version);

            throw;
        }
    }

    private async Task InitializeWithEndlessRetriesAsync(Activity? activity, CancellationToken cancellationToken)
    {
        var initConfig = _initializationConfig ?? new ProcessorInitializationConfiguration();
        var attempt = 0;
        var currentDelay = initConfig.RetryDelay;

        _logger.LogInformationWithCorrelation(
            "Starting endless initialization retry loop for processor {CompositeKey}. RetryDelay: {RetryDelay}, RetryDelayMs: {RetryDelayMs}, UseExponentialBackoff: {UseExponentialBackoff}",
            _config.GetCompositeKey(), initConfig.RetryDelay, initConfig.RetryDelayMs, initConfig.UseExponentialBackoff);

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;

            try
            {
                // 1. First, retrieve schema definitions - no point continuing if this fails
                if (!await RetrieveSchemaDefinitionsAsync())
                {
                    throw new InvalidOperationException("Schema definitions retrieval failed - cannot continue with processor initialization");
                }

                if (initConfig.LogRetryAttempts)
                {
                    _logger.LogDebugWithCorrelation("Requesting processor by composite key: {CompositeKey} (attempt {Attempt})",
                        _config.GetCompositeKey(), attempt);
                }

                // 2. Get processor query to check if processor exists
                var getQuery = new GetProcessorQuery
                {
                    CompositeKey = _config.GetCompositeKey()
                };

                using var timeoutCts = new CancellationTokenSource(initConfig.InitializationTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var response = await _bus.Request<GetProcessorQuery, GetProcessorQueryResponse>(
                    getQuery, combinedCts.Token, initConfig.InitializationTimeout);

                ProcessorEntity processorEntity;

                if (response.Message.Success && response.Message.Entity != null)
                {
                    // Existing processor found
                    processorEntity = response.Message.Entity;

                    lock (_processorIdLock)
                    {
                        _processorId = processorEntity.Id;
                    }

                    _logger.LogInformationWithCorrelation(
                        "Found existing processor after {Attempts} attempts. ProcessorId: {ProcessorId}, CompositeKey: {CompositeKey}",
                        attempt, _processorId, _config.GetCompositeKey());

                    activity?.SetProcessorTags(_processorId.Value, _config.Name, _config.Version);
                }
                else
                {
                    // Processor not found - create new processor
                    if (initConfig.LogRetryAttempts)
                    {
                        _logger.LogInformationWithCorrelation(
                            "Processor not found, creating new processor. CompositeKey: {CompositeKey} (attempt {Attempt})",
                            _config.GetCompositeKey(), attempt);
                    }

                    processorEntity = await CreateProcessorAsync();
                }

                // 3. Validate schema IDs - must return true to continue
                if (!ValidateSchemaIds(processorEntity))
                {
                    throw new InvalidOperationException("Schema IDs validation failed - processor configuration mismatch");
                }

                // 4. Validate implementation hash - must return true to continue
                if (!ValidateImplementationHash(processorEntity))
                {
                    throw new InvalidOperationException("Implementation hash validation failed - version increment required");
                }

                // Success - all three validations passed, exit retry loop
                _logger.LogInformationWithCorrelation(
                    "Processor initialization completed successfully after {Attempts} attempts. ProcessorId: {ProcessorId}. All validations passed: Schema retrieval, Schema IDs, Implementation hash",
                    attempt, _processorId);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformationWithCorrelation(
                    "Processor initialization cancelled after {Attempts} attempts. CompositeKey: {CompositeKey}",
                    attempt, _config.GetCompositeKey());
                throw;
            }
            catch (RequestTimeoutException ex)
            {
                // Record timeout exception metrics
                _healthMetricsService?.RecordException(ex.GetType().Name, "warning", Guid.Empty);

                if (initConfig.LogRetryAttempts)
                {
                    _logger.LogWarningWithCorrelation(ex,
                        "Timeout while requesting processor (attempt {Attempt}). CompositeKey: {CompositeKey}. Retrying in {DelaySeconds} seconds...",
                        attempt, _config.GetCompositeKey(), currentDelay.TotalSeconds);
                }

                // Wait before next retry
                await Task.Delay(currentDelay, cancellationToken);

                // Calculate next delay with exponential backoff if enabled
                if (initConfig.UseExponentialBackoff)
                {
                    currentDelay = TimeSpan.FromMilliseconds(Math.Min(
                        currentDelay.TotalMilliseconds * 2,
                        initConfig.RetryDelayMs));
                }
            }
            catch (Exception ex)
            {
                activity?.SetErrorTags(ex);

                // Record exception metrics for initialization failures
                _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

                _logger.LogErrorWithCorrelation(ex,
                    "Unexpected error during processor initialization (attempt {Attempt}). CompositeKey: {CompositeKey}. Retrying in {DelaySeconds} seconds...",
                    attempt, _config.GetCompositeKey(), currentDelay.TotalSeconds);

                // Wait before next retry
                await Task.Delay(currentDelay, cancellationToken);

                // Calculate next delay with exponential backoff if enabled
                if (initConfig.UseExponentialBackoff)
                {
                    currentDelay = TimeSpan.FromMilliseconds(Math.Min(
                        currentDelay.TotalMilliseconds * 2,
                        initConfig.RetryDelayMs));
                }
            }
        }
    }

    private async Task InitializeWithLimitedRetriesAsync(Activity? activity, CancellationToken cancellationToken)
    {
        var initConfig = _initializationConfig ?? new ProcessorInitializationConfiguration();

        // Legacy behavior: retry limited times then throw
        const int maxRetries = 3;
        var baseDelay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(10);

        RequestTimeoutException? lastTimeoutException = null;

        // Always retrieve schema definitions first to validate configuration
        _logger.LogInformationWithCorrelation("Retrieving schema definitions to validate configuration before processor operations");
        if (!await RetrieveSchemaDefinitionsAsync())
        {
            throw new InvalidOperationException("Schema definitions retrieval failed - cannot continue with processor initialization");
        }

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebugWithCorrelation("Requesting processor by composite key: {CompositeKey} (attempt {Attempt}/{MaxRetries})",
                    _config.GetCompositeKey(), attempt, maxRetries);

                // Always get processor query to check if processor exists
                var getQuery = new GetProcessorQuery
                {
                    CompositeKey = _config.GetCompositeKey()
                };

                var response = await _bus.Request<GetProcessorQuery, GetProcessorQueryResponse>(
                    getQuery, cancellationToken, initConfig.InitializationTimeout);

                ProcessorEntity processorEntity;

                if (response.Message.Success && response.Message.Entity != null)
                {
                    // Existing processor found
                    processorEntity = response.Message.Entity;

                    lock (_processorIdLock)
                    {
                        _processorId = processorEntity.Id;
                    }

                    _logger.LogInformationWithCorrelation(
                        "Found existing processor. ProcessorId: {ProcessorId}, CompositeKey: {CompositeKey}",
                        _processorId, _config.GetCompositeKey());

                    activity?.SetProcessorTags(_processorId.Value, _config.Name, _config.Version);
                }
                else
                {
                    // Processor not found - create new processor
                    _logger.LogInformationWithCorrelation(
                        "Processor not found, creating new processor. CompositeKey: {CompositeKey}",
                        _config.GetCompositeKey());

                    processorEntity = await CreateProcessorAsync();
                }

                // Common validation for both existing and new processors
                ValidateSchemaIds(processorEntity);
                ValidateImplementationHash(processorEntity);

                // Success - exit retry loop
                return;
            }
            catch (RequestTimeoutException ex)
            {
                lastTimeoutException = ex;

                // Record timeout exception metrics
                _healthMetricsService?.RecordException(ex.GetType().Name, "warning", Guid.Empty);

                _logger.LogWarningWithCorrelation(ex,
                    "Timeout while requesting processor (attempt {Attempt}/{MaxRetries}). CompositeKey: {CompositeKey}",
                    attempt, maxRetries, _config.GetCompositeKey());

                // If this was the last attempt, we'll throw after the loop
                if (attempt == maxRetries)
                {
                    break; // Exit the retry loop
                }

                // Calculate exponential backoff delay
                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1),
                    maxDelay.TotalMilliseconds));

                _logger.LogInformationWithCorrelation(
                    "Retrying in {DelaySeconds} seconds... (attempt {NextAttempt}/{MaxRetries})",
                    delay.TotalSeconds, attempt + 1, maxRetries);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                activity?.SetErrorTags(ex);

                // Record exception metrics for initialization failures
                _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

                _logger.LogErrorWithCorrelation(ex,
                    "Failed to initialize processor. CompositeKey: {CompositeKey}",
                    _config.GetCompositeKey());
                throw;
            }
        }

        // If we reach here, all retries failed with timeouts
        _logger.LogErrorWithCorrelation(
            "Failed to initialize processor after {MaxRetries} attempts due to timeouts. CompositeKey: {CompositeKey}",
            maxRetries, _config.GetCompositeKey());

        throw new InvalidOperationException(
            $"Failed to initialize processor after {maxRetries} attempts due to timeout communicating with Processor Manager. CompositeKey: {_config.GetCompositeKey()}",
            lastTimeoutException);
    }

    private async Task<ProcessorEntity> CreateProcessorAsync()
    {
        var createCommand = new CreateProcessorCommand
        {
            Version = _config.Version,
            Name = _config.Name,
            Description = _config.Description,
            InputSchemaId = _config.InputSchemaId,
            OutputSchemaId = _config.OutputSchemaId,
            ImplementationHash = GetImplementationHash(),
            RequestedBy = "BaseProcessorApplication"
        };

        _logger.LogDebugWithCorrelation("Publishing CreateProcessorCommand for {CompositeKey} with InputSchemaId: {InputSchemaId}, OutputSchemaId: {OutputSchemaId}",
            _config.GetCompositeKey(), createCommand.InputSchemaId, createCommand.OutputSchemaId);

        await _bus.Publish(createCommand);

        // Wait a bit and try to get the processor again
        await Task.Delay(TimeSpan.FromSeconds(2));

        var getQuery = new GetProcessorQuery
        {
            CompositeKey = _config.GetCompositeKey()
        };

        var response = await _bus.Request<GetProcessorQuery, GetProcessorQueryResponse>(
            getQuery, timeout: TimeSpan.FromSeconds(30));

        if (response.Message.Success && response.Message.Entity != null)
        {
            lock (_processorIdLock)
            {
                _processorId = response.Message.Entity.Id;
            }

            _logger.LogInformationWithCorrelation(
                "Successfully created and retrieved processor. ProcessorId: {ProcessorId}, CompositeKey: {CompositeKey}",
                _processorId, _config.GetCompositeKey());

            // Schema definitions already retrieved at initialization start - no need to retrieve again
            return response.Message.Entity;
        }
        else
        {
            throw new InvalidOperationException($"Failed to create or retrieve processor with composite key: {_config.GetCompositeKey()}");
        }
    }

    private async Task<bool> RetrieveSchemaDefinitionsAsync()
    {
        var logMessage = "Retrieving schema definitions";
        var logParams = new List<object>();

        if (_validationConfig.EnableInputValidation)
        {
            logMessage += " for InputSchemaId: {InputSchemaId}";
            logParams.Add(_config.InputSchemaId);
        }
        else
        {
            logMessage += " (InputValidation: DISABLED)";
        }

        if (_validationConfig.EnableOutputValidation)
        {
            logMessage += ", OutputSchemaId: {OutputSchemaId}";
            logParams.Add(_config.OutputSchemaId);
        }
        else
        {
            logMessage += " (OutputValidation: DISABLED)";
        }

        _logger.LogInformationWithCorrelation(logMessage, logParams.ToArray());

        bool inputSchemaSuccess = !_validationConfig.EnableInputValidation; // Default to success if validation disabled
        bool outputSchemaSuccess = !_validationConfig.EnableOutputValidation; // Default to success if validation disabled
        string inputErrorMessage = string.Empty;
        string outputErrorMessage = string.Empty;

        // Only retrieve input schema if input validation is enabled
        if (_validationConfig.EnableInputValidation)
        {
            try
            {
                // Retrieve input schema definition
                var inputSchemaQuery = new GetSchemaDefinitionQuery
                {
                    SchemaId = _config.InputSchemaId,
                    RequestedBy = "BaseProcessorApplication"
                };

                var inputSchemaResponse = await _bus.Request<GetSchemaDefinitionQuery, GetSchemaDefinitionQueryResponse>(
                    inputSchemaQuery, timeout: TimeSpan.FromSeconds(30));

                if (inputSchemaResponse.Message.Success && !string.IsNullOrEmpty(inputSchemaResponse.Message.Definition))
                {
                    _config.InputSchemaDefinition = inputSchemaResponse.Message.Definition;
                    inputSchemaSuccess = true;
                    _logger.LogInformationWithCorrelation("Successfully retrieved input schema definition. Length: {Length}",
                        _config.InputSchemaDefinition.Length);
                }
                else
                {
                    inputErrorMessage = $"Failed to retrieve input schema definition. SchemaId: {_config.InputSchemaId}, Message: {inputSchemaResponse.Message.Message}";
                    _logger.LogErrorWithCorrelation(inputErrorMessage);
                }
            }
            catch (Exception ex)
            {
                // Record schema retrieval exception
                _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

                inputErrorMessage = $"Error retrieving input schema definition. SchemaId: {_config.InputSchemaId}, Error: {ex.Message}";
                _logger.LogErrorWithCorrelation(ex, inputErrorMessage);
            }
        }
        else
        {
            _logger.LogDebugWithCorrelation("Input schema validation is disabled. Skipping input schema definition retrieval.");
        }

        // Only retrieve output schema if output validation is enabled
        if (_validationConfig.EnableOutputValidation)
        {
            try
            {
                // Retrieve output schema definition
                var outputSchemaQuery = new GetSchemaDefinitionQuery
                {
                    SchemaId = _config.OutputSchemaId,
                    RequestedBy = "BaseProcessorApplication"
                };

                var outputSchemaResponse = await _bus.Request<GetSchemaDefinitionQuery, GetSchemaDefinitionQueryResponse>(
                    outputSchemaQuery, timeout: TimeSpan.FromSeconds(30));

                if (outputSchemaResponse.Message.Success && !string.IsNullOrEmpty(outputSchemaResponse.Message.Definition))
                {
                    _config.OutputSchemaDefinition = outputSchemaResponse.Message.Definition;
                    outputSchemaSuccess = true;
                    _logger.LogInformationWithCorrelation("Successfully retrieved output schema definition. Length: {Length}",
                        _config.OutputSchemaDefinition.Length);
                }
                else
                {
                    outputErrorMessage = $"Failed to retrieve output schema definition. SchemaId: {_config.OutputSchemaId}, Message: {outputSchemaResponse.Message.Message}";
                    _logger.LogErrorWithCorrelation(outputErrorMessage);
                }
            }
            catch (Exception ex)
            {
                // Record schema retrieval exception
                _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

                outputErrorMessage = $"Error retrieving output schema definition. SchemaId: {_config.OutputSchemaId}, Error: {ex.Message}";
                _logger.LogErrorWithCorrelation(ex, outputErrorMessage);
            }
        }
        else
        {
            _logger.LogDebugWithCorrelation("Output schema validation is disabled. Skipping output schema definition retrieval.");
        }

        // Update schema health status
        lock (_schemaHealthLock)
        {
            _inputSchemaHealthy = inputSchemaSuccess;
            _outputSchemaHealthy = outputSchemaSuccess;
            _inputSchemaErrorMessage = inputSchemaSuccess ? string.Empty : inputErrorMessage;
            _outputSchemaErrorMessage = outputSchemaSuccess ? string.Empty : outputErrorMessage;
        }

        // Log overall schema health status
        if (!inputSchemaSuccess || !outputSchemaSuccess)
        {
            var failureReasons = new List<string>();
            if (_validationConfig.EnableInputValidation && !inputSchemaSuccess)
            {
                failureReasons.Add("Input schema retrieval failed");
            }
            if (_validationConfig.EnableOutputValidation && !outputSchemaSuccess)
            {
                failureReasons.Add("Output schema retrieval failed");
            }

            if (failureReasons.Any())
            {
                _logger.LogErrorWithCorrelation("Processor marked as unhealthy due to schema definition retrieval failures. Failures: {Failures}, InputSchemaHealthy: {InputSchemaHealthy}, OutputSchemaHealthy: {OutputSchemaHealthy}",
                    string.Join(", ", failureReasons), inputSchemaSuccess, outputSchemaSuccess);
            }
        }
        else
        {
            var successMessages = new List<string>();
            if (_validationConfig.EnableInputValidation)
            {
                successMessages.Add("Input schema retrieved");
            }
            else
            {
                successMessages.Add("Input validation disabled");
            }

            if (_validationConfig.EnableOutputValidation)
            {
                successMessages.Add("Output schema retrieved");
            }
            else
            {
                successMessages.Add("Output validation disabled");
            }

            _logger.LogInformationWithCorrelation("Schema definitions ready. Status: {Status}", string.Join(", ", successMessages));
        }

        // Return true if both input and output schemas are successful
        return inputSchemaSuccess && outputSchemaSuccess;
    }

    public async Task<Guid> GetProcessorIdAsync()
    {
        if (_processorId.HasValue)
        {
            return _processorId.Value;
        }

        // Check if we're using endless retry mode
        var initConfig = _initializationConfig ?? new ProcessorInitializationConfiguration();

        if (initConfig.RetryEndlessly)
        {
            // In endless retry mode, return empty GUID if not yet initialized
            // The initialization will continue in the background
            _logger.LogDebugWithCorrelation("Processor ID not available yet - initialization in progress or not started");
            return Guid.Empty;
        }

        // Legacy behavior: try to initialize once
        await InitializeAsync();

        if (!_processorId.HasValue)
        {
            throw new InvalidOperationException("Processor ID is not available. Initialization may have failed.");
        }

        return _processorId.Value;
    }

    /// <summary>
    /// Validates that the processor entity's schema IDs match the configured schema IDs
    /// Respects EnableInputValidation and EnableOutputValidation configuration flags
    /// </summary>
    /// <param name="processorEntity">The processor entity retrieved from the query</param>
    /// <returns>True if schema IDs match configuration, false otherwise</returns>
    private bool ValidateSchemaIds(ProcessorEntity processorEntity)
    {
        using var activity = _activitySource.StartActivity("ValidateSchemaIds");
        activity?.SetTag("processor.id", processorEntity.Id.ToString());
        activity?.SetTag("validation.input_enabled", _validationConfig.EnableInputValidation);
        activity?.SetTag("validation.output_enabled", _validationConfig.EnableOutputValidation);

        try
        {
            var configInputSchemaId = _config.InputSchemaId;
            var configOutputSchemaId = _config.OutputSchemaId;
            var entityInputSchemaId = processorEntity.InputSchemaId;
            var entityOutputSchemaId = processorEntity.OutputSchemaId;

            activity?.SetTag("config.input_schema_id", configInputSchemaId.ToString())
                    ?.SetTag("config.output_schema_id", configOutputSchemaId.ToString())
                    ?.SetTag("entity.input_schema_id", entityInputSchemaId.ToString())
                    ?.SetTag("entity.output_schema_id", entityOutputSchemaId.ToString());

            // Only validate input schema if input validation is enabled
            bool inputSchemaMatches = !_validationConfig.EnableInputValidation || (configInputSchemaId == entityInputSchemaId);

            // Only validate output schema if output validation is enabled
            bool outputSchemaMatches = !_validationConfig.EnableOutputValidation || (configOutputSchemaId == entityOutputSchemaId);

            bool allSchemasValid = inputSchemaMatches && outputSchemaMatches;

            string validationMessage = string.Empty;
            if (!allSchemasValid)
            {
                var errors = new List<string>();
                if (_validationConfig.EnableInputValidation && !inputSchemaMatches)
                {
                    errors.Add($"Input schema mismatch: Config={configInputSchemaId}, Entity={entityInputSchemaId}");
                }
                if (_validationConfig.EnableOutputValidation && !outputSchemaMatches)
                {
                    errors.Add($"Output schema mismatch: Config={configOutputSchemaId}, Entity={entityOutputSchemaId}");
                }
                validationMessage = string.Join("; ", errors);
            }

            // Update schema validation status
            lock (_schemaHealthLock)
            {
                _schemaIdsValid = allSchemasValid;
                _schemaValidationErrorMessage = allSchemasValid ? string.Empty : validationMessage;
            }

            if (allSchemasValid)
            {
                var logMessage = "Schema ID validation successful. ProcessorId: {ProcessorId}";
                var logParams = new List<object> { processorEntity.Id };

                if (_validationConfig.EnableInputValidation)
                {
                    logMessage += ", InputSchemaId: {InputSchemaId}";
                    logParams.Add(configInputSchemaId);
                }
                else
                {
                    logMessage += ", InputValidation: DISABLED";
                }

                if (_validationConfig.EnableOutputValidation)
                {
                    logMessage += ", OutputSchemaId: {OutputSchemaId}";
                    logParams.Add(configOutputSchemaId);
                }
                else
                {
                    logMessage += ", OutputValidation: DISABLED";
                }

                _logger.LogInformationWithCorrelation(logMessage, logParams.ToArray());
            }
            else
            {
                _logger.LogErrorWithCorrelation(
                    "Schema ID validation failed. ProcessorId: {ProcessorId}, ValidationErrors: {ValidationErrors}",
                    processorEntity.Id, validationMessage);
            }

            activity?.SetTag("validation.success", allSchemasValid)
                    ?.SetTag("validation.input_match", inputSchemaMatches)
                    ?.SetTag("validation.output_match", outputSchemaMatches);

            return allSchemasValid;
        }
        catch (Exception ex)
        {
            // Record schema validation exception
            _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

            var errorMessage = $"Error during schema ID validation: {ex.Message}";

            lock (_schemaHealthLock)
            {
                _schemaIdsValid = false;
                _schemaValidationErrorMessage = errorMessage;
            }

            _logger.LogErrorWithCorrelation(ex, "Schema ID validation failed with exception. ProcessorId: {ProcessorId}",
                processorEntity.Id);

            activity?.SetTag("validation.success", false)
                    ?.SetTag("validation.error", ex.Message);

            return false;
        }
    }

    /// <summary>
    /// Gets the current schema health status including schema ID validation
    /// </summary>
    /// <returns>A tuple indicating if schemas are healthy and valid</returns>
    public (bool InputSchemaHealthy, bool OutputSchemaHealthy, bool SchemaIdsValid, string InputSchemaError, string OutputSchemaError, string SchemaValidationError) GetSchemaHealthStatus()
    {
        lock (_schemaHealthLock)
        {
            return (_inputSchemaHealthy, _outputSchemaHealthy, _schemaIdsValid, _inputSchemaErrorMessage, _outputSchemaErrorMessage, _schemaValidationErrorMessage);
        }
    }

    public string GetCacheMapName()
    {
        return _activityCacheConfig.MapName;
    }

    public async Task<string?> GetCachedDataAsync(Guid orchestratedFlowId, Guid correlationId, Guid executionId, Guid stepId, Guid publishId, Guid processorId)
    {
        // Create hierarchical context from available parameters
        var context = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId,
            StepId = stepId,
            ProcessorId = processorId,
            PublishId = publishId,
            ExecutionId = executionId
        };

        return await GetCachedDataAsync(context);
    }

    public async Task<string?> GetCachedDataAsync(HierarchicalLoggingContext context)
    {

        var mapName = GetCacheMapName();
        var key = _cacheService.GetProcessorCacheKey(context.ProcessorId ?? Guid.Empty, context.OrchestratedFlowId, context.CorrelationId, context.ExecutionId ?? Guid.Empty, context.StepId ?? Guid.Empty, context.PublishId ?? Guid.Empty);

        _logger.LogDebugWithHierarchy(context, "Retrieving cached data. MapName: {MapName}",
            mapName);

        var result = await _cacheService.GetAsync(mapName, key, context);

        _logger.LogDebugWithHierarchy(context, "Cache retrieval result. MapName: {MapName}, Found: {Found}, DataLength: {DataLength}",
            mapName, result != null, result?.Length ?? 0);

        return result;
    }

    public async Task SaveCachedDataAsync(Guid orchestratedFlowId, Guid correlationId, Guid executionId, Guid stepId, Guid publishId, string? data, Guid processorId)
    {
        // Create hierarchical context from available parameters
        var context = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = orchestratedFlowId,
            CorrelationId = correlationId,
            StepId = stepId,
            ProcessorId = processorId,
            PublishId = publishId,
            ExecutionId = executionId
        };

        await SaveCachedDataAsync(context, data);
    }

    public async Task SaveCachedDataAsync(HierarchicalLoggingContext context, string? data)
    {

        // Don't save null data to cache
        if (data == null)
        {
            _logger.LogWarningWithHierarchy(context, "Attempted to save null data to cache. Skipping cache save.");
            return;
        }

        var mapName = GetCacheMapName();
        var key = _cacheService.GetProcessorCacheKey(context.ProcessorId ?? Guid.Empty, context.OrchestratedFlowId, context.CorrelationId, context.ExecutionId ?? Guid.Empty, context.StepId ?? Guid.Empty, context.PublishId ?? Guid.Empty);

        _logger.LogDebugWithHierarchy(context, "Saving cached data. MapName: {MapName}, DataLength: {DataLength}",
            mapName, data.Length);

        await _cacheService.SetAsync(mapName, key, data);

        // Log enriched success message with all the givens
        _logger.LogInformationWithHierarchy(context, "Saved data to cache. MapName: {MapName}, Key: {Key}, DataLength: {DataLength}",
            mapName, key, data.Length);

        _logger.LogDebugWithHierarchy(context, "Successfully saved cached data. MapName: {MapName}",
            mapName);
    }

    /// <summary>
    /// Validates data against the specified input schema
    /// </summary>
    /// <param name="data">The input data to validate</param>
    /// <param name="schemaDefinition">Schema definition to validate against</param>
    /// <param name="enableValidation">Whether validation is enabled</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if validation passes or is disabled, false if validation fails</returns>
    public async Task<bool> ValidateInputDataAsync(string data, string schemaDefinition, bool enableValidation, HierarchicalLoggingContext context)
    {
        if (!enableValidation)
        {
            return true;
        }

        if (string.IsNullOrEmpty(schemaDefinition))
        {
            _logger.LogWarningWithHierarchy(context, "Schema definition is not available. Skipping validation.");
            return false;
        }

        // Let the schema validator handle empty data - it should fail validation if data is required by schema
        return await _schemaValidator.ValidateAsync(data, schemaDefinition);
    }

    /// <summary>
    /// Validates data against the specified output schema
    /// </summary>
    /// <param name="data">The output data to validate</param>
    /// <param name="schemaDefinition">Schema definition to validate against</param>
    /// <param name="enableValidation">Whether validation is enabled</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if validation passes or is disabled, false if validation fails</returns>
    public async Task<bool> ValidateOutputDataAsync(string? data, string schemaDefinition, bool enableValidation, HierarchicalLoggingContext context)
    {
        if (!enableValidation)
        {
            return true;
        }

        if (string.IsNullOrEmpty(schemaDefinition))
        {
            _logger.LogWarningWithHierarchy(context, "Schema definition is not available. Skipping validation.");
            return false;
        }

        // Let the schema validator handle empty data - it should fail validation if data is required by schema
        // Convert null to empty string for schema validator (which expects non-nullable string)
        return await _schemaValidator.ValidateAsync(data ?? string.Empty, schemaDefinition);
    }

    /// <summary>
    /// Validates input data with comprehensive error handling and logging
    /// </summary>
    private async Task ValidateInputWithErrorHandlingAsync(
        string inputData,
        string inputSchemaDefinition,
        bool enableInputValidation,
        bool hasPluginAssignment,
        Guid? pluginEntityId,
        ProcessorActivityMessage message,
        HierarchicalLoggingContext context)
    {
        if (!await ValidateInputDataAsync(inputData, inputSchemaDefinition, enableInputValidation, context))
        {
            var errorMessage = hasPluginAssignment
                ? $"Input data validation failed against PluginAssignmentModel schema for entity {pluginEntityId}"
                : "Input data validation failed against InputSchema";

            if (hasPluginAssignment)
            {
                _logger.LogErrorWithHierarchy(context,
                    "{ErrorMessage}. PluginEntityId: {PluginEntityId}",
                    errorMessage, pluginEntityId);
            }
            else
            {
                _logger.LogErrorWithHierarchy(context,
                    "{ErrorMessage}",
                    errorMessage);
            }

            throw new InvalidOperationException($"{errorMessage} for ExecutionId: {message.ExecutionId}");
        }
    }

    /// <summary>
    /// Validates output data with comprehensive error handling and logging
    /// </summary>
    private async Task ValidateOutputWithErrorHandlingAsync(
        string? outputData,
        string outputSchemaDefinition,
        bool enableOutputValidation,
        bool hasPluginAssignment,
        Guid? pluginEntityId,
        ProcessorActivityMessage message,
        HierarchicalLoggingContext context)
    {
        if (!await ValidateOutputDataAsync(outputData, outputSchemaDefinition, enableOutputValidation, context))
        {
            var errorMessage = hasPluginAssignment
                ? $"Output data validation failed against PluginAssignmentModel schema for entity {pluginEntityId}"
                : "Output data validation failed against OutputSchema";

            if (hasPluginAssignment)
            {
                _logger.LogErrorWithHierarchy(context,
                    "{ErrorMessage}. PluginEntityId: {PluginEntityId}",
                    errorMessage, pluginEntityId ?? Guid.Empty);
            }
            else
            {
                _logger.LogErrorWithHierarchy(context,
                    "{ErrorMessage}",
                    errorMessage);
            }

            throw new InvalidOperationException($"{errorMessage} for ExecutionId: {context.ExecutionId}");
        }
    }

    public async Task<IEnumerable<ProcessorActivityResponse>> ProcessActivityAsync(ProcessorActivityMessage message)
    {
        using var activity = _activitySource.StartActivityWithCorrelation("ProcessActivity");
        var stopwatch = Stopwatch.StartNew();

        activity?.SetActivityExecutionTagsWithCorrelation(
            message.OrchestratedFlowId,
            message.StepId,
            message.ExecutionId)
            ?.SetEntityTags(message.Entities.Count);

        // Create Layer 5 hierarchical context for processor service
        var processorContext = new HierarchicalLoggingContext
        {
            OrchestratedFlowId = message.OrchestratedFlowId,
            WorkflowId = message.WorkflowId,
            CorrelationId = message.CorrelationId,
            StepId = message.StepId,
            ProcessorId = message.ProcessorId,
            PublishId = message.PublishId,
            ExecutionId = message.ExecutionId
        };

        _logger.LogInformationWithHierarchy(processorContext,
            "Processing activity. EntitiesCount: {EntitiesCount}",
            message.Entities.Count);

        // Early detection of PluginAssignmentModel and setup validation parameters
        var pluginAssignment = message.Entities.OfType<PluginAssignmentModel>().FirstOrDefault();
        
        // Set validation parameters based on entity type
        string inputSchemaDefinition;
        bool enableInputValidation;
        string outputSchemaDefinition;
        bool enableOutputValidation;
        Guid? pluginEntityId = null;

        if (pluginAssignment != null)
        {
            // Use PluginAssignmentModel schema parameters
            inputSchemaDefinition = pluginAssignment.InputSchemaDefinition;
            enableInputValidation = pluginAssignment.EnableInputValidation;
            outputSchemaDefinition = pluginAssignment.OutputSchemaDefinition;
            enableOutputValidation = pluginAssignment.EnableOutputValidation;
            pluginEntityId = pluginAssignment.EntityId;
        }
        else
        {
            // Use processor's own schema parameters
            inputSchemaDefinition = _config.InputSchemaDefinition;
            enableInputValidation = _validationConfig.EnableInputValidation;
            outputSchemaDefinition = _config.OutputSchemaDefinition;
            enableOutputValidation = _validationConfig.EnableOutputValidation;
        }

        try
        {
            string inputData;

            // Handle special case when ExecutionId is empty
            if (message.ExecutionId == Guid.Empty)
            {
                _logger.LogInformationWithHierarchy(processorContext,
                    "ExecutionId is empty - skipping cache retrieval and input validation.");

                // Skip cache retrieval and use empty string as input data
                inputData = string.Empty;
            }
            else
            {
                // 1. Retrieve data from cache (normal case)
                inputData = await GetCachedDataAsync(processorContext) ?? string.Empty;

                // 2. Validate input data using determined schema parameters
                await ValidateInputWithErrorHandlingAsync(
                    inputData,
                    inputSchemaDefinition,
                    enableInputValidation,
                    pluginAssignment != null,
                    pluginEntityId,
                    message,
                    processorContext);
            }

            // 3. Execute the activity

            var resultDataCollection = await _activityExecutor.ExecuteActivityAsync(
                message.OrchestratedFlowId,
                message.WorkflowId, // ✅ Include WorkflowId from message
                message.CorrelationId,
                message.StepId,
                message.ProcessorId,
                message.PublishId,
                message.ExecutionId,
                message.Entities,
                inputData);

            var responses = new List<ProcessorActivityResponse>();

            // Process each result item
            foreach (var resultData in resultDataCollection)
            {
                processorContext.ExecutionId = resultData.ExecutionId;

                try
                {

                    // Handle effectively empty SerializedData from failed activity execution
                    if (DataValidation.IsEffectivelyEmptyData(resultData.SerializedData))
                    {
                        _logger.LogWarningWithHierarchy(processorContext,
                            "Activity execution produced effectively empty output data. Skipping validation and cache save. Data: '{Data}'",
                            resultData.SerializedData ?? "null");

                        // Continue to response creation (no validation, no cache save)
                    }
                    else
                    {
                        // 5. Validate output data using determined schema parameters
                        await ValidateOutputWithErrorHandlingAsync(
                            resultData.SerializedData,
                            outputSchemaDefinition,
                            enableOutputValidation,
                            pluginAssignment != null,
                            pluginEntityId,
                            message,
                            processorContext);

                        // Validation passed - save to cache
                        if (resultData.ExecutionId != Guid.Empty)
                        {
                            await SaveCachedDataAsync(processorContext, resultData.SerializedData);
                        }
                        else
                        {
                            _logger.LogWarningWithHierarchy(processorContext,
                                "ExecutionId is empty - skipping cache save. OriginalExecutionId: {OriginalExecutionId}",
                                message.ExecutionId);
                        }
                    }

                    // Create response for this item
                    var response = new ProcessorActivityResponse
                    {
                        ProcessorId = message.ProcessorId,
                        OrchestratedFlowId = message.OrchestratedFlowId,
                        StepId = message.StepId,
                        ExecutionId = resultData.ExecutionId,
                        Status = resultData.Status,
                        CorrelationId = message.CorrelationId,
                        ErrorMessage = resultData.Status == ActivityExecutionStatus.Failed ? resultData.Result : null,
                        Duration = stopwatch.Elapsed
                    };

                    responses.Add(response);

                    _logger.LogInformationWithHierarchy(processorContext,
                        "Successfully processed activity item.");
                }
                catch (Exception itemEx)
                {
                    _logger.LogErrorWithHierarchy(processorContext, itemEx,
                        "Error processing result data. Processing will continue.");

                    // Create failed response for this item
                    var failedResponse = new ProcessorActivityResponse
                    {
                        ProcessorId = message.ProcessorId,
                        OrchestratedFlowId = message.OrchestratedFlowId,
                        StepId = message.StepId,
                        ExecutionId = resultData.ExecutionId,
                        Status = ActivityExecutionStatus.Failed,
                        CorrelationId = message.CorrelationId,
                        ErrorMessage = itemEx.Message,
                        Duration = stopwatch.Elapsed
                    };

                    responses.Add(failedResponse);
                }
                
            }

            stopwatch.Stop();

            // Record performance metrics if available
            _performanceMetricsService?.RecordActivity(true, stopwatch.Elapsed.TotalMilliseconds);

            activity?.SetTag(ActivityTags.ActivityStatus, ActivityExecutionStatus.Completed.ToString())
                    ?.SetTag(ActivityTags.ActivityDuration, stopwatch.ElapsedMilliseconds);

            _logger.LogInformationWithHierarchy(processorContext,
                "Successfully processed activity collection. ItemCount: {ItemCount}, Duration: {Duration}ms",
                responses.Count, stopwatch.ElapsedMilliseconds);

            return responses;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Record performance metrics if available
            _performanceMetricsService?.RecordActivity(false, stopwatch.Elapsed.TotalMilliseconds);

            // Record exception metrics
            _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

            activity?.SetErrorTags(ex)
                    ?.SetTag(ActivityTags.ActivityStatus, ActivityExecutionStatus.Failed.ToString())
                    ?.SetTag(ActivityTags.ActivityDuration, stopwatch.ElapsedMilliseconds);

            _logger.LogErrorWithHierarchy(processorContext, ex,
                "Failed to process activity. Duration: {Duration}ms",
                stopwatch.ElapsedMilliseconds);

            var processorId = _processorId ?? Guid.Empty;
            return new[]
            {
                new ProcessorActivityResponse
                {
                    ProcessorId = processorId,
                    OrchestratedFlowId = message.OrchestratedFlowId,
                    StepId = message.StepId,
                    ExecutionId = message.ExecutionId,
                    Status = ActivityExecutionStatus.Failed,
                    CorrelationId = message.CorrelationId,
                    ErrorMessage = ex.Message,
                    Duration = stopwatch.Elapsed
                }
            };
        }
    }

    public async Task<ProcessorHealthResponse> GetHealthStatusAsync()
    {
        using var activity = _activitySource.StartActivity("GetHealthStatus");
        var processorId = await GetProcessorIdAsync();

        activity?.SetProcessorTags(processorId, _config.Name, _config.Version);

        try
        {
            var healthChecks = new Dictionary<string, HealthCheckResult>();

            // Check initialization status first
            bool isInitialized, isInitializing;
            string initializationError;
            lock (_initializationLock)
            {
                isInitialized = _isInitialized;
                isInitializing = _isInitializing;
                initializationError = _initializationErrorMessage;
            }

            healthChecks["initialization"] = new HealthCheckResult
            {
                Status = isInitialized ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = "Processor initialization status",
                Data = new Dictionary<string, object>
                {
                    ["initialized"] = isInitialized,
                    ["initializing"] = isInitializing,
                    ["error_message"] = initializationError
                }
            };

            // Check cache health
            var cacheHealthy = await _cacheService.IsHealthyAsync();
            healthChecks["cache"] = new HealthCheckResult
            {
                Status = cacheHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = "Hazelcast cache connectivity",
                Data = new Dictionary<string, object> { ["connected"] = cacheHealthy }
            };

            // Check message bus health (basic check)
            var busHealthy = _bus != null;
            healthChecks["messagebus"] = new HealthCheckResult
            {
                Status = busHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = "MassTransit message bus connectivity",
                Data = new Dictionary<string, object> { ["connected"] = busHealthy }
            };

            // Check schema health including schema ID validation and implementation hash validation
            bool inputSchemaHealthy, outputSchemaHealthy, schemaIdsValid, implementationHashValid;
            string inputSchemaError, outputSchemaError, schemaValidationError, implementationHashError;

            lock (_schemaHealthLock)
            {
                inputSchemaHealthy = _inputSchemaHealthy;
                outputSchemaHealthy = _outputSchemaHealthy;
                schemaIdsValid = _schemaIdsValid;
                inputSchemaError = _inputSchemaErrorMessage;
                outputSchemaError = _outputSchemaErrorMessage;
                schemaValidationError = _schemaValidationErrorMessage;
                implementationHashValid = _implementationHashValid;
                implementationHashError = _implementationHashErrorMessage;
            }

            healthChecks["schema_validation"] = new HealthCheckResult
            {
                Status = schemaIdsValid ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = "Schema ID validation against processor configuration",
                Data = new Dictionary<string, object>
                {
                    ["valid"] = schemaIdsValid,
                    ["input_validation_enabled"] = _validationConfig.EnableInputValidation,
                    ["output_validation_enabled"] = _validationConfig.EnableOutputValidation,
                    ["config_input_schema_id"] = _config.InputSchemaId.ToString(),
                    ["config_output_schema_id"] = _config.OutputSchemaId.ToString(),
                    ["validation_error"] = schemaValidationError
                }
            };

            healthChecks["input_schema"] = new HealthCheckResult
            {
                Status = inputSchemaHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = _validationConfig.EnableInputValidation ? "Input schema definition availability" : "Input schema validation disabled",
                Data = new Dictionary<string, object>
                {
                    ["available"] = inputSchemaHealthy,
                    ["validation_enabled"] = _validationConfig.EnableInputValidation,
                    ["schema_id"] = _config.InputSchemaId.ToString(),
                    ["error_message"] = inputSchemaError
                }
            };

            healthChecks["output_schema"] = new HealthCheckResult
            {
                Status = outputSchemaHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = _validationConfig.EnableOutputValidation ? "Output schema definition availability" : "Output schema validation disabled",
                Data = new Dictionary<string, object>
                {
                    ["available"] = outputSchemaHealthy,
                    ["validation_enabled"] = _validationConfig.EnableOutputValidation,
                    ["schema_id"] = _config.OutputSchemaId.ToString(),
                    ["error_message"] = outputSchemaError
                }
            };

            healthChecks["implementation_hash"] = new HealthCheckResult
            {
                Status = implementationHashValid ? HealthStatus.Healthy : HealthStatus.Unhealthy,
                Description = "Implementation hash validation for version integrity",
                Data = new Dictionary<string, object>
                {
                    ["valid"] = implementationHashValid,
                    ["processor_version"] = _config.Version,
                    ["validation_error"] = implementationHashError
                }
            };

            var overallStatus = healthChecks.Values.All(h => h.Status == HealthStatus.Healthy)
                ? HealthStatus.Healthy
                : HealthStatus.Unhealthy;

            // Create detailed message based on health status
            string healthMessage;
            if (overallStatus == HealthStatus.Healthy)
            {
                healthMessage = "All systems operational";
            }
            else
            {
                var unhealthyComponents = healthChecks
                    .Where(h => h.Value.Status != HealthStatus.Healthy)
                    .Select(h => h.Key)
                    .ToList();

                if (!isInitialized)
                {
                    if (isInitializing)
                    {
                        healthMessage = "Processor is initializing";
                    }
                    else
                    {
                        healthMessage = string.IsNullOrEmpty(initializationError)
                            ? "Processor not yet initialized"
                            : $"Processor initialization failed: {initializationError}";
                    }
                }
                else
                {
                    healthMessage = $"Processor is unhealthy. Failed components: {string.Join(", ", unhealthyComponents)}";

                    // Add specific error details if components are unhealthy
                    var errorDetails = new List<string>();
                    if (!schemaIdsValid) errorDetails.Add($"Schema validation: {schemaValidationError}");
                    if (!inputSchemaHealthy) errorDetails.Add($"Input schema: {inputSchemaError}");
                    if (!outputSchemaHealthy) errorDetails.Add($"Output schema: {outputSchemaError}");
                    if (!implementationHashValid) errorDetails.Add($"Implementation hash: {implementationHashError}");

                    if (errorDetails.Any())
                    {
                        healthMessage += $". Error details: {string.Join("; ", errorDetails)}";
                    }
                }
            }

            return new ProcessorHealthResponse
            {
                ProcessorId = processorId,
                Status = overallStatus,
                Message = healthMessage,
                HealthCheckInterval = GetHealthCheckIntervalFromConfig(),
                HealthChecks = healthChecks,
                Uptime = DateTime.UtcNow - _startTime,
                Metadata = new ProcessorMetadata
                {
                    Version = _config.Version,
                    Name = _config.Name,
                    StartTime = _startTime,
                    HostName = Environment.MachineName,
                    ProcessId = Environment.ProcessId,
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
                }
            };
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);

            // Record health status retrieval exception
            _healthMetricsService?.RecordException(ex.GetType().Name, "error", Guid.Empty);

            _logger.LogErrorWithCorrelation(ex, "Failed to get health status for ProcessorId: {ProcessorId}", processorId);

            return new ProcessorHealthResponse
            {
                ProcessorId = processorId,
                Status = HealthStatus.Unhealthy,
                Message = $"Health check failed: {ex.Message}",
                HealthCheckInterval = GetHealthCheckIntervalFromConfig(),
                Uptime = DateTime.UtcNow - _startTime,
                Metadata = new ProcessorMetadata
                {
                    Version = _config.Version,
                    Name = _config.Name,
                    StartTime = _startTime,
                    HostName = Environment.MachineName,
                    ProcessId = Environment.ProcessId,
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
                }
            };
        }
    }

    public async Task<ProcessorStatisticsResponse> GetStatisticsAsync(DateTime? startTime, DateTime? endTime)
    {
        using var activity = _activitySource.StartActivity("GetStatistics");
        var processorId = await GetProcessorIdAsync();

        activity?.SetProcessorTags(processorId, _config.Name, _config.Version);

        try
        {
            // For now, return basic metrics
            // In a production system, you might want to store more detailed statistics
            var periodStart = startTime ?? _startTime;
            var periodEnd = endTime ?? DateTime.UtcNow;

            return new ProcessorStatisticsResponse
            {
                ProcessorId = processorId,
                TotalActivitiesProcessed = 0, // Would need to implement proper tracking
                SuccessfulActivities = 0,
                FailedActivities = 0,
                AverageExecutionTime = TimeSpan.Zero,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                CollectedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            activity?.SetErrorTags(ex);

            // Record statistics retrieval exception
            _healthMetricsService?.RecordException(ex.GetType().Name, "error", Guid.Empty);

            _logger.LogErrorWithCorrelation(ex, "Failed to get statistics for ProcessorId: {ProcessorId}", processorId);
            throw;
        }
    }

    /// <summary>
    /// Gets the implementation hash for the current processor using reflection
    /// </summary>
    /// <returns>The SHA-256 hash of the processor implementation</returns>
    private string GetImplementationHash()
    {
        try
        {
            // Use reflection to find the ProcessorImplementationHash class in the entry assembly
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                _logger.LogWarningWithCorrelation("Entry assembly not found. Using empty implementation hash.");
                return string.Empty;
            }

            var hashType = entryAssembly.GetTypes()
                .FirstOrDefault(t => t.Name == "ProcessorImplementationHash");

            if (hashType == null)
            {
                _logger.LogWarningWithCorrelation("ProcessorImplementationHash class not found in entry assembly. Using empty implementation hash.");
                return string.Empty;
            }

            // Try to get Hash as a property first, then as a field
            var hashProperty = hashType.GetProperty("Hash", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            string hash = string.Empty;

            if (hashProperty != null)
            {
                hash = hashProperty.GetValue(null) as string ?? string.Empty;
                _logger.LogDebugWithCorrelation("Retrieved implementation hash from property: {Hash}", hash);
            }
            else
            {
                // Try to get Hash as a field (const field)
                var hashField = hashType.GetField("Hash", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (hashField != null)
                {
                    hash = hashField.GetValue(null) as string ?? string.Empty;
                    _logger.LogDebugWithCorrelation("Retrieved implementation hash from field: {Hash}", hash);
                }
                else
                {
                    _logger.LogWarningWithCorrelation("Hash property or field not found in ProcessorImplementationHash class. Using empty implementation hash.");
                    return string.Empty;
                }
            }
            _logger.LogInformationWithCorrelation("Retrieved implementation hash: {Hash}", hash);
            return hash;
        }
        catch (Exception ex)
        {
            // Record implementation hash retrieval exception
            _healthMetricsService?.RecordException(ex.GetType().Name, "warning", Guid.Empty);

            _logger.LogErrorWithCorrelation(ex, "Error retrieving implementation hash. Using empty hash.");
            return string.Empty;
        }
    }

    /// <summary>
    /// Validates that the processor entity's implementation hash matches the current implementation
    /// </summary>
    /// <param name="processorEntity">The processor entity retrieved from the query</param>
    /// <returns>True if implementation hashes match, false otherwise</returns>
    private bool ValidateImplementationHash(ProcessorEntity processorEntity)
    {
        using var activity = _activitySource.StartActivity("ValidateImplementationHash");
        activity?.SetTag("processor.id", processorEntity.Id.ToString());

        try
        {
            var currentHash = GetImplementationHash();
            var storedHash = processorEntity.ImplementationHash ?? string.Empty;

            activity?.SetTag("current.hash", currentHash)
                    ?.SetTag("stored.hash", storedHash);

            // If current hash is empty (couldn't retrieve), skip validation
            if (string.IsNullOrEmpty(currentHash))
            {
                _logger.LogWarningWithCorrelation(
                    "Current implementation hash is empty, skipping hash validation. ProcessorId: {ProcessorId}",
                    processorEntity.Id);
                return true;
            }

            // If stored hash is empty, this is an old processor without hash - allow it
            if (string.IsNullOrEmpty(storedHash))
            {
                _logger.LogInformationWithCorrelation(
                    "Stored implementation hash is empty (legacy processor), allowing initialization. ProcessorId: {ProcessorId}",
                    processorEntity.Id);
                return true;
            }

            bool hashesMatch = currentHash.Equals(storedHash, StringComparison.OrdinalIgnoreCase);

            // Update implementation hash validation status
            string validationErrorMessage = string.Empty;
            if (!hashesMatch)
            {
                validationErrorMessage = $"Implementation hash mismatch: Expected={storedHash}, Actual={currentHash}. Version increment required for processor {_config.GetCompositeKey()}.";
            }

            lock (_schemaHealthLock)
            {
                _implementationHashValid = hashesMatch;
                _implementationHashErrorMessage = hashesMatch ? string.Empty : validationErrorMessage;
            }

            if (hashesMatch)
            {
                _logger.LogInformationWithCorrelation(
                    "Implementation hash validation successful. ProcessorId: {ProcessorId}, Hash: {Hash}",
                    processorEntity.Id, currentHash);
            }
            else
            {
                _logger.LogErrorWithCorrelation(
                    "Implementation hash validation failed. ProcessorId: {ProcessorId}, " +
                    "Expected: {ExpectedHash}, Actual: {ActualHash}. " +
                    "Version increment required for processor {CompositeKey}.",
                    processorEntity.Id, storedHash, currentHash, _config.GetCompositeKey());
            }

            activity?.SetTag("validation.success", hashesMatch);
            return hashesMatch;
        }
        catch (Exception ex)
        {
            // Record implementation hash validation exception
            _healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);

            var errorMessage = $"Error during implementation hash validation: {ex.Message}";

            // Update implementation hash validation status for exception case
            lock (_schemaHealthLock)
            {
                _implementationHashValid = false;
                _implementationHashErrorMessage = errorMessage;
            }

            _logger.LogErrorWithCorrelation(ex, "Implementation hash validation failed with exception. ProcessorId: {ProcessorId}",
                processorEntity.Id);

            activity?.SetTag("validation.success", false)
                    ?.SetTag("validation.error", ex.Message);

            return false;
        }
    }

    /// <summary>
    /// Gets the health check interval from configuration in seconds
    /// </summary>
    /// <returns>Health check interval in seconds, defaults to 30 if not configured</returns>
    private int GetHealthCheckIntervalFromConfig()
    {
        try
        {
            var healthCheckIntervalString = _configuration["ProcessorHealthMonitor:HealthCheckInterval"];
            if (string.IsNullOrEmpty(healthCheckIntervalString))
            {
                return 30; // Default value
            }

            if (TimeSpan.TryParse(healthCheckIntervalString, out var interval))
            {
                return (int)interval.TotalSeconds;
            }

            return 30; // Default value if parsing fails
        }
        catch
        {
            return 30; // Default value if any exception occurs
        }
    }
}
