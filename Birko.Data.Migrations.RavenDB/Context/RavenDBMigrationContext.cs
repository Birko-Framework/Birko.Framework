using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using Raven.Client.Documents;

namespace Birko.Data.Migrations.RavenDB.Context
{
    public class RavenDBMigrationContext : IMigrationContext
    {
        private readonly IDocumentStore _store;

        public ISchemaBuilder Schema { get; }
        public IDataMigrator Data { get; }
        public string ProviderName => "RavenDB";

        public RavenDBMigrationContext(IDocumentStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            Schema = new RavenDBSchemaBuilder(store);
            Data = new RavenDBDataMigrator(store);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_store);
    }
}
