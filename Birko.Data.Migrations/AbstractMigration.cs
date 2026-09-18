using System;
using Birko.Data.Migrations.Context;

namespace Birko.Data.Migrations
{
    /// <summary>
    /// Abstract base class for migrations.
    /// </summary>
    public abstract class AbstractMigration : IMigration
    {
        /// <summary>
        /// Gets the version number of this migration.
        /// </summary>
        public abstract long Version { get; }

        /// <summary>
        /// Gets a descriptive name for this migration.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Gets the description of what this migration does.
        /// </summary>
        public virtual string Description => Name;

        /// <summary>
        /// Gets the date and time when this migration was created.
        /// </summary>
        public DateTime CreatedAt { get; }

        /// <summary>
        /// Initializes a new instance of the AbstractMigration class.
        /// </summary>
        protected AbstractMigration()
        {
            CreatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Applies the migration (upgrade).
        /// </summary>
        public abstract void Up(IMigrationContext context);

        /// <summary>
        /// Reverts the migration (downgrade).
        /// Default implementation throws NotImplementedException.
        /// </summary>
        public virtual void Down(IMigrationContext context)
        {
            throw new NotImplementedException($"Down migration for '{Name}' (v{Version}) is not implemented.");
        }

        /// <summary>
        /// Returns a string representation of this migration.
        /// </summary>
        public override string ToString()
        {
            return $"Migration {Version}: {Name}";
        }
    }
}
