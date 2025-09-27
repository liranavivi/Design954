namespace Processor.Base.Interfaces;

/// <summary>
/// Marker interface for processor applications to enable generic logging.
/// This allows plugins to use ILogger&lt;IProcessorApplicationLogger&gt; for consistent logging context
/// while avoiding direct dependencies on specific processor application types.
/// 
/// Processor applications should implement this interface to provide a shared logging context
/// that plugins can reference without creating circular dependencies.
/// </summary>
public interface IProcessorApplicationLogger
{
    // This is a marker interface - no methods needed
    // It exists solely to provide a shared type for generic logging
}
