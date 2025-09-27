using System.Text.Json.Serialization;

namespace Shared.Models;



/// <summary>
/// Base model for assignment entities
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(AddressAssignmentModel), "Address")]
[JsonDerivedType(typeof(DeliveryAssignmentModel), "Delivery")]
[JsonDerivedType(typeof(PluginAssignmentModel), "Plugin")]
public class AssignmentModel
{
    /// <summary>
    /// Entity ID of the assignment
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Name of the entity
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Version of the entity
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Payload data for the entity
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Schema ID for the entity
    /// </summary>
    public Guid SchemaId { get; set; }
}



/// <summary>
/// Assignment model for address entities
/// </summary>
public class AddressAssignmentModel : AssignmentModel
{
    /// <summary>
    /// Connection string for the address
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Assignment model for delivery entities
/// </summary>
public class DeliveryAssignmentModel : AssignmentModel
{
}

/// <summary>
/// Assignment model for plugin entities
/// </summary>
public class PluginAssignmentModel : AssignmentModel
{
    /// <summary>
    /// Hides the inherited Payload property - not used for plugin entities
    /// </summary>
    public new string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Hides the inherited SchemaId property - not used for plugin entities
    /// </summary>
    public new Guid SchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the input schema identifier.
    /// This references the schema used for input validation.
    /// </summary>
    public Guid InputSchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the output schema identifier.
    /// This references the schema used for output validation.
    /// </summary>
    public Guid OutputSchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the input schema definition.
    /// This contains the actual JSON schema definition for input validation at the processor side.
    /// Populated from the schema referenced by InputSchemaId for future validation implementation.
    /// </summary>
    public string InputSchemaDefinition { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the output schema definition.
    /// This contains the actual JSON schema definition for output validation at the processor side.
    /// Populated from the schema referenced by OutputSchemaId for future validation implementation.
    /// </summary>
    public string OutputSchemaDefinition { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether input validation is enabled for this plugin.
    /// When true, input data will be validated against the InputSchema before processing.
    /// </summary>
    public bool EnableInputValidation { get; set; } = false;

    /// <summary>
    /// Gets or sets whether output validation is enabled for this plugin.
    /// When true, output data will be validated against the OutputSchema after processing.
    /// </summary>
    public bool EnableOutputValidation { get; set; } = false;

    /// <summary>
    /// Gets or sets the base path where the plugin assembly is located.
    /// This is the directory path containing the plugin assembly file.
    /// </summary>
    public string AssemblyBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the plugin assembly file.
    /// This is the filename (with extension) of the assembly containing the plugin implementation.
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the plugin assembly.
    /// This represents the version of the compiled assembly file.
    /// </summary>
    public string AssemblyVersion { get; set; } = "1.0.0.0";

    /// <summary>
    /// Gets or sets the fully qualified type name of the plugin class.
    /// This is the complete type name including namespace that implements the IPlugin interface.
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the execution timeout in milliseconds for this plugin.
    /// If the plugin execution exceeds this timeout, it will be cancelled.
    /// Default value is 30000ms (30 seconds).
    /// </summary>
    public int ExecutionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets whether the plugin should be treated as stateless.
    /// When true, plugin instances are always created fresh (no caching).
    /// When false, plugin instances are cached and reused for better performance.
    /// </summary>
    public bool IsStateless { get; set; } = true;
}



