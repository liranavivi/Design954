namespace Plugin.Standardizer.Models;

/// <summary>
/// Configuration settings for standardization operations extracted from DeliveryAssignmentModel
/// </summary>
public class StandardizationConfiguration
{
    /// <summary>
    /// Type name for custom metadata standardization implementation
    /// </summary>
    public string? MetadataImplementationType { get; set; }
}
