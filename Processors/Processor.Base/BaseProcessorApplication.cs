using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Consumers;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Processor.Base.Services;
using Shared.Configuration;
using Shared.Correlation;
using Shared.Models;
using Shared.Services;
using Shared.Services.Interfaces;

namespace Processor.Base;

/// <summary>
/// Abstract base class for processor applications
/// </summary>
public abstract class BaseProcessorApplication
{
    private IHost? _host;
    private ILogger<BaseProcessorApplication>? _logger;
    private ProcessorConfiguration? _config;

    /// <summary>
    /// Protected property to access the service provider for derived classes
    /// </summary>
    protected IServiceProvider ServiceProvider => _host?.Services ?? throw new InvalidOperationException("Host not initialized");

    /// <summary>
    /// Creates a simple hierarchical logging context for application lifecycle events.
    /// Layer 1: CorrelationId only (for general application operations)
    /// </summary>
    private HierarchicalLoggingContext CreateApplicationContext()
    {
        // Get correlation ID from static context since we may not have DI available during startup
        return new HierarchicalLoggingContext
        {
            CorrelationId = CorrelationIdContext.GetCurrentCorrelationIdStatic()
        };
    }



    /// <summary>
    /// Abstract method that concrete processor implementations must override
    /// This is where the specific processor business logic should be implemented
    /// Returns a collection of ProcessedActivityData, each with a unique ExecutionId
    /// Enhanced with hierarchical logging support - maintains consistent ID ordering
    /// </summary>
    /// <param name="orchestratedFlowId">ID of the orchestrated flow entity (Layer 1)</param>
    /// <param name="workflowId">ID of the workflow (Layer 2)</param>
    /// <param name="correlationId">Correlation ID for tracking (Layer 3)</param>
    /// <param name="stepId">ID of the step being executed (Layer 4)</param>
    /// <param name="processorId">ID of the processor executing the activity (Layer 5)</param>
    /// <param name="publishId">Original publish ID for this activity instance (Layer 6)</param>
    /// <param name="executionId">Original execution ID for this activity instance (Layer 6)</param>
    /// <param name="entities">Collection of base entities to process</param>
    /// <param name="inputData">Deserialized input data object (null if input was empty, JsonElement if JSON data)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Collection of processed data, each with unique ExecutionId, that will be incorporated into the standard result structure</returns>
    public abstract Task<IEnumerable<ProcessedActivityData>> ProcessActivityDataAsync(
        // ✅ Consistent order: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId
        Guid orchestratedFlowId,
        Guid workflowId,
        Guid correlationId,
        Guid stepId,
        Guid processorId,
        Guid publishId,
        Guid executionId,

        // Supporting parameters
        List<AssignmentModel> entities,
        object? inputData,
        CancellationToken cancellationToken = default);



