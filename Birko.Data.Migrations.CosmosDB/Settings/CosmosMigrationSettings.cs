namespace Birko.Data.Migrations.CosmosDB.Settings;

/// <summary>
/// Settings for Cosmos DB migration runners.
/// Extends CosmosDB Settings to inherit connection and container configuration.
/// Base type is fully qualified because this class's own namespace ends in .Settings,
/// which shadows the unqualified type name <c>Settings</c> under a using directive.
/// </summary>
public class CosmosMigrationSettings : Birko.Data.CosmosDB.Stores.Settings
{
    /// <summary>
    /// Gets or sets the name of the container that stores migration state.
    /// Default is "Migrations".
    /// </summary>
    public string MigrationsContainerName { get; set; } = "Migrations";

    /// <summary>
    /// Gets or sets the id of the state document inside the migrations container.
    /// Multiple modules can share one container by using different ids here
    /// (e.g. "Migrations-State-IoT", "Migrations-State-Events").
    /// Default is "Migrations-State".
    /// </summary>
    public string MigrationsDocumentId { get; set; } = "Migrations-State";

    /// <summary>
    /// Gets or sets the partition key value used for the state document.
    /// When sharing a container across modules, give each module a unique
    /// partition key value so read/write isolation is preserved.
    /// Default is "migrations".
    /// </summary>
    public string MigrationsPartitionKey { get; set; } = "migrations";
}
