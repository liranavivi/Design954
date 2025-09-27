using MongoDB.Driver;
using Shared.Entities;
using Shared.MassTransit.Events;
using Shared.Repositories.Base;
using Shared.Services.Interfaces;

namespace Manager.Plugin.Repositories;

public class PluginEntityRepository : BaseRepository<PluginEntity>, IPluginEntityRepository
{
    public PluginEntityRepository(
        IMongoDatabase database,
        ILogger<BaseRepository<PluginEntity>> logger,
        IEventPublisher eventPublisher,
        IManagerMetricsService metricsService)
        : base(database, "Plugin", logger, eventPublisher, metricsService)
    {
    }

    public async Task<IEnumerable<PluginEntity>> GetByVersionAsync(string version)
    {
        var filter = Builders<PluginEntity>.Filter.Eq(x => x.Version, version);
        return await _collection.Find(filter).ToListAsync();
    }

    public async Task<IEnumerable<PluginEntity>> GetByNameAsync(string name)
    {
        var filter = Builders<PluginEntity>.Filter.Eq(x => x.Name, name);
        return await _collection.Find(filter).ToListAsync();
    }



    protected override void CreateIndexes()
    {
        // Call base implementation if it exists, but since it's abstract, we implement it here

        // Create compound index for composite key (version + name)
        var compositeKeyIndex = Builders<PluginEntity>.IndexKeys
            .Ascending(x => x.Version)
            .Ascending(x => x.Name);
        
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            compositeKeyIndex, 
            new CreateIndexOptions { Unique = true, Name = "idx_version_name_unique" }));

        // Create individual indexes for search operations
        var versionIndex = Builders<PluginEntity>.IndexKeys.Ascending(x => x.Version);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            versionIndex, 
            new CreateIndexOptions { Name = "idx_version" }));

        var nameIndex = Builders<PluginEntity>.IndexKeys.Ascending(x => x.Name);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            nameIndex, 
            new CreateIndexOptions { Name = "idx_name" }));



        // Add new indexes for plugin-specific properties
        var inputSchemaIdIndex = Builders<PluginEntity>.IndexKeys.Ascending(x => x.InputSchemaId);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            inputSchemaIdIndex,
            new CreateIndexOptions { Name = "idx_inputSchemaId" }));

        var outputSchemaIdIndex = Builders<PluginEntity>.IndexKeys.Ascending(x => x.OutputSchemaId);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            outputSchemaIdIndex,
            new CreateIndexOptions { Name = "idx_outputSchemaId" }));

        var assemblyNameIndex = Builders<PluginEntity>.IndexKeys.Ascending(x => x.AssemblyName);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            assemblyNameIndex,
            new CreateIndexOptions { Name = "idx_assemblyName" }));

        var typeNameIndex = Builders<PluginEntity>.IndexKeys.Ascending(x => x.TypeName);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            typeNameIndex,
            new CreateIndexOptions { Name = "idx_typeName" }));

        // Compound index for validation settings
        var validationIndex = Builders<PluginEntity>.IndexKeys
            .Ascending(x => x.EnableInputValidation)
            .Ascending(x => x.EnableOutputValidation);
        _collection.Indexes.CreateOne(new CreateIndexModel<PluginEntity>(
            validationIndex,
            new CreateIndexOptions { Name = "idx_validation_settings" }));
    }

    protected override FilterDefinition<PluginEntity> CreateCompositeKeyFilter(string compositeKey)
    {
        // PluginEntity composite key format: "version_name"
        var parts = compositeKey.Split('_', 2);
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid composite key format: {compositeKey}. Expected format: 'version_name'");
        }

        var version = parts[0];
        var name = parts[1];

        return Builders<PluginEntity>.Filter.And(
            Builders<PluginEntity>.Filter.Eq(x => x.Version, version),
            Builders<PluginEntity>.Filter.Eq(x => x.Name, name)
        );
    }

    protected override async Task PublishCreatedEventAsync(PluginEntity entity)
    {
        var createdEvent = new PluginCreatedEvent
        {
            Id = entity.Id,
            Version = entity.Version,
            Name = entity.Name,
            Description = entity.Description,
            InputSchemaId = entity.InputSchemaId,
            OutputSchemaId = entity.OutputSchemaId,
            EnableInputValidation = entity.EnableInputValidation,
            EnableOutputValidation = entity.EnableOutputValidation,
            AssemblyBasePath = entity.AssemblyBasePath,
            AssemblyName = entity.AssemblyName,
            AssemblyVersion = entity.AssemblyVersion,
            TypeName = entity.TypeName,
            ExecutionTimeoutMs = entity.ExecutionTimeoutMs,
            CreatedAt = entity.CreatedAt,
            CreatedBy = entity.CreatedBy
        };

        await _eventPublisher.PublishAsync(createdEvent);
    }

    protected override async Task PublishUpdatedEventAsync(PluginEntity entity)
    {
        var updatedEvent = new PluginUpdatedEvent
        {
            Id = entity.Id,
            Version = entity.Version,
            Name = entity.Name,
            Description = entity.Description,
            InputSchemaId = entity.InputSchemaId,
            OutputSchemaId = entity.OutputSchemaId,
            EnableInputValidation = entity.EnableInputValidation,
            EnableOutputValidation = entity.EnableOutputValidation,
            AssemblyBasePath = entity.AssemblyBasePath,
            AssemblyName = entity.AssemblyName,
            AssemblyVersion = entity.AssemblyVersion,
            TypeName = entity.TypeName,
            ExecutionTimeoutMs = entity.ExecutionTimeoutMs,
            UpdatedAt = entity.UpdatedAt,
            UpdatedBy = entity.UpdatedBy
        };

        await _eventPublisher.PublishAsync(updatedEvent);
    }

    protected override async Task PublishDeletedEventAsync(Guid id, string deletedBy)
    {
        var deletedEvent = new PluginDeletedEvent
        {
            Id = id,
            DeletedAt = DateTime.UtcNow,
            DeletedBy = deletedBy
        };

        await _eventPublisher.PublishAsync(deletedEvent);
    }



    public async Task<bool> HasInputSchemaReferences(Guid inputSchemaId)
    {
        var filter = Builders<PluginEntity>.Filter.Eq(x => x.InputSchemaId, inputSchemaId);
        var count = await _collection.CountDocumentsAsync(filter);
        return count > 0;
    }

    public async Task<bool> HasOutputSchemaReferences(Guid outputSchemaId)
    {
        var filter = Builders<PluginEntity>.Filter.Eq(x => x.OutputSchemaId, outputSchemaId);
        var count = await _collection.CountDocumentsAsync(filter);
        return count > 0;
    }
}
