using System;
using System.Data.Common;
using Birko.Data.Migrations.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL.Context
{
    public class SqlMigrationContext : IMigrationContext
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction? _transaction;

        public ISchemaBuilder Schema { get; }
        public IDataMigrator Data { get; }
        public string ProviderName { get; }

        /// <summary>
        /// Gets the database connection.
        /// </summary>
        public DbConnection Connection => _connection;

        /// <summary>
        /// Gets the active transaction, or null if no transaction.
        /// </summary>
        public DbTransaction? Transaction => _transaction;

        /// <summary>
        /// Creates the migration context. <paramref name="connector"/> is <b>required</b> (TASK-247).
        /// </summary>
        /// <remarks>
        /// It was optional, and that optional argument was the only door to <c>SqlSchemaBuilder</c>'s
        /// hand-written raw-SQL fallbacks — which emitted index DDL that MySQL and PostgreSQL both reject. So
        /// the connector-free path offered a capability it could not deliver. Verified reachable-by-nobody
        /// before requiring it: the only production caller is <c>SqlMigrationRunner</c>, which already holds a
        /// non-null connector, and a sweep of all 16 consumer repos found no hand-built context and no use of
        /// <c>ISchemaBuilder</c> at all.
        /// </remarks>
        public SqlMigrationContext(DbConnection connection, DbTransaction? transaction, string providerName, AbstractConnector connector)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction;
            ProviderName = providerName;
            Schema = new SqlSchemaBuilder(connection, transaction, connector);
            Data = new SqlDataMigrator(connection, transaction, connector);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_connection);
    }
}
