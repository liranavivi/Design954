using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Correlation;

namespace Processor.PluginLoader.Services;

/// <summary>
/// Factory for managing PluginManager instances with two-level caching:
/// Level 1: PluginManager instances are always cached by AssemblyBasePath (regardless of IsStateless)
/// Level 2: Plugin instances are cached within PluginManager based on IsStateless flag
/// </summary>
public static class PluginManagerFactory
{
    /// <summary>
    /// Level 1 Cache: Always cache PluginManager instances by AssemblyBasePath
    /// Key: Normalized AssemblyBasePath, Value: PluginManager instance
    /// </summary>
    private static readonly ConcurrentDictionary<string, PluginManager> _managerCache = new();

    /// <summary>
    /// Gets or creates a PluginManager for the specified assembly base path.
    /// PluginManager instances are always cached regardless of IsStateless flag for performance.
    /// </summary>
    /// <param name="assemblyBasePath">Base path where plugin assemblies are stored</param>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Cached or new PluginManager instance</returns>
    public static PluginManager GetPluginManager(string assemblyBasePath, IServiceProvider serviceProvider, HierarchicalLoggingContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyBasePath);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        // Normalize the path to ensure consistent cache keys
        var normalizedPath = Path.GetFullPath(assemblyBasePath);

        var logger = serviceProvider.GetRequiredService<ILogger<PluginManager>>();
        
        // Always cache PluginManager - expensive to create (assembly loading, reflection, etc.)
        var pluginManager = _managerCache.GetOrAdd(normalizedPath, path =>
        {
            return new PluginManager(path, serviceProvider);
        });

        return pluginManager;
    }

    /// <summary>
    /// Gets the current count of cached PluginManager instances.
    /// Useful for monitoring and diagnostics.
    /// </summary>
    public static int CachedManagerCount => _managerCache.Count;

    /// <summary>
    /// Gets all cached assembly base paths.
    /// Useful for monitoring and diagnostics.
    /// </summary>
    public static IEnumerable<string> CachedAssemblyPaths => _managerCache.Keys.ToList();

    /// <summary>
    /// Clears the PluginManager cache and disposes all cached managers.
    /// Use with caution - this will dispose all cached PluginManager instances.
    /// </summary>
    public static void ClearManagerCache()
    {
        var managersToDispose = new List<PluginManager>();
        
        // Collect all managers to dispose
        foreach (var kvp in _managerCache)
        {
            managersToDispose.Add(kvp.Value);
        }
        
        // Clear the cache first
        _managerCache.Clear();
        
        // Then dispose all managers
        foreach (var manager in managersToDispose)
        {
            try
            {
                manager.Dispose();
            }
            catch (Exception ex)
            {
                // Log but don't throw - we want to dispose all managers
                Console.WriteLine($"Error disposing PluginManager: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Removes a specific PluginManager from cache and disposes it.
    /// </summary>
    /// <param name="assemblyBasePath">Assembly base path of the manager to remove</param>
    /// <returns>True if manager was found and removed, false otherwise</returns>
    public static bool RemovePluginManager(string assemblyBasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyBasePath);
        
        var normalizedPath = Path.GetFullPath(assemblyBasePath);
        
        if (_managerCache.TryRemove(normalizedPath, out var manager))
        {
            try
            {
                manager.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing PluginManager for path {normalizedPath}: {ex.Message}");
                return false;
            }
        }
        
        return false;
    }
}
