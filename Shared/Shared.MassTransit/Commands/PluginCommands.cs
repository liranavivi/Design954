namespace Shared.MassTransit.Commands;

/// <summary>
/// Command to create a new plugin entity.
/// </summary>
public class CreatePluginCommand
{
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
    /// Gets or sets the payload of the plugin.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schema identifier of the plugin.
    /// </summary>
    public Guid SchemaId { get; set; } = Guid.Empty;

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
    /// Gets or sets the user who requested the creation.
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;
}

/// <summary>
/// Command to update an existing plugin entity.
/// </summary>
public class UpdatePluginCommand
{
    /// <summary>
    /// Gets or sets the unique identifier of the plugin to update.
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
    /// Gets or sets the payload of the plugin.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schema identifier of the plugin.
    /// </summary>
    public Guid SchemaId { get; set; } = Guid.Empty;

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
    /// Gets or sets the user who requested the update.
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;
}

/// <summary>
/// Command to delete a plugin entity.
/// </summary>
public class DeletePluginCommand
{
    /// <summary>
    /// Gets or sets the unique identifier of the plugin to delete.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user who requested the deletion.
    /// </summary>
    public string RequestedBy { get; set; } = string.Empty;
}

/// <summary>
/// Query to retrieve a plugin entity.
/// </summary>
public class GetPluginQuery
{
    /// <summary>
    /// Gets or sets the unique identifier of the plugin to retrieve.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Gets or sets the composite key of the plugin to retrieve.
    /// </summary>
    public string? CompositeKey { get; set; }
}


