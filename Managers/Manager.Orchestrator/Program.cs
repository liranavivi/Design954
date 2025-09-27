using Manager.Orchestrator.Interfaces;
using Manager.Orchestrator.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Quartz;
using Shared.Configuration;
using Shared.Correlation;
using Shared.HealthChecks;
using Shared.Models;
using Shared.Services;
using Shared.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Clear default logging providers - OpenTelemetry will handle logging
builder.Logging.ClearProviders();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add CORS for Swagger UI
builder.Services.AddManagerCors();



// Add Manager Configuration
builder.Services.Configure<ManagerConfiguration>(builder.Configuration.GetSection("ManagerConfiguration"));

// Add Manager Metrics Service
builder.Services.AddSingleton<IManagerMetricsService, ManagerMetricsService>();

// Add Orchestrator Metrics Services
builder.Services.AddSingleton<IOrchestratorHealthMetricsService, OrchestratorHealthMetricsService>();
builder.Services.AddSingleton<IOrchestratorFlowMetricsService, OrchestratorFlowMetricsService>();

// Add MassTransit Bus Provider and register consumers
builder.Services.AddMassTransitBusProvider(builder.Configuration,
    typeof(Manager.Orchestrator.Consumers.ActivityExecutedEventConsumer),
    typeof(Manager.Orchestrator.Consumers.ActivityFailedEventConsumer));

// Add Hazelcast client and cache services
builder.Services.AddHazelcastClient(builder.Configuration);

// Add correlation ID services
builder.Services.AddCorrelationId();

// Add OpenTelemetry with correlation ID enrichment
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"];
var serviceVersion = builder.Configuration["OpenTelemetry:ServiceVersion"];
builder.Services.AddOpenTelemetryObservability(builder.Configuration, serviceName, serviceVersion);

// Add HTTP Client for cross-manager communication with correlation ID support
builder.Services.AddHttpClient<IManagerHttpClient, ManagerHttpClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddHttpMessageHandler<CorrelationIdDelegatingHandler>();

// Add HTTP Client Factory for warmup service
builder.Services.AddHttpClient();

// Add Quartz.NET services
builder.Services.AddQuartz(q =>
{
    q.UseSimpleTypeLoader();
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(tp =>
    {
        tp.MaxConcurrency = 10;
    });
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Add schema validation services
builder.Services.Configure<Shared.Services.Models.SchemaValidationConfiguration>(options =>
{
    // Always enable validation and logging for orchestrator
    options.EnableInputValidation = true;
    options.EnableOutputValidation = true;
    options.LogValidationWarnings = true;
    options.LogValidationErrors = true;
    options.IncludeValidationTelemetry = true;
});
builder.Services.AddScoped<ISchemaValidator, SchemaValidator>();

// Add orchestration services
builder.Services.AddScoped<IOrchestrationCacheService, OrchestrationCacheService>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();
builder.Services.AddScoped<IOrchestrationSchedulerService, OrchestrationSchedulerService>();

// Add Quartz job
builder.Services.AddScoped<Manager.Orchestrator.Jobs.OrchestratedFlowJob>();

// Add orchestrator health monitoring configuration
builder.Services.Configure<OrchestratorHealthMonitorConfiguration>(options =>
{
    var config = builder.Configuration.GetSection("OrchestratorHealthMonitor");
    options.Enabled = config.GetValue<bool>("Enabled", true);
    options.HealthCheckInterval = config.GetValue<TimeSpan>("HealthCheckInterval", TimeSpan.FromSeconds(30));
    options.CacheMapName = builder.Configuration.GetValue<string>("OrchestrationCache:MapName", "orchestration-data");
    options.LogHealthChecks = config.GetValue<bool>("LogHealthChecks", true);
    options.LogLevel = config.GetValue<string>("LogLevel", "Information");
    options.ContinueOnFailure = config.GetValue<bool>("ContinueOnFailure", true);
    options.MaxRetries = config.GetValue<int>("MaxRetries", 3);
    options.RetryDelay = config.GetValue<TimeSpan>("RetryDelay", TimeSpan.FromSeconds(1));
    options.UseExponentialBackoff = config.GetValue<bool>("UseExponentialBackoff", true);
});

// Add orchestrator health monitoring background service
builder.Services.AddHostedService<OrchestratorHealthMonitor>();

// Add Health Checks
builder.Services.AddHttpClient<OpenTelemetryHealthCheck>();
builder.Services.AddHealthChecks()
    .AddRabbitMQ(rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMQ:Username"]}:{builder.Configuration["RabbitMQ:Password"]}@{builder.Configuration["RabbitMQ:Host"]}:{builder.Configuration["RabbitMQ:Port"]}/")
    .AddCheck<OpenTelemetryHealthCheck>("opentelemetry");

var app = builder.Build();

// Configure the HTTP request pipeline
//if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Add correlation ID middleware early in the pipeline
app.UseCorrelationId();

app.UseHttpsRedirection();

// Add CORS middleware (must be before UseRouting)
app.UseManagerCors();
app.UseRouting();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        var metricsService = context.RequestServices.GetRequiredService<IManagerMetricsService>();
        var status = report.Status switch
        {
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy => 0,
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded => 1,
            _ => 2
        };
        metricsService.RecordHealthStatus(status);

        // Write simple JSON response
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { status = report.Status.ToString() }));
    }
});

try
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    // Note: Application startup logging will be updated when hierarchical context is available during startup
    app.Run();
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
}

// Make Program class accessible for testing
public partial class Program { }
