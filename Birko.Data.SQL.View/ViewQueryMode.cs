namespace Birko.Data.SQL
{
    /// <summary>
    /// Controls how view queries are executed against the database.
    /// </summary>
    public enum ViewQueryMode
    {
        /// <summary>
        /// Always generate SELECT with joins from attributes (current default behavior).
        /// </summary>
        OnTheFly = 0,

        /// <summary>
        /// Always query against the persistent database VIEW by name.
        /// Assumes the view has been created in the database.
        /// </summary>
        Persistent = 1,

        /// <summary>
        /// Try querying the persistent view first; fall back to on-the-fly SELECT if the view doesn't exist.
        /// Caches the view existence check result per view name.
        /// </summary>
        Auto = 2,
    }
}
