namespace Manager.Plugin.Services;

/// <summary>
/// Interface for validating schema references during entity operations
/// </summary>
public interface ISchemaValidationService
{
    /// <summary>
    /// Validates that a schema exists before creating or updating an entity
    /// </summary>
    /// <param name="schemaId">The schema ID to validate</param>
    /// <returns>Task that completes successfully if schema exists</returns>
    /// <exception cref="InvalidOperationException">Thrown if schema doesn't exist or validation fails</exception>
    Task ValidateSchemaExists(Guid schemaId);

    /// <summary>
    /// Validates that a required schema exists (throws exception if empty)
    /// </summary>
    /// <param name="schemaId">The schema ID to validate</param>
    /// <returns>Task that completes successfully if schema exists</returns>
    /// <exception cref="InvalidOperationException">Thrown if schema doesn't exist, is empty, or validation fails</exception>
    Task ValidateRequiredSchemaExists(Guid schemaId);
}
