using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Birko.Data.Migrations.CosmosDB.Settings;

namespace Birko.Data.Migrations.CosmosDB;

/// <summary>
/// Stores migration state in a Cosmos DB container. Container name, state document id,
/// and partition key are configurable via <see cref="CosmosMigrationSettings"/> so multiple
/// modules can share one Cosmos database without colliding on the state document.
/// </summary>
public class CosmosMigrationStore : Data.Migrations.IMigrationStore
{
    private readonly Database _database;
    private readonly CosmosMigrationSettings _settings;
    private Container? _container;
    private MigrationsStateDocument? _cachedState;

    /// <summary>
    /// Initializes a new instance of the CosmosMigrationStore class.
    /// </summary>
    public CosmosMigrationStore(Database database, CosmosMigrationSettings? settings = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _settings = settings ?? new CosmosMigrationSettings();
    }

    /// <summary>
    /// Initializes the migration store (creates container and state document if needed).
    /// </summary>
    public void Initialize()
    {
        _container = _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_settings.MigrationsContainerName, "/partitionKey")
        ).GetAwaiter().GetResult().Container;

        try
        {
            var response = _container.ReadItemAsync<MigrationsStateDocument>(
                _settings.MigrationsDocumentId, new PartitionKey(_settings.MigrationsPartitionKey)
            ).GetAwaiter().GetResult();
            _cachedState = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _cachedState = new MigrationsStateDocument
            {
                Id = _settings.MigrationsDocumentId,
                PartitionKey = _settings.MigrationsPartitionKey,
                AppliedMigrations = new Dictionary<string, MigrationRecord>()
            };
            _container.CreateItemAsync(_cachedState, new PartitionKey(_settings.MigrationsPartitionKey))
                .GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Asynchronously initializes the migration store.
    /// </summary>
    public Task InitializeAsync()
    {
        Initialize();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets all applied migration versions.
    /// </summary>
    public ISet<long> GetAppliedVersions()
    {
        EnsureInitialized();

        try
        {
            var response = _container!.ReadItemAsync<MigrationsStateDocument>(
                _settings.MigrationsDocumentId, new PartitionKey(_settings.MigrationsPartitionKey)
            ).GetAwaiter().GetResult();

            var state = response.Resource;
            if (state?.AppliedMigrations == null)
            {
                return new HashSet<long>();
            }

            return new HashSet<long>(state.AppliedMigrations.Values.Select(m => m.Version));
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new HashSet<long>();
        }
    }

    /// <summary>
    /// Asynchronously gets all applied migration versions.
    /// </summary>
    public Task<ISet<long>> GetAppliedVersionsAsync()
    {
        return Task.FromResult(GetAppliedVersions());
    }

    /// <summary>
    /// Records that a migration has been applied.
    /// </summary>
    public void RecordMigration(Data.Migrations.IMigration migration)
    {
        EnsureInitialized();

        var response = _container!.ReadItemAsync<MigrationsStateDocument>(
            _settings.MigrationsDocumentId, new PartitionKey(_settings.MigrationsPartitionKey)
        ).GetAwaiter().GetResult();

        var state = response.Resource;
        state.AppliedMigrations ??= new Dictionary<string, MigrationRecord>();

        state.AppliedMigrations[migration.Version.ToString()] = new MigrationRecord
        {
            Version = migration.Version,
            Name = migration.Name,
            Description = migration.Description,
            CreatedAt = migration.CreatedAt,
            AppliedAt = DateTime.UtcNow
        };

        _container.ReplaceItemAsync(state, _settings.MigrationsDocumentId, new PartitionKey(_settings.MigrationsPartitionKey))
            .GetAwaiter().GetResult();

        _cachedState = state;
    }

    /// <summary>
    /// Asynchronously records that a migration has been applied.
    /// </summary>
    public Task RecordMigrationAsync(Data.Migrations.IMigration migration)
    {
        RecordMigration(migration);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a migration record (when downgrading).
    /// </summary>
    public void RemoveMigration(Data.Migrations.IMigration migration)
    {
        EnsureInitialized();

        var response = _container!.ReadItemAsync<MigrationsStateDocument>(
            _settings.MigrationsDocumentId, new PartitionKey(_settings.MigrationsPartitionKey)
        ).GetAwaiter().GetResult();

        var state = response.Resource;
        if (state?.AppliedMigrations != null)
        {
            state.AppliedMigrations.Remove(migration.Version.ToString());
            _container.ReplaceItemAsync(state, _settings.MigrationsDocumentId, new PartitionKey(_settings.MigrationsPartitionKey))
                .GetAwaiter().GetResult();
            _cachedState = state;
        }
    }

    /// <summary>
    /// Asynchronously removes a migration record.
    /// </summary>
    public Task RemoveMigrationAsync(Data.Migrations.IMigration migration)
    {
        RemoveMigration(migration);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the current version of the database.
    /// </summary>
    public long GetCurrentVersion()
    {
        var versions = GetAppliedVersions();
        return versions.Any() ? versions.Max() : 0;
    }

    /// <summary>
    /// Asynchronously gets the current version.
    /// </summary>
    public Task<long> GetCurrentVersionAsync()
    {
        return Task.FromResult(GetCurrentVersion());
    }

    private void EnsureInitialized()
    {
        if (_cachedState == null)
        {
            Initialize();
        }
    }

    /// <summary>
    /// Internal document class for storing migration state.
    /// </summary>
    internal class MigrationsStateDocument
    {
        public string Id { get; set; } = string.Empty;
        public string PartitionKey { get; set; } = string.Empty;
        public Dictionary<string, MigrationRecord> AppliedMigrations { get; set; } = new();
    }

    internal class MigrationRecord
    {
        public long Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime AppliedAt { get; set; }
    }
}
