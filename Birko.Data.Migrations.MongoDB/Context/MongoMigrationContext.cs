using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using MongoDB.Driver;

namespace Birko.Data.Migrations.MongoDB.Context
{
    public class MongoMigrationContext : IMigrationContext
    {
        private readonly IMongoDatabase _database;

        public ISchemaBuilder Schema { get; }
        public IDataMigrator Data { get; }
        public string ProviderName => "MongoDB";

        public MongoMigrationContext(IMongoDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            Schema = new MongoSchemaBuilder(database);
            Data = new MongoDataMigrator(database);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_database);
    }
}
