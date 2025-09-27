using System.Diagnostics.Metrics;
using Manager.Orchestrator.Interfaces;
using Microsoft.Extensions.Options;
using Shared.Correlation;
using Shared.Models;

namespace Manager.Orchestrator.Services;

/// <summary>
/// Service for recording orchestrator flow metrics optimized for anomaly detection.
/// Follows the processor pattern with focused metrics: consume counter, publish counter, and anomaly detection.
/// Reduces metric volume while focusing on important operational issues.
/// Uses consistent labeling from appsettings configuration (Name and Version).
/// </summary>
public class OrchestratorFlowMetricsService : IOrchestratorFlowMetricsService
{
    private readonly ManagerConfiguration _config;
    private readonly ILogger<OrchestratorFlowMetricsService> _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // Core Flow Metrics (Optimized for Anomaly Detection) - inline creation
    private readonly Counter<long> _commandsConsumedCounter;
    private readonly Counter<long> _commandsConsumedSuccessfulCounter;
    private readonly Counter<long> _commandsConsumedFailedCounter;

    private readonly Counter<long> _eventsPublishedCounter;
    private readonly Counter<long> _eventsPublishedSuccessfulCounter;
    private readonly Counter<long> _eventsPublishedFailedCounter;

    // Anomaly Detection Metric - inline creation
    private readonly Gauge<long> _flowAnomalyGauge;

    public OrchestratorFlowMetricsService(
        IOptions<ManagerConfiguration> config,
        ILogger<OrchestratorFlowMetricsService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Initialize base labels for this metrics service
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("orchestrator_composite_key", _config.GetCompositeKey()),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };

        // Use the recommended unique meter name pattern: {Version}_{Name}
        var meterName = $"{_config.Version}_{_config.Name}";
        var fullMeterName = $"{meterName}.Flow";
        _meter = new Meter(fullMeterName);

        // Initialize command consumption metrics (Core for Anomaly Detection) - inline creation
        _commandsConsumedCounter = _meter.CreateCounter<long>(
            "orchestrator_commands_consumed_total",
            "Total number of commands consumed by the orchestrator");

        _commandsConsumedSuccessfulCounter = _meter.CreateCounter<long>(
            "orchestrator_commands_consumed_successful_total",
            "Total number of commands consumed successfully by the orchestrator");

        _commandsConsumedFailedCounter = _meter.CreateCounter<long>(
            "orchestrator_commands_consumed_failed_total",
            "Total number of commands that failed to be consumed by the orchestrator");

        // Initialize event publishing metrics (Core for Anomaly Detection) - inline creation
        _eventsPublishedCounter = _meter.CreateCounter<long>(
            "orchestrator_events_published_total",
            "Total number of events published by the orchestrator");

        _eventsPublishedSuccessfulCounter = _meter.CreateCounter<long>(
            "orchestrator_events_published_successful_total",
            "Total number of events published successfully by the orchestrator");

        _eventsPublishedFailedCounter = _meter.CreateCounter<long>(
            "orchestrator_events_published_failed_total",
            "Total number of events that failed to be published by the orchestrator");

        // Initialize flow anomaly detection metric - inline creation
        _flowAnomalyGauge = _meter.CreateGauge<long>(
            "orchestrator_flow_anomaly_difference",
            "Absolute difference between consumed commands and published events (anomaly indicator)");

        // Single summary log (like ProcessorFlowMetricsService)
        _logger.LogInformationWithCorrelation(
            "OrchestratorFlowMetricsService initialized with meter name: {MeterName}, Composite Key: {CompositeKey}",
            fullMeterName, _config.GetCompositeKey());
    }

    public void RecordCommandConsumed(bool success, Guid orchestratedFlowId, Guid stepId, Guid executionId, Guid correlationId)
    {
        // Create flow labels with actual values
        var flowLabels = new KeyValuePair<string, object?>[]
        {
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + flowLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        flowLabels.CopyTo(tags, _baseLabels.Length);

        _commandsConsumedCounter.Add(1, tags);

        if (success)
            _commandsConsumedSuccessfulCounter.Add(1, tags);
        else
            _commandsConsumedFailedCounter.Add(1, tags);
    }

    public void RecordEventPublished(bool success, Guid orchestratedFlowId, Guid stepId, Guid executionId, Guid correlationId)
    {
        // Create flow labels with actual values
        var flowLabels = new KeyValuePair<string, object?>[]
        {
            new("orchestrated_flow_entity_id", orchestratedFlowId.ToString()),
            new("step_id", stepId.ToString()),
            new("execution_id", executionId.ToString()),
            new("correlation_id", correlationId)
        };

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + flowLabels.Length];
        _baseLabels.CopyTo(tags, 0);
        flowLabels.CopyTo(tags, _baseLabels.Length);

        _eventsPublishedCounter.Add(1, tags);

        if (success)
            _eventsPublishedSuccessfulCounter.Add(1, tags);
        else
            _eventsPublishedFailedCounter.Add(1, tags);
    }

    public void RecordFlowAnomaly(long consumedCount, long publishedCount, Guid orchestratedFlowId, Guid correlationId)
    {
        var difference = Math.Abs(consumedCount - publishedCount);
        var anomalyStatus = difference > 0 ? "anomaly_detected" : "healthy";

        var tags = new KeyValuePair<string, object?>[_baseLabels.Length + 3];
        _baseLabels.CopyTo(tags, 0);
        tags[_baseLabels.Length] = new("orchestrated_flow_entity_id", orchestratedFlowId.ToString());
        tags[_baseLabels.Length + 1] = new("anomaly_status", anomalyStatus);
        tags[_baseLabels.Length + 2] = new("correlation_id", correlationId);

        _flowAnomalyGauge.Record(difference, tags);

        if (difference > 0)
        {
            _logger.LogWarningWithCorrelation(
                "Flow anomaly detected: Consumed={Consumed}, Published={Published}, Difference={Difference}, OrchestratedFlowId={OrchestratedFlowId}",
                consumedCount, publishedCount, difference, orchestratedFlowId);
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
        _logger.LogInformationWithCorrelation("OrchestratorFlowMetricsService disposed");
    }
}
