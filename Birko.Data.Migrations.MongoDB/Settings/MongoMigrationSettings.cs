namespace Birko.Data.Migrations.MongoDB.Settings
{
    /// <summary>
    /// Settings for MongoDB migration runners.
    /// </summary>
    public class MongoMigrationSettings : Birko.Data.MongoDB.Stores.Settings
    {
        /// <summary>
        /// Gets or sets the name of the migrations collection.
        /// Default is "__migrations".
        /// </summary>
        public string MigrationsCollection { get; set; } = "__migrations";

        /// <summary>
        /// Gets or sets whether to use sessions for transactions.
        /// Default is true.
        /// Note: Transactions require MongoDB 4.0+ replica sets.
        /// </summary>
        public bool UseSession { get; set; } = true;
    }
}
