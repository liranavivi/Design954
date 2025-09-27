using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Shared.Correlation;

/// <summary>
/// Extension methods for ILogger that provide hierarchical logging with consistent 6-layer structure.
/// Maintains consistent ordering: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId
/// </summary>
public static class HierarchicalLoggerExtensions
{
    /// <summary>
    /// Logs a debug message with hierarchical context
    /// </summary>
    public static void LogDebugWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Debug, context, null, message, args);
    }

    /// <summary>
    /// Logs a debug message with hierarchical context and exception
    /// </summary>
    public static void LogDebugWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, Exception exception, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Debug, context, exception, message, args);
    }

    /// <summary>
    /// Logs an information message with hierarchical context
    /// </summary>
    public static void LogInformationWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Information, context, null, message, args);
    }

    /// <summary>
    /// Logs an information message with hierarchical context and exception
    /// </summary>
    public static void LogInformationWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, Exception exception, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Information, context, exception, message, args);
    }

    /// <summary>
    /// Logs a warning message with hierarchical context
    /// </summary>
    public static void LogWarningWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Warning, context, null, message, args);
    }

    /// <summary>
    /// Logs a warning message with hierarchical context and exception
    /// </summary>
    public static void LogWarningWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, Exception exception, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Warning, context, exception, message, args);
    }

    /// <summary>
    /// Logs an error message with hierarchical context
    /// </summary>
    public static void LogErrorWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Error, context, null, message, args);
    }

    /// <summary>
    /// Logs an error message with hierarchical context and exception
    /// </summary>
    public static void LogErrorWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, Exception exception, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Error, context, exception, message, args);
    }

    /// <summary>
    /// Logs a critical message with hierarchical context
    /// </summary>
    public static void LogCriticalWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Critical, context, null, message, args);
    }

    /// <summary>
    /// Logs a critical message with hierarchical context and exception
    /// </summary>
    public static void LogCriticalWithHierarchy(this ILogger logger,
        HierarchicalLoggingContext context, Exception exception, string message, params object?[] args)
    {
        LogWithHierarchy(logger, LogLevel.Critical, context, exception, message, args);
    }

    /// <summary>
    /// Creates a logging scope with hierarchical context for automatic inclusion in all log statements within the scope
    /// </summary>
    public static IDisposable? BeginHierarchicalScope(this ILogger logger, HierarchicalLoggingContext context)
    {
        var scopeState = new Dictionary<string, object>
        {
            ["HierarchyLayer"] = context.Layer
        };

        // Add hierarchy levels only if they have non-empty values
        if (context.OrchestratedFlowId != Guid.Empty)
            scopeState["OrchestratedFlowId"] = context.OrchestratedFlowId;

        if (context.WorkflowId.HasValue && context.WorkflowId.Value != Guid.Empty)
            scopeState["WorkflowId"] = context.WorkflowId.Value;

        if (context.CorrelationId != Guid.Empty)
            scopeState["CorrelationId"] = context.CorrelationId;

        if (context.StepId.HasValue && context.StepId.Value != Guid.Empty)
            scopeState["StepId"] = context.StepId.Value;

        if (context.ProcessorId.HasValue && context.ProcessorId.Value != Guid.Empty)
            scopeState["ProcessorId"] = context.ProcessorId.Value;

        if (context.PublishId.HasValue && context.PublishId.Value != Guid.Empty)
            scopeState["PublishId"] = context.PublishId.Value;

        if (context.ExecutionId.HasValue && context.ExecutionId.Value != Guid.Empty)
            scopeState["ExecutionId"] = context.ExecutionId.Value;

        return logger.BeginScope(scopeState);
    }

    /// <summary>
    /// Core logging method that handles hierarchical context integration
    /// </summary>
    private static void LogWithHierarchy(ILogger logger, LogLevel logLevel,
        HierarchicalLoggingContext context, Exception? exception, string message, params object?[] args)
    {
        if (!logger.IsEnabled(logLevel))
            return;

        // Create structured attributes for OpenTelemetry/Elasticsearch
        var hierarchyAttributes = BuildHierarchyAttributes(context);

        // Use a custom log state that preserves hierarchy attributes
        var logState = new HierarchicalLogState(message, args ?? Array.Empty<object?>(), hierarchyAttributes);

        // Log with custom state that includes hierarchy attributes
        if (exception != null)
        {
            logger.Log(logLevel, new EventId(), logState, exception, HierarchicalLogState.Formatter);
        }
        else
        {
            logger.Log(logLevel, new EventId(), logState, null, HierarchicalLogState.Formatter);
        }
    }
    
    /// <summary>
    /// Builds structured attributes for OpenTelemetry/Elasticsearch with consistent ordering
    /// </summary>
    private static Dictionary<string, object> BuildHierarchyAttributes(HierarchicalLoggingContext context)
    {
        var attributes = new Dictionary<string, object>
        {
            // Core hierarchy information
            ["HierarchyLayer"] = context.Layer
        };

        // Add hierarchy levels only if they have non-empty values
        // Consistent order: OrchestratedFlowId -> WorkflowId -> CorrelationId -> StepId -> ProcessorId -> PublishId -> ExecutionId

        if (context.OrchestratedFlowId != Guid.Empty)
            attributes["OrchestratedFlowId"] = context.OrchestratedFlowId.ToString();

        if (context.WorkflowId.HasValue && context.WorkflowId.Value != Guid.Empty)
            attributes["WorkflowId"] = context.WorkflowId.Value.ToString();

        if (context.CorrelationId != Guid.Empty)
            attributes["CorrelationId"] = context.CorrelationId.ToString();

        if (context.StepId.HasValue && context.StepId.Value != Guid.Empty)
            attributes["StepId"] = context.StepId.Value.ToString();

        if (context.ProcessorId.HasValue && context.ProcessorId.Value != Guid.Empty)
            attributes["ProcessorId"] = context.ProcessorId.Value.ToString();

        if (context.PublishId.HasValue && context.PublishId.Value != Guid.Empty)
            attributes["PublishId"] = context.PublishId.Value.ToString();

        if (context.ExecutionId.HasValue && context.ExecutionId.Value != Guid.Empty)
            attributes["ExecutionId"] = context.ExecutionId.Value.ToString();

        return attributes;
    }
}