    /// <summary>
    /// Main entry point for the processor application
    /// Sets up infrastructure and starts the application
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code (0 for success, non-zero for failure)</returns>
    public async Task<int> RunAsync(string[] args)
    {
        // Initialize console output and display startup information
        var (processorName, processorVersion) = await InitializeConsoleAndDisplayStartupInfoAsync();

        try
        {
            Console.WriteLine($"🔧 Initializing {processorName} Application...");

            _host = CreateHostBuilder(args).Build();

            // Get logger and configuration from DI container
            _logger = _host.Services.GetRequiredService<ILogger<BaseProcessorApplication>>();

            // Get configuration with validation
            var configOptions = _host.Services.GetRequiredService<IOptions<ProcessorConfiguration>>();
            _config = configOptions.Value;

            // Validate configuration at runtime
            ValidateProcessorConfiguration(_config);

            // Create application-level hierarchical context for startup logging
            var appContext = CreateApplicationContext();

            _logger.LogInformationWithHierarchy(appContext, "Starting {ApplicationName}", GetType().Name);

            _logger.LogInformationWithHierarchy(appContext,
                "Initializing {ApplicationName} - {ProcessorName} v{ProcessorVersion}",
                GetType().Name, _config.Name, _config.Version);

            _logger.LogInformationWithHierarchy(appContext, "Starting host services (MassTransit, Hazelcast, etc.)...");

            // Start the host first (this will start MassTransit consumers)
            await _host.StartAsync();

            _logger.LogInformationWithHierarchy(appContext, "Host services started successfully. Now initializing processor...");

            // Force early initialization of metrics services to ensure meters are registered with OpenTelemetry
            // This must happen after host.StartAsync() but before processor initialization to ensure OpenTelemetry is ready
            var healthMetricsService = _host.Services.GetRequiredService<IProcessorHealthMetricsService>();
            var flowMetricsService = _host.Services.GetRequiredService<IProcessorFlowMetricsService>();

            // Allow derived classes to initialize their specific metrics services
            await InitializeCustomMetricsServicesAsync();

            // Allow derived classes to initialize their processor-specific services
            await InitializeProcessorSpecificServicesAsync();

            _logger.LogInformationWithHierarchy(appContext, "Host services initialized early to register meters with OpenTelemetry");

            // Initialize the processor service AFTER host is started
            var processorService = _host.Services.GetRequiredService<IProcessorService>();
            var initializationConfig = _host.Services.GetRequiredService<IOptions<ProcessorInitializationConfiguration>>().Value;

            if (initializationConfig.RetryEndlessly)
            {
                // Start initialization in background - don't wait for completion
                var appLifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
                _ = Task.Run(async () =>
                {
                    // Generate correlation ID for background initialization task
                    var correlationId = Guid.NewGuid();
                    CorrelationIdContext.SetCorrelationIdStatic(correlationId);

                    try
                    {
                        await processorService.InitializeAsync(appLifetime.ApplicationStopping);

                        // Create background task hierarchical context
                        var backgroundContext = CreateApplicationContext();
                        _logger.LogInformationWithHierarchy(backgroundContext, "Processor initialization completed successfully");
                    }
                    catch (OperationCanceledException)
                    {
                        // Create background task hierarchical context
                        var backgroundContext = CreateApplicationContext();
                        _logger.LogInformationWithHierarchy(backgroundContext, "Processor initialization cancelled during shutdown");
                    }
                    catch (Exception ex)
                    {
                        // Record background initialization exception
                        try
                        {
                            var healthMetricsService = _host?.Services?.GetService<IProcessorHealthMetricsService>();
                            healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);
                        }
                        catch
                        {
                            // Ignore metrics recording errors during critical failure
                        }

                        // Create background task hierarchical context
                        var backgroundContext = CreateApplicationContext();
                        _logger.LogErrorWithHierarchy(backgroundContext, ex, "Processor initialization failed unexpectedly");
                    }
                });

                _logger.LogInformationWithHierarchy(appContext,
                    "{ApplicationName} started successfully. Processor initialization is running in background with endless retry.",
                    GetType().Name);
            }
            else
            {
                // Legacy behavior: wait for initialization to complete
                await processorService.InitializeAsync();
                _logger.LogInformationWithHierarchy(appContext, "Processor initialization completed successfully");

                _logger.LogInformationWithHierarchy(appContext,
                    "{ApplicationName} started successfully and is ready to process activities",
                    GetType().Name);
            }

            // Wait for shutdown signal
            var lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
            await WaitForShutdownAsync(lifetime.ApplicationStopping);

            _logger.LogInformationWithHierarchy(appContext, "Shutting down {ApplicationName}", GetType().Name);

            // Stop the host gracefully
            await _host.StopAsync(TimeSpan.FromSeconds(30));

            _logger.LogInformationWithHierarchy(appContext, "{ApplicationName} stopped successfully", GetType().Name);

            Console.WriteLine($"✅ {processorName} Application completed successfully");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"🛑 {processorName} Application was cancelled");
            return 0;
        }
        catch (Exception ex)
        {
            if (_logger != null)
            {
                _logger.LogCritical(ex, "Fatal error occurred in {ApplicationName}", GetType().Name);
            }

            // Record critical exception metrics
            try
            {
                var healthMetricsService = _host?.Services?.GetService<IProcessorHealthMetricsService>();
                healthMetricsService?.RecordException(ex.GetType().Name, "critical", Guid.Empty);
            }
            catch
            {
                // Ignore metrics recording errors during critical failure
            }

            Console.WriteLine($"💥 {processorName} Application terminated unexpectedly: {ex.Message}");
            Console.WriteLine($"🔍 Error Context:");
            Console.WriteLine($"   • Exception Type: {ex.GetType().Name}");
            Console.WriteLine($"   • Message: {ex.Message}");
            Console.WriteLine($"   • Source: {ex.Source}");

            if (ex.InnerException != null)
            {
                Console.WriteLine($"   • Inner Exception: {ex.InnerException.Message}");
            }

            return 1;
        }
        finally
        {
            Console.WriteLine($"🧹 Shutting down {processorName} Application");
            _host?.Dispose();
        }
    }

    /// <summary>
    /// Initializes console output and displays startup information
    /// </summary>
    /// <returns>Tuple containing processor name and version</returns>
    private async Task<(string processorName, string processorVersion)> InitializeConsoleAndDisplayStartupInfoAsync()
    {
        // Force console output to be visible - bypass any logging framework interference
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.WriteLine("=== PROCESSOR STARTING ===");
        Console.WriteLine($"Current Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

        // Load early configuration to get processor info
        var configuration = LoadEarlyConfiguration();
        var processorConfig = GetProcessorConfigurationSafely(configuration);
        var processorName = processorConfig?.Name ?? "Unknown";
        var processorVersion = processorConfig?.Version ?? "Unknown";

        // Display application information
        DisplayApplicationInformation(processorName, processorVersion);

        // Perform environment validation
        await ValidateEnvironmentAsync();

        // Display configuration
        await DisplayConfigurationAsync();

        return (processorName, processorVersion);
    }

    /// <summary>
    /// Loads early configuration before host is built
    /// </summary>
    /// <returns>Configuration instance</returns>
    private IConfiguration LoadEarlyConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    /// <summary>
    /// Safely gets ProcessorConfiguration from IConfiguration with error handling
    /// </summary>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>ProcessorConfiguration or null if not available/invalid</returns>
    private ProcessorConfiguration? GetProcessorConfigurationSafely(IConfiguration configuration)
    {
        try
        {
            var processorConfig = configuration.GetSection("ProcessorConfiguration").Get<ProcessorConfiguration>();

            // Basic validation - ensure required properties are present
            if (processorConfig != null &&
                !string.IsNullOrWhiteSpace(processorConfig.Name) &&
                !string.IsNullOrWhiteSpace(processorConfig.Version))
            {
                return processorConfig;
            }

            Console.WriteLine("⚠️  Warning: ProcessorConfiguration is missing or has invalid required properties (Name, Version)");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Warning: Failed to load ProcessorConfiguration: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validates ProcessorConfiguration at runtime and throws if invalid
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <exception cref="InvalidOperationException">Thrown when configuration is invalid</exception>
    private void ValidateProcessorConfiguration(ProcessorConfiguration config)
    {
        if (config == null)
        {
            throw new InvalidOperationException("ProcessorConfiguration is null - this indicates a DI container configuration issue");
        }

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("ProcessorConfiguration.Name is required but not configured");
        }

        if (string.IsNullOrWhiteSpace(config.Version))
        {
            throw new InvalidOperationException("ProcessorConfiguration.Version is required but not configured");
        }

        // Get validation configuration to check if schema validation is enabled
        var validationConfig = _host?.Services.GetRequiredService<IOptions<Shared.Services.Models.SchemaValidationConfiguration>>()?.Value;

        // Only validate InputSchemaId if input validation is enabled
        if (validationConfig?.EnableInputValidation == true && config.InputSchemaId == Guid.Empty)
        {
            throw new InvalidOperationException("ProcessorConfiguration.InputSchemaId is required when input validation is enabled but not configured");
        }

        // Only validate OutputSchemaId if output validation is enabled
        if (validationConfig?.EnableOutputValidation == true && config.OutputSchemaId == Guid.Empty)
        {
            throw new InvalidOperationException("ProcessorConfiguration.OutputSchemaId is required when output validation is enabled but not configured");
        }
    }

    /// <summary>
    /// Displays application information
    /// </summary>
    /// <param name="processorName">Name of the processor</param>
    /// <param name="processorVersion">Version of the processor</param>
    private void DisplayApplicationInformation(string processorName, string processorVersion)
    {
        Console.WriteLine($"🌟 Starting {processorName} v{processorVersion}");
        Console.WriteLine("═══════════════════════════════════════════════════════");
        Console.WriteLine("📋 Application Information:");
        Console.WriteLine($"   🔖 Config Version: {processorVersion}");

        // Try to get implementation hash if available
        try
        {
            var hashType = Type.GetType("ProcessorImplementationHash");
            if (hashType != null)
            {
                var versionProp = hashType.GetProperty("Version");
                var hashProp = hashType.GetProperty("Hash");
                var sourceFileProp = hashType.GetProperty("SourceFile");
                var generatedAtProp = hashType.GetProperty("GeneratedAt");

                Console.WriteLine($"   📦 Assembly Version: {versionProp?.GetValue(null) ?? "Unknown"}");
                Console.WriteLine($"   🔐 SHA Hash: {hashProp?.GetValue(null) ?? "Unknown"}");
                Console.WriteLine($"   📝 Source File: {sourceFileProp?.GetValue(null) ?? "Unknown"}");
                Console.WriteLine($"   🕒 Hash Generated: {generatedAtProp?.GetValue(null) ?? "Unknown"}");
            }
        }
        catch
        {
            Console.WriteLine($"   📦 Assembly Version: Unknown");
            Console.WriteLine($"   🔐 SHA Hash: Unknown");
        }

        Console.WriteLine($"   🏷️  Processor Name: {processorName}");
        Console.WriteLine($"   🌍 Environment: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}");
        Console.WriteLine($"   🖥️  Machine: {Environment.MachineName}");
        Console.WriteLine($"   👤 User: {Environment.UserName}");
        Console.WriteLine($"   📁 Working Directory: {Environment.CurrentDirectory}");
        Console.WriteLine($"   🆔 Process ID: {Environment.ProcessId}");
        Console.WriteLine($"   ⚙️  .NET Version: {Environment.Version}");
        Console.WriteLine($"   🕒 Started At: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss UTC}");
        Console.WriteLine("═══════════════════════════════════════════════════════");

        // Allow derived classes to add custom application info
        DisplayCustomApplicationInfo();
    }

    /// <summary>
    /// Virtual method that derived classes can override to display custom application information
    /// </summary>
    protected virtual void DisplayCustomApplicationInfo()
    {
        // Default implementation does nothing
        // Derived classes can override to add custom information
    }

    /// <summary>
    /// Performs environment validation
    /// </summary>
    protected virtual async Task ValidateEnvironmentAsync()
    {
        Console.WriteLine("🔍 Performing Environment Validation...");

        var validationResults = new List<(string Check, bool Passed, string Message)>();

        // Check required environment variables
        var requiredEnvVars = new[] { "ASPNETCORE_ENVIRONMENT" };
        foreach (var envVar in requiredEnvVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            var passed = !string.IsNullOrEmpty(value);
            validationResults.Add((envVar, passed, passed ? $"✅ {envVar}={value}" : $"⚠️  {envVar} not set"));
        }

        // Check system resources
        var memoryMB = Environment.WorkingSet / 1024.0 / 1024.0;
        var memoryOk = memoryMB < 1000; // Less than 1GB
        validationResults.Add(("Memory", memoryOk,
            memoryOk ? $"✅ Memory usage: {memoryMB:F1} MB" : $"⚠️  High memory usage: {memoryMB:F1} MB"));

        // Check processor manager connectivity
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("http://localhost:5110/health");
            var passed = response.IsSuccessStatusCode;
            validationResults.Add(("ProcessorManager", passed,
                passed ? "✅ Processor Manager connectivity verified" : $"⚠️  Processor Manager returned: {response.StatusCode}"));
        }
        catch (Exception ex)
        {
            validationResults.Add(("ProcessorManager", false, $"⚠️  Processor Manager unreachable: {ex.Message}"));
        }

        // Check schema manager connectivity
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await client.GetAsync("http://localhost:5100/health");
            var passed = response.IsSuccessStatusCode;
            validationResults.Add(("SchemaManager", passed,
                passed ? "✅ Schema Manager connectivity verified" : $"⚠️  Schema Manager returned: {response.StatusCode}"));
        }
        catch (Exception ex)
        {
            validationResults.Add(("SchemaManager", false, $"⚠️  Schema Manager unreachable: {ex.Message}"));
        }

        // Allow derived classes to add custom validations
        await PerformCustomEnvironmentValidationAsync(validationResults);

        // Log validation results
        Console.WriteLine("📊 Environment Validation Results:");
        foreach (var (check, passed, message) in validationResults)
        {
            Console.WriteLine($"   {message}");
        }

        var passedCount = validationResults.Count(r => r.Passed);
        var totalCount = validationResults.Count;

        if (passedCount == totalCount)
        {
            Console.WriteLine($"🎉 All environment validations passed ({passedCount}/{totalCount})");
        }
        else
        {
            Console.WriteLine($"⚠️  Environment validation completed with warnings ({passedCount}/{totalCount} passed)");
            Console.WriteLine("💡 Warnings are normal if dependent services are not running yet");
        }

        Console.WriteLine("✅ Environment validation completed");
    }

    /// <summary>
    /// Virtual method that derived classes can override to add custom environment validations
    /// </summary>
    /// <param name="validationResults">List to add validation results to</param>
    protected virtual async Task PerformCustomEnvironmentValidationAsync(List<(string Check, bool Passed, string Message)> validationResults)
    {
        // Default implementation does nothing
        // Derived classes can override to add custom validations
        await Task.CompletedTask;
    }

    /// <summary>
    /// Displays configuration information
    /// </summary>
    protected virtual async Task DisplayConfigurationAsync()
    {
        Console.WriteLine("📋 Loading and Displaying Configuration...");

        try
        {
            var configuration = LoadEarlyConfiguration();

            // Read and display the entire appsettings.json content
            var appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (File.Exists(appSettingsPath))
            {
                var appSettingsContent = await File.ReadAllTextAsync(appSettingsPath);
                var formattedJson = JsonSerializer.Serialize(
                    JsonSerializer.Deserialize<object>(appSettingsContent),
                    new JsonSerializerOptions {
                        WriteIndented = true,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                Console.WriteLine("📄 Configuration Content:");
                Console.WriteLine(formattedJson);
            }

            Console.WriteLine("✅ Configuration display completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error displaying configuration: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates and configures the host builder with all necessary services
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Configured host builder</returns>
    protected virtual IHostBuilder CreateHostBuilder(string[] args)
    {
        // Find the project directory by looking for the .csproj file
        var currentDir = Directory.GetCurrentDirectory();
        var projectDir = FindProjectDirectory(currentDir);

        return Host.CreateDefaultBuilder(args)
            .UseContentRoot(projectDir)
            .UseEnvironment(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
            .ConfigureLogging(logging =>
            {
                // Allow derived classes to add additional logging providers before OpenTelemetry setup
                ConfigureLogging(logging);
            })
            .ConfigureServices((context, services) =>
            {
                // Configure application settings
                services.Configure<ProcessorConfiguration>(
                    context.Configuration.GetSection("ProcessorConfiguration"));

                services.Configure<Shared.Services.Models.SchemaValidationConfiguration>(
                    context.Configuration.GetSection("SchemaValidation"));
                services.Configure<ProcessorInitializationConfiguration>(
                    context.Configuration.GetSection("ProcessorInitialization"));
                services.Configure<ProcessorHealthMonitorConfiguration>(
                    context.Configuration.GetSection("ProcessorHealthMonitor"));
                services.Configure<ProcessorActivityDataCacheConfiguration>(
                    context.Configuration.GetSection("ProcessorActivityDataCache"));

                // Add core services
                RegisterProcessorService(services);
                services.AddSingleton(this);
                services.AddSingleton<ISchemaValidator, SchemaValidator>();

                // Add health monitoring services
                services.AddSingleton<IPerformanceMetricsService, PerformanceMetricsService>();
                services.AddSingleton<IProcessorHealthMetricsService, ProcessorHealthMetricsService>();
                services.AddSingleton<IProcessorHealthMonitor, ProcessorHealthMonitor>();
                services.AddHostedService<ProcessorHealthMonitor>();

                // Add flow metrics service (optimized for anomaly detection)
                services.AddSingleton<IProcessorFlowMetricsService, ProcessorFlowMetricsService>();
                
                // Add infrastructure services
                // Get processor configuration consistently using strongly-typed binding
                var processorConfig = context.Configuration.GetSection("ProcessorConfiguration").Get<ProcessorConfiguration>();

                // Validate processor configuration is available
                if (processorConfig == null)
                {
                    throw new InvalidOperationException("ProcessorConfiguration section is missing or invalid in appsettings.json");
                }

                // Validate required properties
                if (string.IsNullOrWhiteSpace(processorConfig.Name))
                {
                    throw new InvalidOperationException("ProcessorConfiguration:Name is required but not configured");
                }

                if (string.IsNullOrWhiteSpace(processorConfig.Version))
                {
                    throw new InvalidOperationException("ProcessorConfiguration:Version is required but not configured");
                }

                var compositeKey = processorConfig.GetCompositeKey();

                services.AddMassTransitBusProvider(context.Configuration, compositeKey,
                    typeof(ExecuteActivityCommandConsumer));
                services.AddHazelcastClient(context.Configuration);

                // Add queue handoff pattern services
                services.AddSingleton<RequestProcessingQueue>();
                services.AddSingleton<IRequestProcessingQueue>(provider => provider.GetRequiredService<RequestProcessingQueue>());
                services.AddHostedService<RequestProcessingService>();

                // Add response processing queue services (concurrent background processing)
                services.AddSingleton<ResponseProcessingQueue>();
                services.AddSingleton<IResponseProcessingQueue>(provider => provider.GetRequiredService<ResponseProcessingQueue>());
                services.AddHostedService<ResponseProcessingService>();

                // Add OpenTelemetry - use ProcessorConfiguration values consistently
                services.AddOpenTelemetryObservability(context.Configuration, processorConfig.Name, processorConfig.Version);

                // Allow derived classes to add custom services
                ConfigureServices(services, context.Configuration);
            });
    }

    /// <summary>
    /// Virtual method that derived classes can override to add custom services
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Configuration</param>
    protected virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Default implementation does nothing
        // Derived classes can override to add custom services
    }

    /// <summary>
    /// Virtual method that derived classes can override to configure logging
    /// This is called before OpenTelemetry logging is configured
    /// </summary>
    /// <param name="logging">Logging builder</param>
    protected virtual void ConfigureLogging(ILoggingBuilder logging)
    {
        // Default implementation does nothing
        // Derived classes can override to add custom logging providers
    }

    /// <summary>
    /// Virtual method that derived classes can override to initialize custom metrics services
    /// This is called after host.StartAsync() but before processor initialization
    /// </summary>
    protected virtual async Task InitializeCustomMetricsServicesAsync()
    {
        // Default implementation does nothing
        // Derived classes can override to initialize their specific metrics services
        await Task.CompletedTask;
    }

    /// <summary>
    /// Virtual method that derived classes can override to initialize processor-specific services
    /// This is called after InitializeCustomMetricsServicesAsync() but before processor initialization
    /// </summary>
    protected virtual async Task InitializeProcessorSpecificServicesAsync()
    {
        // Default implementation does nothing
        // Derived classes can override to initialize their processor-specific services
        await Task.CompletedTask;
    }

    /// <summary>
    /// Finds the project directory by looking for the .csproj file
    /// </summary>
    /// <param name="startDirectory">Directory to start searching from</param>
    /// <returns>Project directory path</returns>
    private static string FindProjectDirectory(string startDirectory)
    {
        var currentDir = new DirectoryInfo(startDirectory);

        // Look for .csproj file in current directory and parent directories
        while (currentDir != null)
        {
            var csprojFiles = currentDir.GetFiles("*.csproj");
            if (csprojFiles.Length > 0)
            {
                return currentDir.FullName;
            }

            // Check if we're in the BaseProcessor.Application directory specifically
            if (currentDir.Name == "FlowOrchestrator.BaseProcessor.Application")
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        // Fallback: try to find the BaseProcessor.Application directory
        var baseDir = startDirectory;
        var targetPath = Path.Combine(baseDir, "src", "Framework", "FlowOrchestrator.BaseProcessor.Application");
        if (Directory.Exists(targetPath))
        {
            return targetPath;
        }

        // Final fallback: use current directory
        return startDirectory;
    }

    /// <summary>
    /// Waits for shutdown signal
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the wait operation</returns>
    private static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        cancellationToken.Register(() => tcs.SetResult(true));

        // Also listen for Ctrl+C
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.SetResult(true);
        };

        await tcs.Task;
    }

    /// <summary>
    /// Virtual method to register the processor service - can be overridden by derived classes
    /// </summary>
    protected virtual void RegisterProcessorService(IServiceCollection services)
    {
        // Default implementation - register the abstract ProcessorService
        // This will fail at runtime since ProcessorService is abstract
        // Derived classes must override this method to register their concrete implementation
        throw new InvalidOperationException(
            "ProcessorService is abstract and cannot be instantiated directly. " +
            "Derived processor applications must override RegisterProcessorService to register their concrete ProcessorService implementation.");
    }
}


