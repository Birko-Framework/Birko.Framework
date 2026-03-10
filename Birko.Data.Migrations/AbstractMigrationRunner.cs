using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Migrations
{
    /// <summary>
    /// Abstract base class for migration runners.
    /// Handles the logic of determining which migrations to run.
    /// </summary>
    public abstract class AbstractMigrationRunner : IMigrationRunner
    {
        private readonly List<IMigration> _migrations = new();
        private bool _isInitialized;

        /// <summary>
        /// Gets the migration store used to track state.
        /// </summary>
        public IMigrationStore Store { get; }

        /// <summary>
        /// Gets all registered migrations.
        /// </summary>
        public IReadOnlyList<IMigration> Migrations => _migrations;

        /// <summary>
        /// Gets the current database version.
        /// </summary>
        public long CurrentVersion => Store.GetCurrentVersion();

        /// <summary>
        /// Gets the latest available migration version.
        /// </summary>
        public long LatestVersion => _migrations.Any() ? _migrations.Max(m => m.Version) : 0;

        /// <summary>
        /// Initializes a new instance of the AbstractMigrationRunner class.
        /// </summary>
        /// <param name="store">The migration store to use.</param>
        protected AbstractMigrationRunner(IMigrationStore store)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Registers migrations for execution.
        /// </summary>
        public void RegisterMigrations(params IMigration[] migrations)
        {
            if (migrations == null) return;

            foreach (var migration in migrations)
            {
                if (migration == null) continue;

                if (_migrations.Any(m => m.Version == migration.Version))
                {
                    throw new InvalidOperationException($"A migration with version {migration.Version} is already registered.");
                }

                _migrations.Add(migration);
            }

            // Sort by version
            _migrations.Sort((a, b) => a.Version.CompareTo(b.Version));
        }

        /// <summary>
        /// Registers migrations from an enumeration.
        /// </summary>
        public void RegisterMigrations(IEnumerable<IMigration> migrations)
        {
            RegisterMigrations(migrations?.ToArray());
        }

        /// <summary>
        /// Initializes the migration runner and store.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;

            Store.Initialize();
            _isInitialized = true;
        }

        /// <summary>
        /// Asynchronously initializes the migration runner and store.
        /// </summary>
        public virtual System.Threading.Tasks.Task InitializeAsync()
        {
            Initialize();
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Migrates up to the specified version.
        /// If target is null, migrates to the latest version.
        /// </summary>
        public virtual MigrationResult Migrate(long? targetVersion = null)
        {
            EnsureInitialized();

            var current = CurrentVersion;
            var target = targetVersion ?? LatestVersion;

            if (target == current)
            {
                return MigrationResult.Successful(current, current, MigrationDirection.Up, Array.Empty<ExecutedMigration>());
            }

            if (target < current)
            {
                return MigrationResult.Failed(current, MigrationDirection.Up, $"Target version {target} is less than current version {current}. Use Rollback for downgrades.");
            }

            return ExecuteMigrations(current, target, MigrationDirection.Up);
        }

        /// <summary>
        /// Asynchronously migrates up to the specified version.
        /// </summary>
        public virtual System.Threading.Tasks.Task<MigrationResult> MigrateAsync(long? targetVersion = null)
        {
            return System.Threading.Tasks.Task.FromResult(Migrate(targetVersion));
        }

        /// <summary>
        /// Rolls back to the specified version.
        /// </summary>
        public virtual MigrationResult Rollback(long targetVersion)
        {
            EnsureInitialized();

            var current = CurrentVersion;

            if (targetVersion == current)
            {
                return MigrationResult.Successful(current, current, MigrationDirection.Down, Array.Empty<ExecutedMigration>());
            }

            if (targetVersion > current)
            {
                return MigrationResult.Failed(current, MigrationDirection.Down, $"Target version {targetVersion} is greater than current version {current}. Use Migrate for upgrades.");
            }

            return ExecuteMigrations(current, targetVersion, MigrationDirection.Down);
        }

        /// <summary>
        /// Asynchronously rolls back to the specified version.
        /// </summary>
        public virtual System.Threading.Tasks.Task<MigrationResult> RollbackAsync(long targetVersion)
        {
            return System.Threading.Tasks.Task.FromResult(Rollback(targetVersion));
        }

        /// <summary>
        /// Gets pending migrations (not yet applied).
        /// </summary>
        public IReadOnlyList<IMigration> GetPendingMigrations()
        {
            EnsureInitialized();
            var applied = Store.GetAppliedVersions();
            return _migrations.Where(m => !applied.Contains(m.Version)).ToList();
        }

        /// <summary>
        /// Gets applied migrations that could be rolled back.
        /// </summary>
        public IReadOnlyList<IMigration> GetAppliedMigrations()
        {
            EnsureInitialized();
            var applied = Store.GetAppliedVersions();
            return _migrations.Where(m => applied.Contains(m.Version)).ToList();
        }

        /// <summary>
        /// Executes migrations in the specified direction.
        /// Derived classes should override this to handle transactions.
        /// </summary>
        protected abstract MigrationResult ExecuteMigrations(long fromVersion, long toVersion, MigrationDirection direction);

        /// <summary>
        /// Gets the migrations to execute for a version range.
        /// </summary>
        protected IReadOnlyList<IMigration> GetMigrationsToExecute(long fromVersion, long toVersion, MigrationDirection direction)
        {
            IEnumerable<IMigration> query = direction == MigrationDirection.Up
                ? _migrations.Where(m => m.Version > fromVersion && m.Version <= toVersion)
                : _migrations.Where(m => m.Version <= fromVersion && m.Version > toVersion);

            // Reverse order for Down migrations
            return direction == MigrationDirection.Up
                ? query.OrderBy(m => m.Version).ToList()
                : query.OrderByDescending(m => m.Version).ToList();
        }

        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Migration runner has not been initialized. Call Initialize() first.");
            }
        }
    }
}
