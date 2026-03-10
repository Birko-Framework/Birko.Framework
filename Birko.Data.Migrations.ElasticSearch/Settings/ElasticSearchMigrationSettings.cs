using Nest;

namespace Birko.Data.Migrations.ElasticSearch.Settings
{
    /// <summary>
    /// Settings for ElasticSearch migration runners.
    /// </summary>
    public class ElasticSearchMigrationSettings : Birko.Data.ElasticSearch.Stores.Settings
    {
        /// <summary>
        /// Gets or sets the name of the migrations index.
        /// Default is "__migrations".
        /// </summary>
        public string MigrationsIndex { get; set; } = "__migrations";

        /// <summary>
        /// Gets or sets whether migrations use index aliases for zero-downtime deployments.
        /// Default is true.
        /// </summary>
        public bool UseAliases { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of shards for created indices.
        /// Default is 1.
        /// </summary>
        public int? NumberOfShards { get; set; } = 1;

        /// <summary>
        /// Gets or sets the number of replicas for created indices.
        /// Default is 1.
        /// </summary>
        public int? NumberOfReplicas { get; set; } = 1;
    }
}
