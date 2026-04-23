using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using Nest;

namespace Birko.Data.Migrations.ElasticSearch.Context
{
    public class ElasticSearchMigrationContext : IMigrationContext
    {
        private readonly ElasticClient _client;

        public ISchemaBuilder Schema { get; }
        public IDataMigrator Data { get; }
        public string ProviderName => "ElasticSearch";

        public ElasticSearchMigrationContext(ElasticClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Schema = new ElasticSearchSchemaBuilder(client);
            Data = new ElasticSearchDataMigrator(client);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_client);
    }
}
