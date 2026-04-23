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

        public SqlMigrationContext(DbConnection connection, DbTransaction? transaction, string providerName, AbstractConnector? connector = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction;
            ProviderName = providerName;
            Schema = new SqlSchemaBuilder(connection, transaction, connector);
            Data = new SqlDataMigrator(connection, transaction);
        }

        public void Raw(Action<object> providerAction)
            => providerAction(_connection);
    }
}
