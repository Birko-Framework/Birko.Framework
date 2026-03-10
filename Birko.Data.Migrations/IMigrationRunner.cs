using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Birko.Data.Migrations
{
    /// <summary>
    /// Defines the contract for executing migrations.
    /// </summary>
    public interface IMigrationRunner
    {
        /// <summary>
        /// Gets the migration store used to track state.
        /// </summary>
        IMigrationStore Store { get; }

        /// <summary>
        /// Gets all registered migrations.
        /// </summary>
        IReadOnlyList<IMigration> Migrations { get; }

        /// <summary>
        /// Gets the current database version.
        /// </summary>
        long CurrentVersion { get; }

        /// <summary>
        /// Gets the latest available migration version.
        /// </summary>
        long LatestVersion { get; }

        /// <summary>
        /// Registers migrations for execution.
        /// </summary>
        /// <param name="migrations">The migrations to register.</param>
        void RegisterMigrations(params IMigration[] migrations);

        /// <summary>
        /// Registers migrations from an enumeration.
        /// </summary>
        /// <param name="migrations">The migrations to register.</param>
        void RegisterMigrations(IEnumerable<IMigration> migrations);

        /// <summary>
        /// Initializes the migration runner and store.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Asynchronously initializes the migration runner and store.
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Migrates up to the specified version.
        /// If target is null, migrates to the latest version.
        /// </summary>
        /// <param name="targetVersion">The target version, or null for latest.</param>
        /// <returns>Result of the migration operation.</returns>
        MigrationResult Migrate(long? targetVersion = null);

        /// <summary>
        /// Asynchronously migrates up to the specified version.
        /// </summary>
        /// <param name="targetVersion">The target version, or null for latest.</param>
        Task<MigrationResult> MigrateAsync(long? targetVersion = null);

        /// <summary>
        /// Rolls back to the specified version.
        /// </summary>
        /// <param name="targetVersion">The target version to roll back to.</param>
        /// <returns>Result of the migration operation.</returns>
        MigrationResult Rollback(long targetVersion);

        /// <summary>
        /// Asynchronously rolls back to the specified version.
        /// </summary>
        /// <param name="targetVersion">The target version to roll back to.</param>
        Task<MigrationResult> RollbackAsync(long targetVersion);

        /// <summary>
        /// Gets pending migrations (not yet applied).
        /// </summary>
        IReadOnlyList<IMigration> GetPendingMigrations();

        /// <summary>
        /// Gets applied migrations that could be rolled back.
        /// </summary>
        IReadOnlyList<IMigration> GetAppliedMigrations();
    }
}
