using Manager.Plugin.Repositories;
using Manager.Plugin.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Correlation;
using Shared.Entities;
using Shared.Exceptions;
using Swashbuckle.AspNetCore.Annotations;

namespace Manager.Plugin.Controllers;

[ApiController]
[Route("api/[controller]")]
[SwaggerTag("Plugin management operations")]
public class PluginController : ControllerBase
{
    private readonly IPluginEntityRepository _repository;
    private readonly IEntityReferenceValidator _entityReferenceValidator;
    private readonly ISchemaValidationService _schemaValidator;
    private readonly ILogger<PluginController> _logger;
    private readonly ICorrelationIdContext _correlationIdContext;

    public PluginController(
        IPluginEntityRepository repository,
        IEntityReferenceValidator entityReferenceValidator,
        ISchemaValidationService schemaValidator,
        ILogger<PluginController> logger,
        ICorrelationIdContext correlationIdContext)
    {
        _repository = repository;
        _entityReferenceValidator = entityReferenceValidator;
        _schemaValidator = schemaValidator;
        _logger = logger;
        _correlationIdContext = correlationIdContext;
    }

    /// <summary>
    /// Creates a hierarchical logging context for operations with a Plugin entity
    /// </summary>
    private HierarchicalLoggingContext CreateHierarchicalContext(PluginEntity entity)
    {
        return new HierarchicalLoggingContext
        {
            CorrelationId = _correlationIdContext.Current
        };
    }

