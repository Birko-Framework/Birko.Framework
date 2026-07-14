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
        /// <remarks>
        /// CR-L142: reserved / not yet wired — no alias logic exists in the store or schema builder today.
        /// <see cref="NumberOfShards"/> / <see cref="NumberOfReplicas"/> are honored by the migrations-index
        /// create; the data-collection schema builder does not yet receive these settings.
        /// </remarks>
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