/// <summary>
/// Template information cached for performance optimization
/// </summary>
internal readonly struct TemplateInfo
{
    public readonly string Template;
    public readonly string[] Placeholders;

    public TemplateInfo(string template, string[] placeholders)
    {
        Template = template;
        Placeholders = placeholders;
    }

    /// <summary>
    /// Formats the message template with provided arguments
    /// </summary>
    public string FormatMessage(object?[] args)
    {
        var result = Template;
        for (int i = 0; i < Math.Min(Placeholders.Length, args.Length); i++)
        {
            result = result.Replace(Placeholders[i], args[i]?.ToString() ?? "null");
        }
        return result;
    }

    /// <summary>
    /// Extracts parameter name from placeholder by removing curly braces
    /// </summary>
    public string GetParameterName(int index)
    {
        var placeholder = Placeholders[index];
        return placeholder.Substring(1, placeholder.Length - 2); // Remove { and }
    }
}

/// <summary>
/// Optimized custom log state with template caching that preserves hierarchical attributes alongside message template parameters
/// </summary>
internal class HierarchicalLogState : IReadOnlyList<KeyValuePair<string, object>>
{
    // Static cache for template parsing - thread-safe and bounded by unique template patterns
    private static readonly ConcurrentDictionary<string, TemplateInfo> _templateCache =
        new(StringComparer.Ordinal);

    private readonly string _formattedMessage;
    private readonly List<KeyValuePair<string, object>> _allAttributes;

    public HierarchicalLogState(string message, object?[] args, Dictionary<string, object> hierarchyAttributes)
    {
        _allAttributes = new List<KeyValuePair<string, object>>();

        // Add hierarchy attributes first (these have priority)
        foreach (var attr in hierarchyAttributes)
        {
            _allAttributes.Add(new KeyValuePair<string, object>(attr.Key, attr.Value));
        }

        // Process message template with caching
        if (args?.Length > 0)
        {
            // Get cached template info or parse and cache new template
            var templateInfo = _templateCache.GetOrAdd(message, ParseTemplate);

            // Format message using cached template structure
            _formattedMessage = templateInfo.FormatMessage(args);

            // Add template parameters as structured attributes for searchability
            for (int i = 0; i < Math.Min(templateInfo.Placeholders.Length, args.Length); i++)
            {
                // Handle nullable arguments properly
                var paramValue = args[i] ?? "null";
                _allAttributes.Add(new KeyValuePair<string, object>(
                    templateInfo.GetParameterName(i), paramValue));
            }
        }
        else
        {
            _formattedMessage = message;
        }
    }

    /// <summary>
    /// Parses message template to extract placeholders for caching
    /// </summary>
    private static TemplateInfo ParseTemplate(string template)
    {
        var regex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);
        var matches = regex.Matches(template);

        var placeholders = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            placeholders[i] = matches[i].Value; // Store "{MapName}" directly
        }

        return new TemplateInfo(template, placeholders);
    }

    /// <summary>
    /// Optimized formatter that returns pre-computed formatted message
    /// </summary>
    public static readonly Func<HierarchicalLogState, Exception?, string> Formatter =
        (state, exception) => state._formattedMessage;

    public KeyValuePair<string, object> this[int index] => _allAttributes[index];
    public int Count => _allAttributes.Count;
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _allAttributes.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