    /// <summary>
    /// Creates a hierarchical logging context for operations without a specific entity (e.g., GetAll)
    /// </summary>
    private HierarchicalLoggingContext CreateHierarchicalContext()
    {
        return new HierarchicalLoggingContext
        {
            CorrelationId = _correlationIdContext.Current
        };
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PluginEntity>>> GetAll()
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformationWithCorrelation("Starting GetAll schemas request. User: {User}, RequestId: {RequestId}",
            userContext, HttpContext.TraceIdentifier);

        try
        {
            var entities = await _repository.GetAllAsync();
            var count = entities.Count();

            _logger.LogInformationWithCorrelation("Successfully retrieved {Count} Plugin entities. User: {User}, RequestId: {RequestId}",
                count, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error retrieving all Plugin entities. User: {User}, RequestId: {RequestId}",
                userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving Plugin entities");
        }
    }

    [HttpGet("paged")]
    public async Task<ActionResult<object>> GetPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        var originalPage = page;
        var originalPageSize = pageSize;

        _logger.LogInformationWithCorrelation("Starting GetPaged schemas request. Page: {Page}, PageSize: {PageSize}, User: {User}, RequestId: {RequestId}",
            page, pageSize, userContext, HttpContext.TraceIdentifier);

        try
        {
            // Strict validation - return 400 for invalid parameters instead of auto-correcting
            if (page < 1)
            {
                _logger.LogWarningWithCorrelation("Invalid page parameter {Page} provided. User: {User}, RequestId: {RequestId}",
                    originalPage, userContext, HttpContext.TraceIdentifier);
                return BadRequest(new
                {
                    error = "Invalid page parameter",
                    message = "Page must be greater than 0",
                    parameter = "page",
                    value = originalPage
                });
            }

            if (pageSize < 1)
            {
                _logger.LogWarningWithCorrelation("Invalid pageSize parameter {PageSize} provided. User: {User}, RequestId: {RequestId}",
                    originalPageSize, userContext, HttpContext.TraceIdentifier);
                return BadRequest(new
                {
                    error = "Invalid pageSize parameter",
                    message = "PageSize must be greater than 0",
                    parameter = "pageSize",
                    value = originalPageSize
                });
            }
            else if (pageSize > 100)
            {
                _logger.LogWarningWithCorrelation("PageSize parameter {PageSize} exceeds maximum. User: {User}, RequestId: {RequestId}",
                    originalPageSize, userContext, HttpContext.TraceIdentifier);
                return BadRequest(new
                {
                    error = "Invalid pageSize parameter",
                    message = "PageSize cannot exceed 100",
                    parameter = "pageSize",
                    value = originalPageSize,
                    maximum = 100
                });
            }

            var entities = await _repository.GetPagedAsync(page, pageSize);
            var totalCount = await _repository.CountAsync();
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var result = new
            {
                data = entities,
                page = page,
                pageSize = pageSize,
                totalCount = totalCount,
                totalPages = totalPages
            };

            _logger.LogInformationWithCorrelation("Successfully retrieved paged Plugin entities. Page: {Page}/{TotalPages}, PageSize: {PageSize}, TotalCount: {TotalCount}, User: {User}, RequestId: {RequestId}",
                page, totalPages, pageSize, totalCount, userContext, HttpContext.TraceIdentifier);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error retrieving paged Plugin entities. Page: {Page}, PageSize: {PageSize}, User: {User}, RequestId: {RequestId}",
                originalPage, originalPageSize, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving paged Plugin entities");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PluginEntity>> GetById(string id)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";

        // Validate GUID format
        if (!Guid.TryParse(id, out Guid guidId))
        {
            _logger.LogWarningWithCorrelation("Invalid GUID format provided. Id: {Id}, User: {User}, RequestId: {RequestId}",
                id, userContext, HttpContext.TraceIdentifier);
            return BadRequest($"Invalid GUID format: {id}");
        }

        // Create basic hierarchical context for request start (Layer 1 - only CorrelationId)
        var requestContext = CreateHierarchicalContext();

        _logger.LogInformationWithHierarchy(requestContext,
            "Starting GetById Plugin request. Id: {Id}, User: {User}, RequestId: {RequestId}",
            guidId, userContext, HttpContext.TraceIdentifier);

        try
        {
            var entity = await _repository.GetByIdAsync(guidId);

            if (entity == null)
            {
                _logger.LogWarningWithHierarchy(requestContext, "Plugin entity not found. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return NotFound($"Plugin entity with ID {guidId} not found");
            }

            // Create hierarchical context for structured logging
            var context = CreateHierarchicalContext(entity);

            _logger.LogInformationWithHierarchy(context,
                "Successfully retrieved Plugin entity. Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                entity.Version, entity.Name, userContext, HttpContext.TraceIdentifier);

            return Ok(entity);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithHierarchy(requestContext, ex, "Error retrieving Plugin entity by ID. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving the Plugin entity");
        }
    }

    [HttpGet("composite/{version}/{name}")]
    public async Task<ActionResult<PluginEntity>> GetByCompositeKey(string version, string name)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        // PluginEntity composite key format: "version_name"
        var compositeKey = $"{version}_{name}";

        _logger.LogInformationWithCorrelation("Starting GetByCompositeKey Plugin request. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
            version, name, compositeKey, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarningWithCorrelation("Empty or null version/name provided. Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                    version, name, userContext, HttpContext.TraceIdentifier);
                return BadRequest("Version and name cannot be empty");
            }

            var entity = await _repository.GetByCompositeKeyAsync(compositeKey);

            if (entity == null)
            {
                _logger.LogWarningWithCorrelation("Plugin entity not found by composite key. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                    version, name, compositeKey, userContext, HttpContext.TraceIdentifier);
                return NotFound($"Plugin entity with version '{version}' and name '{name}' not found");
            }

            _logger.LogInformationWithCorrelation("Successfully retrieved Plugin entity by composite key. CompositeKey: {CompositeKey}, Id: {Id}, Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                compositeKey, entity.Id, entity.Version, entity.Name, userContext, HttpContext.TraceIdentifier);

            return Ok(entity);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error retrieving Plugin entity by composite key. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                version, name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving the Plugin entity");
        }
    }



    [HttpGet("version/{version}")]
    public async Task<ActionResult<IEnumerable<PluginEntity>>> GetByVersion(string version)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformationWithCorrelation("Starting GetByVersion Plugin request. Version: {Version}, User: {User}, RequestId: {RequestId}",
            version, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                _logger.LogWarningWithCorrelation("Empty or null version provided. User: {User}, RequestId: {RequestId}",
                    userContext, HttpContext.TraceIdentifier);
                return BadRequest("Version cannot be empty");
            }

            var entities = await _repository.GetByVersionAsync(version);
            var count = entities.Count();

            _logger.LogInformationWithCorrelation("Successfully retrieved {Count} Plugin entities by version. Version: {Version}, User: {User}, RequestId: {RequestId}",
                count, version, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error retrieving Plugin entities by version. Version: {Version}, User: {User}, RequestId: {RequestId}",
                version, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving Plugin entities");
        }
    }

    [HttpGet("name/{name}")]
    public async Task<ActionResult<IEnumerable<PluginEntity>>> GetByName(string name)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        _logger.LogInformationWithCorrelation("Starting GetByName Plugin request. Name: {Name}, User: {User}, RequestId: {RequestId}",
            name, userContext, HttpContext.TraceIdentifier);

        try
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                _logger.LogWarningWithCorrelation("Empty or null name provided. User: {User}, RequestId: {RequestId}",
                    userContext, HttpContext.TraceIdentifier);
                return BadRequest("Name cannot be empty");
            }

            var entities = await _repository.GetByNameAsync(name);
            var count = entities.Count();

            _logger.LogInformationWithCorrelation("Successfully retrieved {Count} Plugin entities by name. Name: {Name}, User: {User}, RequestId: {RequestId}",
                count, name, userContext, HttpContext.TraceIdentifier);

            return Ok(entities);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error retrieving Plugin entities by name. Name: {Name}, User: {User}, RequestId: {RequestId}",
                name, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while retrieving Plugin entities");
        }
    }

