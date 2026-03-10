using System;
using System.Collections.Generic;

namespace Birko.Data.Migrations
{
    /// <summary>
    /// Represents the result of a migration operation.
    /// </summary>
    public class MigrationResult
    {
        /// <summary>
        /// Gets whether the operation was successful.
        /// </summary>
        public bool Success { get; init; }

        /// <summary>
        /// Gets the version before the operation.
        /// </summary>
        public long FromVersion { get; init; }

        /// <summary>
        /// Gets the version after the operation.
        /// </summary>
        public long ToVersion { get; init; }

        /// <summary>
        /// Gets the direction of the migration.
        /// </summary>
        public MigrationDirection Direction { get; init; }

        /// <summary>
        /// Gets migrations that were executed during the operation.
        /// </summary>
        public IReadOnlyList<ExecutedMigration> ExecutedMigrations { get; init; }

        /// <summary>
        /// Gets the error message if the operation failed.
        /// </summary>
        public string? ErrorMessage { get; init; }

        /// <summary>
        /// Gets the exception if the operation failed.
        /// </summary>
        public Exception? Exception { get; init; }

        /// <summary>
        /// Creates a successful migration result.
        /// </summary>
        public static MigrationResult Successful(long from, long to, MigrationDirection direction, IReadOnlyList<ExecutedMigration> executed)
            => new()
            {
                Success = true,
                FromVersion = from,
                ToVersion = to,
                Direction = direction,
                ExecutedMigrations = executed
            };

        /// <summary>
        /// Creates a failed migration result.
        /// </summary>
        public static MigrationResult Failed(long from, MigrationDirection direction, string errorMessage, Exception? exception = null)
            => new()
            {
                Success = false,
                FromVersion = from,
                ToVersion = from,
                Direction = direction,
                ErrorMessage = errorMessage,
                Exception = exception
            };
    }

    /// <summary>
    /// Represents a migration that was executed.
    /// </summary>
    public class ExecutedMigration
    {
        /// <summary>
        /// Gets the migration that was executed.
        /// </summary>
        public IMigration Migration { get; init; }

        /// <summary>
        /// Gets the direction in which the migration was executed.
        /// </summary>
        public MigrationDirection Direction { get; init; }

        /// <summary>
        /// Gets the time when the migration was executed.
        /// </summary>
        public DateTime ExecutedAt { get; init; }

        public ExecutedMigration(IMigration migration, MigrationDirection direction)
        {
            Migration = migration;
            Direction = direction;
            ExecutedAt = DateTime.UtcNow;
        }
    }
}
