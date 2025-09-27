namespace Processor.PluginLoader.Models;

/// <summary>
/// Metadata for stateful plugin instances stored in the distributed registry
/// </summary>
public class StatefulPluginMetadata
{
    /// <summary>
    /// The processor ID that owns this stateful plugin instance
    /// </summary>
    public Guid ProcessorId { get; set; }

    /// <summary>
    /// Assembly name containing the plugin
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Version of the assembly
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the plugin class
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Base path where plugin assemblies are stored
    /// </summary>
    public string AssemblyBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the plugin was registered as stateful
    /// </summary>
    public DateTime RegisteredAt { get; set; }
}
