namespace Processor.PluginLoader.Models;

/// <summary>
/// Configuration for the stateful plugin registry
/// </summary>
public class StatefulPluginRegistryConfiguration
{
    /// <summary>
    /// Name of the Hazelcast map for stateful plugin registry
    /// </summary>
    public string MapName { get; set; } = "stateful-plugin-registry";
}
