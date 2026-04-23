using System;
using Birko.Data.Patterns.Schema;

namespace Birko.Data.Migrations.Context
{
    public interface IMigrationContext
    {
        ISchemaBuilder Schema { get; }
        IDataMigrator Data { get; }

        string ProviderName { get; }

        void Raw(Action<object> providerAction);
    }
}
