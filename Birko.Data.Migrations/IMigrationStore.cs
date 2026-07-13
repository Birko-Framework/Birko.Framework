using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Migrations
{
    /// <summary>
    /// Defines the contract for storing and retrieving migration state.
    /// Implementations track which migrations have been applied.
    /// </summary>
    public interface IMigrationStore
    {
        /// <summary>
        /// Initializes the migration store (e.g., creates migrations table).
        /// </summary>
        void Initialize();

        /// <summary>
        /// Asynchronously initializes the migration store.
        /// </summary>
        Task InitializeAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets all applied migration versions.
        /// </summary>
        /// <returns>Set of applied version numbers.</returns>
        ISet<long> GetAppliedVersions();

        /// <summary>
        /// Asynchronously gets all applied migration versions.
        /// </summary>
        Task<ISet<long>> GetAppliedVersionsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Records that a migration has been applied.
        /// </summary>
        /// <param name="migration">The migration that was applied.</param>
        void RecordMigration(IMigration migration);

        /// <summary>
        /// Asynchronously records that a migration has been applied.
        /// </summary>
        /// <param name="migration">The migration that was applied.</param>
        Task RecordMigrationAsync(IMigration migration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a migration record (when downgrading).
        /// </summary>
        /// <param name="migration">The migration to remove.</param>
        void RemoveMigration(IMigration migration);

        /// <summary>
        /// Asynchronously removes a migration record (when downgrading).
        /// </summary>
        /// <param name="migration">The migration to remove.</param>
        Task RemoveMigrationAsync(IMigration migration, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current version of the database.
        /// </summary>
        /// <returns>The highest applied version, or 0 if no migrations applied.</returns>
        long GetCurrentVersion();

        /// <summary>
        /// Asynchronously gets the current version of the database.
        /// </summary>
        Task<long> GetCurrentVersionAsync(CancellationToken cancellationToken = default);
    }
}
