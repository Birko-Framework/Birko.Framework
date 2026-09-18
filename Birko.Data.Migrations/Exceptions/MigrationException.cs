using System;

namespace Birko.Data.Migrations.Exceptions
{
    /// <summary>
    /// Exception thrown when a migration operation fails.
    /// </summary>
    public class MigrationException : Exception
    {
        /// <summary>
        /// Gets the migration that failed.
        /// </summary>
        public IMigration? Migration { get; }

        /// <summary>
        /// Gets the direction in which the migration was executing.
        /// </summary>
        public MigrationDirection Direction { get; }

        /// <summary>
        /// Initializes a new instance of the MigrationException class.
        /// </summary>
        public MigrationException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the MigrationException class.
        /// </summary>
        public MigrationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MigrationException class.
        /// </summary>
        public MigrationException(string message, Exception innerException) : base(message, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the MigrationException class with migration context.
        /// </summary>
        public MigrationException(IMigration migration, MigrationDirection direction, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Migration = migration;
            Direction = direction;
        }

        /// <summary>
        /// Initializes a new instance of the MigrationException class with migration context.
        /// </summary>
        public MigrationException(IMigration migration, MigrationDirection direction, Exception innerException)
            : base($"Migration {migration.Version} ({migration.Name}) failed during {direction}.", innerException)
        {
            Migration = migration;
            Direction = direction;
        }
    }
}
