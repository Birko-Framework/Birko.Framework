using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Operations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Birko.Data.Migrations.RavenDB
{
    /// <summary>
    /// Stores migration state in a RavenDB document.
    /// </summary>
    public class RavenMigrationStore : Data.Migrations.IMigrationStore
    {
        private const string MigrationsDocumentId = "Migrations/State";
        private readonly IDocumentStore _store;

        private MigrationsStateDocument? _cachedState;

        /// <summary>
        /// Initializes a new instance of the RavenMigrationStore class.
        /// </summary>
        public RavenMigrationStore(IDocumentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Initializes the migration store (creates migrations document if needed).
        /// </summary>
        public void Initialize()
        {
            // Load or create state document
            using var session = _store.OpenSession();
            var state = session.Load<MigrationsStateDocument>(MigrationsDocumentId);

            if (state == null)
            {
                state = new MigrationsStateDocument
                {
                    Id = MigrationsDocumentId,
                    AppliedMigrations = new Dictionary<string, MigrationRecord>()
                };
                session.Store(state);
                session.SaveChanges();
            }

            _cachedState = state;
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

            using var session = _store.OpenSession();
            var state = session.Load<MigrationsStateDocument>(MigrationsDocumentId);

            if (state == null || state.AppliedMigrations == null)
            {
                return new HashSet<long>();
            }

            return new HashSet<long>(state.AppliedMigrations.Values.Select(m => m.Version));
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

            using var session = _store.OpenSession();
            var state = session.Load<MigrationsStateDocument>(MigrationsDocumentId) ?? new MigrationsStateDocument
            {
                Id = MigrationsDocumentId,
                AppliedMigrations = new Dictionary<string, MigrationRecord>()
            };

            var record = new MigrationRecord
            {
                Version = migration.Version,
                Name = migration.Name,
                Description = migration.Description,
                CreatedAt = migration.CreatedAt,
                AppliedAt = DateTime.UtcNow
            };

            state.AppliedMigrations[migration.Version.ToString()] = record;
            session.Store(state);
            session.SaveChanges();

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

            using var session = _store.OpenSession();
            var state = session.Load<MigrationsStateDocument>(MigrationsDocumentId);

            if (state?.AppliedMigrations != null)
            {
                state.AppliedMigrations.Remove(migration.Version.ToString());
                session.Store(state);
                session.SaveChanges();
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
}