    [HttpPost]
    public async Task<ActionResult<PluginEntity>> Create([FromBody] PluginEntity entity)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        var compositeKey = entity?.GetCompositeKey() ?? "Unknown";

        _logger.LogInformationWithCorrelation("Starting Create Plugin request. CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
            compositeKey, userContext, HttpContext.TraceIdentifier);

        if (entity == null)
        {
            _logger.LogWarningWithCorrelation("Null entity provided for Plugin creation. User: {User}, RequestId: {RequestId}",
                userContext, HttpContext.TraceIdentifier);
            return BadRequest("Plugin entity cannot be null");
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarningWithCorrelation("Invalid model state for Plugin creation. Errors: {Errors}, User: {User}, RequestId: {RequestId}",
                errorMessage, userContext, HttpContext.TraceIdentifier);
            return BadRequest(ModelState);
        }

        try
        {
            entity!.CreatedBy = userContext;
            entity.Id = Guid.Empty;

            // Validate all schema references before creating the entity
            _logger.LogDebugWithCorrelation("Validating schema references before creating Plugin entity. InputSchemaId: {InputSchemaId}, OutputSchemaId: {OutputSchemaId}, User: {User}, RequestId: {RequestId}",
                entity.InputSchemaId, entity.OutputSchemaId, userContext, HttpContext.TraceIdentifier);

            // InputSchemaId and OutputSchemaId are optional for Plugin entities
            await _schemaValidator.ValidateSchemaExists(entity.InputSchemaId);
            await _schemaValidator.ValidateSchemaExists(entity.OutputSchemaId);

            _logger.LogDebugWithCorrelation("Schema validation passed. Creating Plugin entity with details. Version: {Version}, Name: {Name}, AssemblyName: {AssemblyName}, AssemblyVersion: {AssemblyVersion}, TypeName: {TypeName}, InputSchemaId: {InputSchemaId}, OutputSchemaId: {OutputSchemaId}, ExecutionTimeoutMs: {ExecutionTimeoutMs}, User: {User}, RequestId: {RequestId}",
                entity.Version, entity.Name, entity.AssemblyName, entity.AssemblyVersion, entity.TypeName, entity.InputSchemaId, entity.OutputSchemaId, entity.ExecutionTimeoutMs, userContext, HttpContext.TraceIdentifier);

            var created = await _repository.CreateAsync(entity);

            if (created.Id == Guid.Empty)
            {
                _logger.LogErrorWithCorrelation("MongoDB failed to generate ID for new PluginEntity. Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                    entity.Version, entity.Name, userContext, HttpContext.TraceIdentifier);
                return StatusCode(500, "Failed to generate entity ID");
            }

            _logger.LogInformationWithCorrelation("Successfully created Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                created.Id, created.Version, created.Name, created.GetCompositeKey(), userContext, HttpContext.TraceIdentifier);

            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Schema") || ex.Message.Contains("schema"))
        {
            _logger.LogWarningWithCorrelation(ex, "Schema validation failed creating Plugin entity. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return BadRequest(new {
                error = "Schema validation failed",
                message = ex.Message
            });
        }
        catch (DuplicateKeyException ex)
        {
            _logger.LogWarningWithCorrelation(ex, "Duplicate key conflict creating Plugin entity. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error creating Plugin entity. Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while creating the Plugin entity");
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<PluginEntity>> Update(string id, [FromBody] PluginEntity entity)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";
        var compositeKey = entity?.GetCompositeKey() ?? "Unknown";

        // Validate GUID format
        if (!Guid.TryParse(id, out Guid guidId))
        {
            _logger.LogWarningWithCorrelation("Invalid GUID format provided for update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                id, userContext, HttpContext.TraceIdentifier);
            return BadRequest($"Invalid GUID format: {id}");
        }

        _logger.LogInformationWithCorrelation("Starting Update Plugin request. Id: {Id}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
            guidId, compositeKey, userContext, HttpContext.TraceIdentifier);

        if (entity == null)
        {
            _logger.LogWarningWithCorrelation("Null entity provided for Plugin update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);
            return BadRequest("Plugin entity cannot be null");
        }

        if (guidId != entity.Id)
        {
            _logger.LogWarningWithCorrelation("ID mismatch in Plugin update request. URL ID: {UrlId}, Entity ID: {EntityId}, User: {User}, RequestId: {RequestId}",
                guidId, entity.Id, userContext, HttpContext.TraceIdentifier);
            return BadRequest("ID in URL does not match entity ID");
        }

        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarningWithCorrelation("Invalid model state for Plugin update. Id: {Id}, Errors: {Errors}, User: {User}, RequestId: {RequestId}",
                guidId, errorMessage, userContext, HttpContext.TraceIdentifier);
            return BadRequest(ModelState);
        }

        try
        {
            var existingEntity = await _repository.GetByIdAsync(guidId);
            if (existingEntity == null)
            {
                _logger.LogWarningWithCorrelation("Plugin entity not found for update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return NotFound($"Plugin entity with ID {guidId} not found");
            }

            // Validate entity references before updating (check for references in Assignment entities)
            _logger.LogDebugWithCorrelation("Validating entity references before updating Plugin entity. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);

            try
            {
                await _entityReferenceValidator.ValidateEntityCanBeDeleted(guidId);
                _logger.LogDebugWithCorrelation("Entity reference validation passed for Plugin entity update. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarningWithCorrelation("Plugin entity update blocked due to references or validation failure. Id: {Id}, Message: {Message}, User: {User}, RequestId: {RequestId}",
                    guidId, ex.Message, userContext, HttpContext.TraceIdentifier);
                return Conflict($"Cannot update Plugin entity: {ex.Message}");
            }

            // Validate all schema references before updating the entity
            _logger.LogDebugWithCorrelation("Validating schema references before updating Plugin entity. InputSchemaId: {InputSchemaId}, OutputSchemaId: {OutputSchemaId}, User: {User}, RequestId: {RequestId}",
                entity.InputSchemaId, entity.OutputSchemaId, userContext, HttpContext.TraceIdentifier);

            // InputSchemaId and OutputSchemaId are optional for Plugin entities
            await _schemaValidator.ValidateSchemaExists(entity.InputSchemaId);
            await _schemaValidator.ValidateSchemaExists(entity.OutputSchemaId);

            entity.UpdatedBy = userContext;
            entity.CreatedAt = existingEntity.CreatedAt;
            entity.CreatedBy = existingEntity.CreatedBy;

            _logger.LogDebugWithCorrelation("Schema validation passed. Updating Plugin entity with details. Id: {Id}, Version: {Version}, Name: {Name}, AssemblyName: {AssemblyName}, AssemblyVersion: {AssemblyVersion}, TypeName: {TypeName}, InputSchemaId: {InputSchemaId}, OutputSchemaId: {OutputSchemaId}, ExecutionTimeoutMs: {ExecutionTimeoutMs}, User: {User}, RequestId: {RequestId}",
                entity.Id, entity.Version, entity.Name, entity.AssemblyName, entity.AssemblyVersion, entity.TypeName, entity.InputSchemaId, entity.OutputSchemaId, entity.ExecutionTimeoutMs, userContext, HttpContext.TraceIdentifier);

            var updated = await _repository.UpdateAsync(entity);

            _logger.LogInformationWithCorrelation("Successfully updated Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                updated.Id, updated.Version, updated.Name, updated.GetCompositeKey(), userContext, HttpContext.TraceIdentifier);

            return Ok(updated);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Schema") || ex.Message.Contains("schema"))
        {
            _logger.LogWarningWithCorrelation(ex, "Schema validation failed updating Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                guidId, entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return BadRequest(new {
                error = "Schema validation failed",
                message = ex.Message,

            });
        }
        catch (DuplicateKeyException ex)
        {
            _logger.LogWarningWithCorrelation(ex, "Duplicate key conflict updating Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                guidId, entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error updating Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, CompositeKey: {CompositeKey}, User: {User}, RequestId: {RequestId}",
                guidId, entity?.Version, entity?.Name, compositeKey, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while updating the Plugin entity");
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id)
    {
        var userContext = User.Identity?.Name ?? "Anonymous";

        // Validate GUID format
        if (!Guid.TryParse(id, out Guid guidId))
        {
            _logger.LogWarningWithCorrelation("Invalid GUID format provided for delete. Id: {Id}, User: {User}, RequestId: {RequestId}",
                id, userContext, HttpContext.TraceIdentifier);
            return BadRequest($"Invalid GUID format: {id}");
        }

        _logger.LogInformationWithCorrelation("Starting Delete Plugin request. Id: {Id}, User: {User}, RequestId: {RequestId}",
            guidId, userContext, HttpContext.TraceIdentifier);

        try
        {
            var existingEntity = await _repository.GetByIdAsync(guidId);
            if (existingEntity == null)
            {
                _logger.LogWarningWithCorrelation("Plugin entity not found for deletion. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return NotFound($"Plugin entity with ID {guidId} not found");
            }

            // Validate entity can be deleted (check for references in Assignment entities)
            try
            {
                await _entityReferenceValidator.ValidateEntityCanBeDeleted(guidId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarningWithCorrelation("Plugin entity deletion blocked due to references or validation failure. Id: {Id}, Message: {Message}, User: {User}, RequestId: {RequestId}",
                    guidId, ex.Message, userContext, HttpContext.TraceIdentifier);
                return Conflict($"Cannot delete Plugin entity: {ex.Message}");
            }

            _logger.LogDebugWithCorrelation("Deleting Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                guidId, existingEntity.Version, existingEntity.Name, userContext, HttpContext.TraceIdentifier);

            var success = await _repository.DeleteAsync(guidId);

            if (!success)
            {
                _logger.LogErrorWithCorrelation("Failed to delete Plugin entity. Id: {Id}, User: {User}, RequestId: {RequestId}",
                    guidId, userContext, HttpContext.TraceIdentifier);
                return StatusCode(500, "Failed to delete Plugin entity");
            }

            _logger.LogInformationWithCorrelation("Successfully deleted Plugin entity. Id: {Id}, Version: {Version}, Name: {Name}, User: {User}, RequestId: {RequestId}",
                guidId, existingEntity.Version, existingEntity.Name, userContext, HttpContext.TraceIdentifier);

            return NoContent();
        }
        
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error deleting Plugin entity. Id: {Id}, User: {User}, RequestId: {RequestId}",
                guidId, userContext, HttpContext.TraceIdentifier);
            return StatusCode(500, "An error occurred while deleting the Plugin entity");
        }
    }

    [HttpGet("composite")]
    public ActionResult<PluginEntity> GetByCompositeKeyEmpty()
    {
        var userContext = User.Identity?.Name ?? "Anonymous";

        _logger.LogWarningWithCorrelation("Empty composite key parameters in GetByCompositeKey request (no parameters provided). User: {User}, RequestId: {RequestId}",
            userContext, HttpContext.TraceIdentifier);

        return BadRequest(new {
            error = "Invalid composite key parameters",
            message = "Both version and name parameters are required",
            parameters = new[] { "version", "name" }
        });
    }



    /// <summary>
    /// Check if any plugin entities reference the specified input schema ID
    /// </summary>
    /// <param name="inputSchemaId">The input schema ID to check</param>
    /// <returns>True if input schema is referenced, false otherwise</returns>
    [HttpGet("inputSchema/{inputSchemaId}/exists")]
    public async Task<ActionResult<bool>> CheckInputSchemaReference(Guid inputSchemaId)
    {
        var requestId = HttpContext.TraceIdentifier;
        var user = HttpContext.User?.Identity?.Name ?? "Anonymous";

        _logger.LogInformationWithCorrelation("Starting input schema reference check. InputSchemaId: {InputSchemaId}, User: {User}, RequestId: {RequestId}",
            inputSchemaId, user, requestId);

        try
        {
            if (inputSchemaId == Guid.Empty)
            {
                _logger.LogWarningWithCorrelation("Invalid input schema ID provided for reference check. InputSchemaId: {InputSchemaId}, User: {User}, RequestId: {RequestId}",
                    inputSchemaId, user, requestId);
                return BadRequest("Input Schema ID cannot be empty");
            }

            var hasReferences = await _repository.HasInputSchemaReferences(inputSchemaId);

            _logger.LogInformationWithCorrelation("Input schema reference check completed. InputSchemaId: {InputSchemaId}, HasReferences: {HasReferences}, User: {User}, RequestId: {RequestId}",
                inputSchemaId, hasReferences, user, requestId);

            return Ok(hasReferences);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error checking input schema references. InputSchemaId: {InputSchemaId}, User: {User}, RequestId: {RequestId}",
                inputSchemaId, user, requestId);
            return StatusCode(500, "Internal server error while checking input schema references");
        }
    }

    /// <summary>
    /// Check if any plugin entities reference the specified output schema ID
    /// </summary>
    /// <param name="outputSchemaId">The output schema ID to check</param>
    /// <returns>True if output schema is referenced, false otherwise</returns>
    [HttpGet("outputSchema/{outputSchemaId}/exists")]
    public async Task<ActionResult<bool>> CheckOutputSchemaReference(Guid outputSchemaId)
    {
        var requestId = HttpContext.TraceIdentifier;
        var user = HttpContext.User?.Identity?.Name ?? "Anonymous";

        _logger.LogInformationWithCorrelation("Starting output schema reference check. OutputSchemaId: {OutputSchemaId}, User: {User}, RequestId: {RequestId}",
            outputSchemaId, user, requestId);

        try
        {
            if (outputSchemaId == Guid.Empty)
            {
                _logger.LogWarningWithCorrelation("Invalid output schema ID provided for reference check. OutputSchemaId: {OutputSchemaId}, User: {User}, RequestId: {RequestId}",
                    outputSchemaId, user, requestId);
                return BadRequest("Output Schema ID cannot be empty");
            }

            var hasReferences = await _repository.HasOutputSchemaReferences(outputSchemaId);

            _logger.LogInformationWithCorrelation("Output schema reference check completed. OutputSchemaId: {OutputSchemaId}, HasReferences: {HasReferences}, User: {User}, RequestId: {RequestId}",
                outputSchemaId, hasReferences, user, requestId);

            return Ok(hasReferences);
        }
        catch (Exception ex)
        {
            _logger.LogErrorWithCorrelation(ex, "Error checking output schema references. OutputSchemaId: {OutputSchemaId}, User: {User}, RequestId: {RequestId}",
                outputSchemaId, user, requestId);
            return StatusCode(500, "Internal server error while checking output schema references");
        }
    }
}
