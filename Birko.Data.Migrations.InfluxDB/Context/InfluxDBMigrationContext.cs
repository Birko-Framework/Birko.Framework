using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using InfluxDB.Client;

namespace Birko.Data.Migrations.InfluxDB.Context
{
    public class InfluxDBMigrationContext : IMigrationContext
    {
        private readonly InfluxDBClient _client;
        private readonly string _organization;

        public ISchemaBuilder Schema { get; }
        public IDataMigrator Data { get; }
        public string ProviderName => "InfluxDB";

        public InfluxDBMigrationContext(InfluxDBClient client, string organization)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _organization = organization ?? throw new ArgumentNullException(nameof(organization));
            Schema = new InfluxDBSchemaBuilder(client, organization);
            Data = new InfluxDBDataMigrator(client, organization);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_client);
    }
}
