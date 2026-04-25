namespace Birko.Data.Migrations.RavenDB.Settings
{
    /// <summary>
    /// Settings for RavenDB migration runners.
    /// Extends RavenDB Settings to inherit connection and timeout configuration.
    /// Base type is fully qualified because this class's own namespace ends in .Settings,
    /// which shadows the unqualified type name <c>Settings</c> under a using directive.
    /// </summary>
    public class RavenMigrationSettings : Birko.Data.RavenDB.Stores.Settings
    {
        /// <summary>
        /// Gets or sets the id of the document that stores migration state.
        /// Multiple modules can share one Raven database by using different ids here
        /// (e.g. "Migrations/State/IoT", "Migrations/State/Events").
        /// Default is "Migrations/State".
        /// </summary>
        public string MigrationsDocumentId { get; set; } = "Migrations/State";
    }
}
