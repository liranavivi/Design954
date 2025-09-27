using Processor.PluginLoader.Models;
using Shared.Correlation;

namespace Processor.PluginLoader.Interfaces;

/// <summary>
/// Service for managing stateful plugin instances in a distributed registry
/// </summary>
public interface IStatefulPluginRegistryService
{
    /// <summary>
    /// Registers a plugin as stateful in the distributed registry
    /// </summary>
    /// <param name="registryKey">Registry key in format {ProcessorId}:{AssemblyName}:{Version}:{TypeName}</param>
    /// <param name="metadata">Plugin metadata</param>
    /// <param name="context">Hierarchical logging context</param>
    Task RegisterStatefulPluginAsync(string registryKey, StatefulPluginMetadata metadata, HierarchicalLoggingContext context);

    /// <summary>
    /// Removes a plugin from the stateful registry
    /// </summary>
    /// <param name="registryKey">Registry key in format {ProcessorId}:{AssemblyName}:{Version}:{TypeName}</param>
    /// <param name="context">Hierarchical logging context</param>
    Task UnregisterStatefulPluginAsync(string registryKey, HierarchicalLoggingContext context);

    /// <summary>
    /// Gets all stateful plugins from the registry
    /// </summary>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Collection of stateful plugin metadata</returns>
    Task<IEnumerable<StatefulPluginMetadata>> GetAllStatefulPluginsAsync(HierarchicalLoggingContext context);

    /// <summary>
    /// Preloads all stateful plugins for the current processor
    /// </summary>
    /// <param name="context">Hierarchical logging context</param>
    Task PreloadStatefulPluginsAsync(HierarchicalLoggingContext context);

    /// <summary>
    /// Checks if a plugin is registered as stateful
    /// </summary>
    /// <param name="registryKey">Registry key in format {ProcessorId}:{AssemblyName}:{Version}:{TypeName}</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>True if plugin is registered as stateful, false otherwise</returns>
    Task<bool> IsPluginStatefulAsync(string registryKey, HierarchicalLoggingContext context);
}
