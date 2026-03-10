using Nest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.ElasticSearch
{
    /// <summary>
    /// Stores migration state in an ElasticSearch index.
    /// </summary>
    public class ElasticSearchMigrationStore : Data.Migrations.IMigrationStore
    {
        private readonly ElasticClient _client;
        private readonly Settings.ElasticSearchMigrationSettings _settings;
        private const string MigrationDocType = "_doc";

        /// <summary>
        /// Initializes a new instance of the ElasticSearchMigrationStore class.
        /// </summary>
        public ElasticSearchMigrationStore(ElasticClient client, Settings.ElasticSearchMigrationSettings? settings = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _settings = settings ?? new Settings.ElasticSearchMigrationSettings();
        }

        /// <summary>
        /// Initializes the migration store (creates migrations index if needed).
        /// </summary>
        public void Initialize()
        {
            var indexName = GetMigrationsIndex();

            if (!_client.Indices.Exists(indexName).Exists)
            {
                _client.Indices.Create(indexName, c => c
                    .Settings(s => s
                        .NumberOfShards(1)
                        .NumberOfReplicas(0)
                    )
                    .Map<MigrationDocument>(m => m
                        .Properties(p => p
                            .Keyword(k => k.Name(n => n.Version))
                            .Text(t => t.Name(n => n.Name))
                            .Text(t => t.Name(n => n.Description))
                            .Date(d => d.Name(n => n.CreatedAt))
                            .Date(d => d.Name(n => n.AppliedAt))
                        )
                    )
                );
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
            var indexName = GetMigrationsIndex();

            if (!_client.Indices.Exists(indexName).Exists)
            {
                return new HashSet<long>();
            }

            var searchResponse = _client.Search<MigrationDocument>(s => s
                .Index(indexName)
                .Size(1000)
                .Sort(sort => sort.Ascending(f => f.Version))
            );

            if (!searchResponse.IsValid)
            {
                return new HashSet<long>();
            }

            return new HashSet<long>(searchResponse.Documents.Select(d => d.Version));
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
            var indexName = GetMigrationsIndex();
            var docId = migration.Version.ToString();

            var document = new MigrationDocument
            {
                Version = migration.Version,
                Name = migration.Name,
                Description = migration.Description,
                CreatedAt = migration.CreatedAt,
                AppliedAt = DateTime.UtcNow
            };

            var response = _client.Index(document, i => i.Index(indexName).Id(docId));
            if (!response.IsValid)
            {
                throw new InvalidOperationException($"Failed to record migration {migration.Version}: {response.DebugInformation}", response.OriginalException);
            }
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
            var indexName = GetMigrationsIndex();
            var docId = migration.Version.ToString();

            var response = _client.Delete<MigrationDocument>(docId, d => d.Index(indexName));
            // Ignore 404 errors (migration already removed)
            if (!response.IsValid && response.ServerError?.Status != 404)
            {
                throw new InvalidOperationException($"Failed to remove migration {migration.Version}: {response.DebugInformation}", response.OriginalException);
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

        private string GetMigrationsIndex()
        {
            var indexName = _settings.MigrationsIndex;
            if (!string.IsNullOrEmpty(_settings.Name))
            {
                indexName = $"{_settings.Name}_{_settings.MigrationsIndex}";
            }
            return indexName.ToLowerInvariant();
        }

        /// <summary>
        /// Internal document class for storing migration records.
        /// </summary>
        internal class MigrationDocument
        {
            public long Version { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public DateTime AppliedAt { get; set; }
        }
    }
}
