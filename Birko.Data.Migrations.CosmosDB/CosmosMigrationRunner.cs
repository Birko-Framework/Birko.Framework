using Microsoft.Azure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Migrations.CosmosDB;

/// <summary>
/// Executes Cosmos DB migrations.
/// </summary>
public class CosmosMigrationRunner : Data.Migrations.AbstractMigrationRunner
{
    private readonly Database _database;

    /// <summary>
    /// Gets the Cosmos DB database.
    /// </summary>
    public Database Database => _database;

    /// <summary>
    /// Initializes a new instance of the CosmosMigrationRunner class.
    /// </summary>
    public CosmosMigrationRunner(Database database)
        : base(new CosmosMigrationStore(database))
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Executes migrations in the specified direction.
    /// </summary>
    protected override Data.Migrations.MigrationResult ExecuteMigrations(long fromVersion, long toVersion, Data.Migrations.MigrationDirection direction)
    {
        var migrations = GetMigrationsToExecute(fromVersion, toVersion, direction);
        var executed = new List<Data.Migrations.ExecutedMigration>();

        if (!migrations.Any())
        {
            return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
        }

        var store = (CosmosMigrationStore)Store;

        try
        {
            foreach (var migration in migrations)
            {
                if (migration is CosmosMigration cosmosMigration)
                {
                    cosmosMigration.Execute(_database, direction);
                }
                else if (direction == Data.Migrations.MigrationDirection.Up)
                {
                    migration.Up();
                }
                else
                {
                    migration.Down();
                }

                if (direction == Data.Migrations.MigrationDirection.Up)
                {
                    store.RecordMigration(migration);
                }
                else
                {
                    store.RemoveMigration(migration);
                }

                executed.Add(new Data.Migrations.ExecutedMigration(migration, direction));
            }

            return Data.Migrations.MigrationResult.Successful(fromVersion, toVersion, direction, executed);
        }
        catch (Exception ex)
        {
            var failedMigration = executed.Count > 0 ? migrations[executed.Count] : migrations[0];
            throw new Exceptions.MigrationException(failedMigration, direction, "Migration failed. Cosmos DB state may be inconsistent.", ex);
        }
    }
}
