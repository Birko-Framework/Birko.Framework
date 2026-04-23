using System;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using Microsoft.Azure.Cosmos;

namespace Birko.Data.Migrations.CosmosDB.Context;

public class CosmosDBMigrationContext : IMigrationContext
{
    private readonly Database _database;

    public ISchemaBuilder Schema { get; }
    public IDataMigrator Data { get; }
    public string ProviderName => "CosmosDB";

    public CosmosDBMigrationContext(Database database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        Schema = new CosmosDBSchemaBuilder(database);
        Data = new CosmosDBDataMigrator(database);
    }

    public void Raw(Action<object> providerAction)
        => providerAction(_database);
}
