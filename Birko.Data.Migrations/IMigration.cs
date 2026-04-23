using System;
using Birko.Data.Migrations.Context;

namespace Birko.Data.Migrations
{
    /// <summary>
    /// Defines a single migration with Up (apply) and Down (revert) operations.
    /// Migrations receive an IMigrationContext that provides platform-agnostic
    /// schema and data operations.
    /// </summary>
    public interface IMigration
    {
        /// <summary>
        /// Gets the version number of this migration.
        /// Versions should be sequential and unique within a migration set.
        /// </summary>
        long Version { get; }

        /// <summary>
        /// Gets a descriptive name for this migration.
        /// Should be brief and indicate what changes are made.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the description of what this migration does.
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Gets the date and time when this migration was created.
        /// </summary>
        DateTime CreatedAt { get; }

        /// <summary>
        /// Applies the migration (upgrade).
        /// </summary>
        void Up(IMigrationContext context);

        /// <summary>
        /// Reverts the migration (downgrade).
        /// Should undo all changes made by Up().
        /// </summary>
        void Down(IMigrationContext context);
    }
}
