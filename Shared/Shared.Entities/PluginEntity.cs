using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Entities.Base;

namespace Shared.Entities;

/// <summary>
/// Represents a plugin entity in the system.
/// Contains plugin information including version, name, schema validation settings, assembly details, and execution configuration.
/// Inherits from BaseEntity to provide core entity functionality without delivery-specific properties.
/// </summary>
public class PluginEntity : BaseEntity
{
    /// <summary>
    /// Gets or sets the input schema identifier.
    /// This references the schema used for input validation (optional).
    /// </summary>
    [BsonElement("inputSchemaId")]
    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public Guid InputSchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the output schema identifier.
    /// This references the schema used for output validation (optional).
    /// </summary>
    [BsonElement("outputSchemaId")]
    [BsonRepresentation(MongoDB.Bson.BsonType.String)]
    public Guid OutputSchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets whether input validation is enabled for this plugin.
    /// When true, input data will be validated against the InputSchema before processing.
    /// </summary>
    [BsonElement("enableInputValidation")]
    public bool EnableInputValidation { get; set; } = false;

    /// <summary>
    /// Gets or sets whether output validation is enabled for this plugin.
    /// When true, output data will be validated against the OutputSchema after processing.
    /// </summary>
    [BsonElement("enableOutputValidation")]
    public bool EnableOutputValidation { get; set; } = false;

    /// <summary>
    /// Gets or sets the base path where the plugin assembly is located.
    /// This is the directory path containing the plugin assembly file.
    /// </summary>
    [BsonElement("assemblyBasePath")]
    [Required(ErrorMessage = "AssemblyBasePath is required")]
    [StringLength(500, ErrorMessage = "AssemblyBasePath cannot exceed 500 characters")]
    public string AssemblyBasePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the plugin assembly file.
    /// This is the filename (with extension) of the assembly containing the plugin implementation.
    /// </summary>
    [BsonElement("assemblyName")]
    [Required(ErrorMessage = "AssemblyName is required")]
    [StringLength(255, ErrorMessage = "AssemblyName cannot exceed 255 characters")]
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the version of the plugin assembly.
    /// This represents the version of the compiled assembly file.
    /// Must follow the format d.d.d.d where d is a digit (e.g., 1.0.0.0, 2.1.3.4).
    /// </summary>
    [BsonElement("assemblyVersion")]
    [Required(ErrorMessage = "AssemblyVersion is required")]
    [StringLength(50, ErrorMessage = "AssemblyVersion cannot exceed 50 characters")]
    [RegularExpression(@"^\d+\.\d+\.\d+\.\d+$", ErrorMessage = "AssemblyVersion must follow the format d.d.d.d where d is a digit (e.g., 1.0.0.0, 2.1.3.4)")]
    public string AssemblyVersion { get; set; } = "1.0.0.0";

    /// <summary>
    /// Gets or sets the fully qualified type name of the plugin class.
    /// This is the complete type name including namespace that implements the IPlugin interface.
    /// </summary>
    [BsonElement("typeName")]
    [Required(ErrorMessage = "TypeName is required")]
    [StringLength(500, ErrorMessage = "TypeName cannot exceed 500 characters")]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the execution timeout in milliseconds for this plugin.
    /// If the plugin execution exceeds this timeout, it will be cancelled.
    /// Default value is 30000ms (30 seconds).
    /// </summary>
    [BsonElement("executionTimeoutMs")]
    [Range(1000, int.MaxValue, ErrorMessage = "ExecutionTimeoutMs must be at least 1000ms (1 second)")]
    public int ExecutionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets whether the plugin should be treated as stateless.
    /// When true, plugin instances are always created fresh (no caching).
    /// When false, plugin instances are cached and reused for better performance.
    /// Default value is true for safety and predictability.
    /// </summary>
    [BsonElement("isStateless")]
    public bool IsStateless { get; set; } = true;
}
