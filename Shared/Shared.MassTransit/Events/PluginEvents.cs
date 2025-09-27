namespace Shared.MassTransit.Events;

/// <summary>
/// Event published when a plugin entity is created.
/// </summary>
public class PluginCreatedEvent
{
    /// <summary>
    /// Gets or sets the unique identifier of the created plugin.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the version of the plugin.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the plugin.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the plugin.
    /// </summary>
    public string Description { get; set; } = string.Empty;



    /// <summary>
    /// Gets or sets the input schema identifier of the plugin.
    /// This references the schema used for input validation.
    /// </summary>
    public Guid InputSchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the output schema identifier of the plugin.
    /// This references the schema used for output validation.
    /// </summary>
    public Guid OutputSchemaId { get; set; } = Guid.Empty;

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
    /// Gets or sets the timestamp when the plugin was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the user who created the plugin.
    /// </summary>
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Event published when a plugin entity is updated.
/// </summary>
public class PluginUpdatedEvent
{
    /// <summary>
    /// Gets or sets the unique identifier of the updated plugin.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the version of the plugin.
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the plugin.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the plugin.
    /// </summary>
    public string Description { get; set; } = string.Empty;



    /// <summary>
    /// Gets or sets the input schema identifier of the plugin.
    /// This references the schema used for input validation.
    /// </summary>
    public Guid InputSchemaId { get; set; } = Guid.Empty;

    /// <summary>
    /// Gets or sets the output schema identifier of the plugin.
    /// This references the schema used for output validation.
    /// </summary>
    public Guid OutputSchemaId { get; set; } = Guid.Empty;

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
    /// Gets or sets the timestamp when the plugin was updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the user who updated the plugin.
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Event published when a plugin entity is deleted.
/// </summary>
public class PluginDeletedEvent
{
    /// <summary>
    /// Gets or sets the unique identifier of the deleted plugin.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the plugin was deleted.
    /// </summary>
    public DateTime DeletedAt { get; set; }

    /// <summary>
    /// Gets or sets the user who deleted the plugin.
    /// </summary>
    public string DeletedBy { get; set; } = string.Empty;
}
