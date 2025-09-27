using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Shared.Interfaces;
using Processor.Base.Interfaces;
using Processor.PluginLoader.Interfaces;
using Processor.PluginLoader.Models;
using Shared.Correlation;

namespace Processor.PluginLoader.Services;

/// <summary>
/// Plugin manager for loading and managing plugin assemblies with two-level caching:
/// Level 1: PluginManager instances are cached by AssemblyBasePath (handled by PluginManagerFactory)
/// Level 2: Plugin instances are cached within PluginManager based on IsStateless flag
/// Provides isolated assembly loading contexts with dependency injection support.
/// </summary>
public class PluginManager : IDisposable
{
    private readonly string _baseAssemblyPath;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PluginManager> _logger;
    private readonly IProcessorService _processorService;
    private readonly IStatefulPluginRegistryService _registryService;

    /// <summary>
    /// Level 2 Cache: Plugin instances cached by ProcessorId:AssemblyName:Version:TypeName
    /// Only used when IsStateless = false
    /// </summary>
    private readonly ConcurrentDictionary<string, IPlugin> _pluginCache = new();

    public PluginManager(string baseAssemblyPath, IServiceProvider serviceProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAssemblyPath);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _baseAssemblyPath = baseAssemblyPath;
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<PluginManager>>();
        _processorService = serviceProvider.GetRequiredService<IProcessorService>();
        _registryService = serviceProvider.GetRequiredService<IStatefulPluginRegistryService>();

