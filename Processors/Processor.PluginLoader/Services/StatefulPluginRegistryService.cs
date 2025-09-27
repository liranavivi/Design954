using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Processor.Base.Interfaces;
using Processor.PluginLoader.Interfaces;
using Processor.PluginLoader.Models;
using Shared.Correlation;
using Shared.Services.Interfaces;

namespace Processor.PluginLoader.Services;

/// <summary>
/// Service for managing stateful plugin instances in a distributed registry using Hazelcast
/// </summary>
public class StatefulPluginRegistryService : IStatefulPluginRegistryService
{
    private readonly ICacheService _cacheService;
    private readonly IProcessorService _processorService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StatefulPluginRegistryService> _logger;
    private readonly StatefulPluginRegistryConfiguration _config;
    private readonly JsonSerializerOptions _jsonOptions;

    public StatefulPluginRegistryService(
        ICacheService cacheService,
        IProcessorService processorService,
        IServiceProvider serviceProvider,
        ILogger<StatefulPluginRegistryService> logger,
        IOptions<StatefulPluginRegistryConfiguration> config)
    {
        _cacheService = cacheService;
        _processorService = processorService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _config = config.Value;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task RegisterStatefulPluginAsync(string registryKey, StatefulPluginMetadata metadata, HierarchicalLoggingContext context)
    {
        try
        {
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            await _cacheService.SetAsync(_config.MapName, registryKey, json, context);
            
            _logger.LogDebugWithHierarchy(context, "✅ Registered stateful plugin in registry: {RegistryKey}", registryKey);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to register stateful plugin: {RegistryKey}", registryKey);
            throw;
        }
    }

    public async Task UnregisterStatefulPluginAsync(string registryKey, HierarchicalLoggingContext context)
    {
        try
        {
            await _cacheService.RemoveAsync(_config.MapName, registryKey, context);
            
            _logger.LogDebugWithHierarchy(context, "🗑️ Unregistered stateful plugin from registry: {RegistryKey}", registryKey);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to unregister stateful plugin: {RegistryKey}", registryKey);
            throw;
        }
    }

    public async Task<IEnumerable<StatefulPluginMetadata>> GetAllStatefulPluginsAsync(HierarchicalLoggingContext context)
    {
        try
        {
            var allEntries = await _cacheService.GetAllEntriesAsync(_config.MapName, context);
            var plugins = new List<StatefulPluginMetadata>();

            foreach (var entry in allEntries)
            {
                if (!string.IsNullOrEmpty(entry.Value))
                {
                    try
                    {
                        var metadata = JsonSerializer.Deserialize<StatefulPluginMetadata>(entry.Value, _jsonOptions);
                        if (metadata != null)
                        {
                            plugins.Add(metadata);
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarningWithHierarchy(context, ex, "⚠️ Failed to deserialize plugin metadata for key: {RegistryKey}", entry.Key);
                    }
                }
            }

            _logger.LogDebugWithHierarchy(context, "📋 Retrieved {Count} stateful plugins from registry", plugins.Count);
            return plugins;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to get all stateful plugins from registry");
            throw;
        }
    }

    public async Task<bool> IsPluginStatefulAsync(string registryKey, HierarchicalLoggingContext context)
    {
        try
        {
            var result = await _cacheService.GetAsync(_config.MapName, registryKey, context);
            var isStateful = !string.IsNullOrEmpty(result);

            _logger.LogDebugWithHierarchy(context, "🔍 Plugin stateful check: {RegistryKey} = {IsStateful}", registryKey, isStateful);
            return isStateful;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to check if plugin is stateful: {RegistryKey}", registryKey);
            throw;
        }
    }

    public async Task PreloadStatefulPluginsAsync(HierarchicalLoggingContext context)
    {
        try
        {
            var processorId = await _processorService.GetProcessorIdAsync();
            var allStatefulPlugins = await GetAllStatefulPluginsAsync(context);

            // Filter plugins for current processor
            var processorPlugins = allStatefulPlugins.Where(p => p.ProcessorId == processorId).ToList();

            _logger.LogInformationWithHierarchy(context, "🔄 Starting preload of {Count} stateful plugins for processor {ProcessorId}",
                processorPlugins.Count, processorId);

            foreach (var plugin in processorPlugins)
            {
                try
                {
                    var pluginManager = PluginManagerFactory.GetPluginManager(
                        plugin.AssemblyBasePath, _serviceProvider, context);

                    // Force initialization of stateful plugin
                    await pluginManager.GetPluginInstanceAsync(
                        plugin.AssemblyName,
                        Version.Parse(plugin.Version),
                        plugin.TypeName,
                        isStateless: false,
                        context);

                    _logger.LogDebugWithHierarchy(context, "✅ Preloaded stateful plugin: {AssemblyName}:{Version}:{TypeName}",
                        plugin.AssemblyName, plugin.Version, plugin.TypeName);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to preload stateful plugin: {AssemblyName}:{Version}:{TypeName}",
                        plugin.AssemblyName, plugin.Version, plugin.TypeName);
                    throw;
                }
            }

            _logger.LogInformationWithHierarchy(context, "✅ Successfully preloaded {Count} stateful plugins", processorPlugins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to preload stateful plugins");
            throw;
        }
    }
}
