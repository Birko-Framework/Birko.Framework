using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.CosmosDB;

/// <summary>
/// Stores migration state in a Cosmos DB container.
/// Uses a single document with ID "Migrations/State" to track applied migrations.
/// </summary>
public class CosmosMigrationStore : Data.Migrations.IMigrationStore
{
    private const string MigrationsContainerName = "Migrations";
    private const string MigrationsDocumentId = "Migrations-State";
    private const string MigrationsPartitionKey = "migrations";

    private readonly Database _database;
    private Container? _container;
    private MigrationsStateDocument? _cachedState;

    /// <summary>
    /// Initializes a new instance of the CosmosMigrationStore class.
    /// </summary>
    public CosmosMigrationStore(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Initializes the migration store (creates container and state document if needed).
    /// </summary>
    public void Initialize()
    {
        _container = _database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(MigrationsContainerName, "/partitionKey")
        ).GetAwaiter().GetResult().Container;

        try
        {
            var response = _container.ReadItemAsync<MigrationsStateDocument>(
                MigrationsDocumentId, new PartitionKey(MigrationsPartitionKey)
            ).GetAwaiter().GetResult();
            _cachedState = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _cachedState = new MigrationsStateDocument
            {
                Id = MigrationsDocumentId,
                PartitionKey = MigrationsPartitionKey,
                AppliedMigrations = new Dictionary<string, MigrationRecord>()
            };
            _container.CreateItemAsync(_cachedState, new PartitionKey(MigrationsPartitionKey))
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
                MigrationsDocumentId, new PartitionKey(MigrationsPartitionKey)
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
            MigrationsDocumentId, new PartitionKey(MigrationsPartitionKey)
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

        _container.ReplaceItemAsync(state, MigrationsDocumentId, new PartitionKey(MigrationsPartitionKey))
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
            MigrationsDocumentId, new PartitionKey(MigrationsPartitionKey)
        ).GetAwaiter().GetResult();

        var state = response.Resource;
        if (state?.AppliedMigrations != null)
        {
            state.AppliedMigrations.Remove(migration.Version.ToString());
            _container.ReplaceItemAsync(state, MigrationsDocumentId, new PartitionKey(MigrationsPartitionKey))
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
        public string Id { get; set; } = MigrationsDocumentId;
        public string PartitionKey { get; set; } = MigrationsPartitionKey;
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
