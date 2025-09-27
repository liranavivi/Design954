using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Interfaces;
using Processor.Base.Models;
using Shared.Correlation;

namespace Processor.Base.Services;

/// <summary>
/// Service for recording processor flow metrics optimized for anomaly detection.
/// Follows the orchestrator pattern with focused metrics: consume counter, publish counter, and anomaly detection.
/// Reduces metric volume while focusing on important operational issues.
/// </summary>
public class ProcessorFlowMetricsService : IProcessorFlowMetricsService
{
    private readonly ProcessorConfiguration _config;
    private readonly ILogger<ProcessorFlowMetricsService> _logger;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?>[] _baseLabels;

    // Command Consumption Metrics
    private readonly Counter<long> _commandsConsumedCounter;
    private readonly Counter<long> _commandsConsumedSuccessfulCounter;
    private readonly Counter<long> _commandsConsumedFailedCounter;

    // Event Publishing Metrics
    private readonly Counter<long> _eventsPublishedCounter;
    private readonly Counter<long> _eventsPublishedSuccessfulCounter;
    private readonly Counter<long> _eventsPublishedFailedCounter;

    // Anomaly Detection Metrics
    private readonly Gauge<long> _flowAnomalyGauge;
    
    public ProcessorFlowMetricsService(
        IOptions<ProcessorConfiguration> config,
        ILogger<ProcessorFlowMetricsService> logger)
    {
        _config = config.Value;
        _logger = logger;

        // Initialize base labels for this metrics service
        _baseLabels = new KeyValuePair<string, object?>[]
        {
            new("processor_composite_key", _config.GetCompositeKey()),
            new("environment", Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "development")
        };



        // Use the recommended unique meter name pattern: {Version}_{Name}
        var meterName = $"{_config.Version}_{_config.Name}";
        _meter = new Meter($"{meterName}.Flow");

        // Initialize command consumption metrics (Core for Anomaly Detection)
        _commandsConsumedCounter = _meter.CreateCounter<long>(
            "processor_commands_consumed_total",
            "Total number of ExecuteActivityCommand messages consumed by the processor");

        _commandsConsumedSuccessfulCounter = _meter.CreateCounter<long>(
            "processor_commands_consumed_successful_total",
            "Total number of ExecuteActivityCommand messages successfully consumed");

        _commandsConsumedFailedCounter = _meter.CreateCounter<long>(
            "processor_commands_consumed_failed_total",
            "Total number of ExecuteActivityCommand messages that failed to consume");

        // Initialize event publishing metrics (Core for Anomaly Detection)
        _eventsPublishedCounter = _meter.CreateCounter<long>(
            "processor_events_published_total",
            "Total number of activity events published by the processor");

        _eventsPublishedSuccessfulCounter = _meter.CreateCounter<long>(
            "processor_events_published_successful_total",
            "Total number of activity events successfully published");

        _eventsPublishedFailedCounter = _meter.CreateCounter<long>(
            "processor_events_published_failed_total",
            "Total number of activity events that failed to publish");

        // Initialize flow anomaly detection metric
        _flowAnomalyGauge = _meter.CreateGauge<long>(
            "processor_flow_anomaly_difference",
            "Absolute difference between consumed commands and published events (anomaly indicator)");


        _logger.LogInformationWithCorrelation(
            "ProcessorFlowMetricsService initialized with meter name: {MeterName}, Composite Key: {CompositeKey}",
            $"{meterName}.Flow", _config.GetCompositeKey());
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
                "Flow anomaly detected for processor {CompositeKey}: Consumed={Consumed}, Published={Published}, Difference={Difference}, OrchestratedFlowId={OrchestratedFlowId}",
                _config.GetCompositeKey(), consumedCount, publishedCount, difference, orchestratedFlowId);
        }
    }



    public void Dispose()
    {
        _meter?.Dispose();
    }
}
