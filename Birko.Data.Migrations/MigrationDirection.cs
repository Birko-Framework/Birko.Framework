namespace Birko.Data.Migrations
{
    /// <summary>
    /// Defines the direction of a migration operation.
    /// </summary>
    public enum MigrationDirection
    {
        /// <summary>
        /// Apply the migration (upgrade).
        /// </summary>
        Up,

        /// <summary>
        /// Revert the migration (downgrade).
        /// </summary>
        Down
    }
}
