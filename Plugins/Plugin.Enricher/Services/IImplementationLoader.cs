using Shared.Correlation;

namespace Plugin.Enricher.Services;

/// <summary>
/// Interface for loading custom enrichment implementations from current assembly
/// </summary>
public interface IImplementationLoader
{
    /// <summary>
    /// Load implementation type from current assembly
    /// </summary>
    /// <typeparam name="T">Interface type the implementation must implement</typeparam>
    /// <param name="typeName">Full type name to load</param>
    /// <param name="context">Hierarchical logging context</param>
    /// <returns>Instance of the implementation</returns>
    T LoadImplementation<T>(string typeName, HierarchicalLoggingContext context) where T : class;
}
