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

        // The optional session is threaded into the schema builder + data migrator so their operations
        // participate in the runner's transaction (CR-C09). Without it, driver calls commit immediately.
        public MongoMigrationContext(IMongoDatabase database, IClientSessionHandle? session = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            Schema = new MongoSchemaBuilder(database, session);
            Data = new MongoDataMigrator(database, session);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_database);
    }
}
