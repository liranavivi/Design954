namespace Processor.PluginLoader.Models;

/// <summary>
/// Configuration extracted from PluginAssignmentModel for plugin loading operations
/// Contains assembly information and plugin-specific settings
/// </summary>
public class PluginLoaderConfiguration
{
    // ========================================
    // 1. PLUGIN ASSEMBLY CONFIGURATION
    // ========================================

    /// <summary>
    /// Base path where plugin assemblies are stored
    /// </summary>
    public string AssemblyBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Name of the plugin assembly (without .dll extension)
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Version of the plugin assembly to load
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the plugin class that implements IPlugin
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    // ========================================
    // 2. PLUGIN EXECUTION CONFIGURATION
    // ========================================

    /// <summary>
    /// Optional timeout for plugin execution (in milliseconds)
    /// </summary>
    public int ExecutionTimeoutMs { get; set; } = 300000; // 5 minutes default

    /// <summary>
    /// Determines plugin instance caching behavior:
    /// - true: Always create fresh plugin instances (stateless mode)
    /// - false: Cache and reuse plugin instances (stateful mode)
    /// Note: PluginManager instances are always cached regardless of this setting
    /// </summary>
    public bool IsStateless { get; set; } = true; // Default to stateless for safety

}