        // Note: Constructor logging will be updated when hierarchical context is available during initialization
    }

    /// <summary>
    /// Loads a versioned plugin assembly with enhanced context and dependency resolution
    /// </summary>
    /// <param name="assemblyName">Name of the assembly to load</param>
    /// <param name="version">Version of the assembly</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Loaded assembly</returns>
    public Assembly LoadPluginAssembly(string assemblyName, Version version, HierarchicalLoggingContext context)
    {
        // Create version-specific assembly path
        string versionedPath = Path.Combine(_baseAssemblyPath, $"v{version}", $"{assemblyName}.dll");

        if (!File.Exists(versionedPath))
        {
            throw new FileNotFoundException($"Plugin assembly not found: {versionedPath}");
        }

        // Create shared context for this plugin version (isolation with shared infrastructure)
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        var contextLogger = loggerFactory.CreateLogger<SharedAssemblyLoadContext>();
        var assemblyContext = new SharedAssemblyLoadContext(versionedPath, contextLogger);

        try
        {
            var assembly = assemblyContext.LoadFromAssemblyPath(versionedPath);
            _logger.LogInformationWithHierarchy(context, "✅ Loaded plugin assembly {AssemblyName} v{Version} with shared context isolation", assemblyName, version);
            return assembly;
        }
        catch (Exception ex)
        {
            assemblyContext.Unload(); // Clean up on failure
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to load plugin assembly: {AssemblyName} v{Version}", assemblyName, version);
            throw;
        }
    }

    /// <summary>
    /// Gets a plugin instance with conditional caching based on IsStateless flag.
    /// Enhanced with hierarchical logging support and stateful plugin registry integration
    /// Level 2 caching: Plugin instances cached by ProcessorId:AssemblyName:Version:TypeName when IsStateless = false
    /// </summary>
    /// <param name="assemblyName">Assembly name containing the plugin</param>
    /// <param name="version">Version of the assembly</param>
    /// <param name="typeName">Full type name of the plugin class</param>
    /// <param name="isStateless">If true, always create fresh instance; if false, cache and reuse instances</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Plugin instance (fresh or cached based on isStateless parameter)</returns>
    public async Task<IPlugin> GetPluginInstanceAsync(string assemblyName, Version version, string typeName, bool isStateless, HierarchicalLoggingContext context)
    {
        var cacheKey = await GetPluginCacheKeyAsync(assemblyName, version, typeName);

        if (isStateless)
        {
            // Stateless mode: Remove from cache if exists and create fresh instance
            if (_pluginCache.TryRemove(cacheKey, out var existingPlugin))
            {
                _logger.LogDebugWithHierarchy(context, "🗑️ Removed stateless plugin from cache: {CacheKey}", cacheKey);
                (existingPlugin as IDisposable)?.Dispose();
            }

            // Also remove from registry if it was registered as stateful
            await _registryService.UnregisterStatefulPluginAsync(cacheKey, context);

            var freshInstance = CreateFreshPluginInstance(assemblyName, version, typeName, context);
            _logger.LogDebugWithHierarchy(context, "🆕 Created fresh stateless plugin instance: {CacheKey}", cacheKey);
            return freshInstance;
        }
        else
        {
            // Stateful mode: Get from cache or create new and cache
            return _pluginCache.GetOrAdd(cacheKey, _ =>
            {
                var newInstance = CreateFreshPluginInstance(assemblyName, version, typeName, context);
                _logger.LogDebugWithHierarchy(context, "💾 Cached new stateful plugin instance: {CacheKey}", cacheKey);

                // Register in stateful plugin registry
                var processorId = _processorService.GetProcessorIdAsync().Result;
                var metadata = new StatefulPluginMetadata
                {
                    ProcessorId = processorId,
                    AssemblyName = assemblyName,
                    Version = version.ToString(),
                    TypeName = typeName,
                    AssemblyBasePath = _baseAssemblyPath,
                    RegisteredAt = DateTime.UtcNow
                };

                _registryService.RegisterStatefulPluginAsync(cacheKey, metadata, context).Wait();

                return newInstance;
            });
        }
    }

    /// <summary>
    /// Generates cache key for plugin instances including ProcessorId (async version)
    /// </summary>
    private async Task<string> GetPluginCacheKeyAsync(string assemblyName, Version version, string typeName)
    {
        var processorId = await _processorService.GetProcessorIdAsync();
        return $"{processorId}:{assemblyName}:{version}:{typeName}";
    }

    /// <summary>
    /// Generates cache key for plugin instances (synchronous version for backward compatibility)
    /// </summary>
    public IPlugin GetPluginInstance(string assemblyName, Version version, string typeName, bool isStateless, HierarchicalLoggingContext context)
    {
        return GetPluginInstanceAsync(assemblyName, version, typeName, isStateless, context).Result;
    }

    /// <summary>
    /// Gets the current count of cached plugin instances.
    /// Useful for monitoring and diagnostics.
    /// </summary>
    public int CachedPluginCount => _pluginCache.Count;

    /// <summary>
    /// Gets all cached plugin cache keys.
    /// Useful for monitoring and diagnostics.
    /// </summary>
    public IEnumerable<string> CachedPluginKeys => _pluginCache.Keys.ToList();

    /// <summary>
    /// Creates a fresh plugin instance with dependency injection
    /// </summary>
    private IPlugin CreateFreshPluginInstance(string assemblyName, Version version, string typeName, HierarchicalLoggingContext context)
    {
        // Load the assembly (this will still use existing assembly cache for performance)
        var assembly = LoadPluginAssembly(assemblyName, version, context);

        // Get the type
        var type = assembly.GetType(typeName);
        if (type == null)
        {
            throw new TypeLoadException($"Plugin type {typeName} not found in {assemblyName} v{version}");
        }

        // Validate that the type implements IPlugin
        if (!typeof(IPlugin).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Plugin type {typeName} does not implement IPlugin");
        }

        _logger.LogDebugWithHierarchy(context, "🔍 Creating fresh plugin instance: {PluginType}", type.Name);

        // Create plugin composite key for DI
        var pluginCompositeKey = $"{version}_{assemblyName}";
        _logger.LogDebugWithHierarchy(context, "🔑 Plugin composite key: {CompositeKey}", pluginCompositeKey);

        try
        {
            // Create a scope and register the plugin composite key
            using var scope = _serviceProvider.CreateScope();
            var scopedServices = scope.ServiceProvider;

            // Create the instance with dependency injection, providing the composite key as an additional parameter
            var instance = ActivatorUtilities.CreateInstance(scopedServices, type, pluginCompositeKey);
            if (instance == null)
            {
                throw new InvalidOperationException($"Failed to create instance of plugin {type.Name}. ActivatorUtilities.CreateInstance returned null.");
            }

            _logger.LogDebugWithHierarchy(context, "✅ Successfully created fresh plugin instance: {PluginType} with ActivatorUtilities and IServiceProvider injection", type.Name);

            return (IPlugin)instance;
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(context, ex, "❌ Failed to create fresh plugin instance: {PluginType}", type.Name);
            throw new InvalidOperationException($"Unable to create instance of plugin {type.Name}. Ensure the plugin implements IPlugin and has a constructor that can be resolved by dependency injection.", ex);
        }
    }

    /// <summary>
    /// Disposes all cached plugin instances and clears the cache
    /// </summary>
    public void Dispose()
    {
        // Note: Dispose logging will be updated when hierarchical context is available during disposal

        // Dispose all cached plugin instances
        foreach (var plugin in _pluginCache.Values)
        {
            try
            {
                (plugin as IDisposable)?.Dispose();
            }
            catch (Exception)
            {
                // Note: Plugin disposal error logging will be updated when hierarchical context is available
            }
        }

        // Clear the cache
        _pluginCache.Clear();

        // Note: Disposal completion logging will be updated when hierarchical context is available
    }

}

/// <summary>
/// Custom AssemblyLoadContext that provides plugin isolation while sharing critical infrastructure assemblies
/// </summary>
public class SharedAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly HashSet<string> _sharedAssemblyNames;
    private readonly ILogger<SharedAssemblyLoadContext> _logger;

    public SharedAssemblyLoadContext(string pluginPath, ILogger<SharedAssemblyLoadContext> logger)
        : base($"Plugin_{Path.GetFileNameWithoutExtension(pluginPath)}", isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
        _logger = logger;

        // Define which assemblies should be shared with host (same type identities)
        _sharedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // .NET Core assemblies
            "System.Runtime",
            "System.Collections",
            "System.Threading",
            "System.Threading.Tasks",
            "netstandard",

            // Microsoft Extensions (DI infrastructure)
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Options",
            "Microsoft.Extensions.Configuration",
            "Microsoft.Extensions.Configuration.Abstractions",
            "Microsoft.Extensions.Hosting",
            "Microsoft.Extensions.Hosting.Abstractions",

            // MassTransit (messaging infrastructure)
            "MassTransit",
            "MassTransit.Abstractions",

            // Your shared assemblies
            "Shared.Correlation",
            "Shared.Models",
            "Processor.Base",
            "Shared.MassTransit",
            "Shared.Configuration"
        };
    }

}
