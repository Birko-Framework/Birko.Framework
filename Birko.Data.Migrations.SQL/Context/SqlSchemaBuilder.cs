using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL.Context
{
    public class SqlSchemaBuilder : ISchemaBuilder
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction? _transaction;
        private readonly AbstractConnector? _connector;

        public SqlSchemaBuilder(DbConnection connection, DbTransaction? transaction, AbstractConnector? connector = null)
        {
            _connection = connection;
            _transaction = transaction;
            _connector = connector;
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            return new SqlCollectionBuilder(name, _connection, _transaction, _connector);
        }

        public void DropCollection(string name)
        {
            if (_connector != null)
            {
                EnsureExternalTransaction();
                _connector.DropTable(new[] { name });
                return;
            }

            Execute($"DROP TABLE IF EXISTS {QuoteIdentifier(name)}");
        }

        public bool CollectionExists(string name)
        {
            // CR-L152: INFORMATION_SCHEMA is standard for MSSql/MySQL/PostgreSQL but does not exist in
            // SQLite (which uses sqlite_master), so an unconditional INFORMATION_SCHEMA query threw there.
            // Pick the catalog query from the connection's provider.
            var connTypeName = _connection.GetType().Name;
            var isSqlite = connTypeName.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0;

            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = isSqlite
                ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName"
                : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName";
            var param = command.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = name;
            command.Parameters.Add(param);
            var result = command.ExecuteScalar();
            return Convert.ToInt64(result) > 0;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return new SqlIndexBuilder(collectionName, indexName, _connection, _transaction, _connector);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            if (_connector != null)
            {
                EnsureExternalTransaction();
                var indexDef = new Birko.Data.SQL.Tables.IndexDefinition { Name = indexName };
                _connector.DropIndexes(collectionName, new[] { indexDef });
                return;
            }

            Execute($"DROP INDEX IF EXISTS {QuoteIdentifier(indexName)} ON {QuoteIdentifier(collectionName)}");
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            if (_connector != null)
            {
                EnsureExternalTransaction();
                _connector.AlterTableAdd(collectionName, new[] { new SchemaField(field) });
                return;
            }

            var sqlType = FieldTypeToSql(field);
            var nullable = field.IsRequired ? " NOT NULL" : "";
            var defaultVal = field.DefaultValue != null ? $" DEFAULT {FormatValue(field.DefaultValue)}" : "";
            Execute($"ALTER TABLE {QuoteIdentifier(collectionName)} ADD COLUMN {QuoteIdentifier(field.Name)} {sqlType}{nullable}{defaultVal}");
        }

        public void DropField(string collectionName, string fieldName)
        {
            if (_connector != null)
            {
                EnsureExternalTransaction();
                var field = new SchemaField(new FieldDescriptor { Name = fieldName, Type = FieldType.String });
                _connector.AlterTableDrop(collectionName, new[] { field });
                return;
            }

            Execute($"ALTER TABLE {QuoteIdentifier(collectionName)} DROP COLUMN {QuoteIdentifier(fieldName)}");
        }

        public void RenameField(string collectionName, string oldName, string newName)
        {
            Execute($"ALTER TABLE {QuoteIdentifier(collectionName)} RENAME COLUMN {QuoteIdentifier(oldName)} TO {QuoteIdentifier(newName)}");
        }

        private void EnsureExternalTransaction()
        {
            if (_connector != null)
                _connector.SetExternalTransaction(_connection, _transaction);
        }

        private void Execute(string sql)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private string QuoteIdentifier(string name)
        {
            if (_connector != null)
                return _connector.QuoteIdentifier(name);
            return $"\"{name}\"";
        }

        internal static string FieldTypeToSql(FieldDescriptor field)
        {
            return field.Type switch
            {
                FieldType.String => field.MaxLength.HasValue
                    ? $"VARCHAR({field.MaxLength.Value})"
                    : "TEXT",
                FieldType.Integer => "INTEGER",
                FieldType.Long => "BIGINT",
                FieldType.Decimal => field.Precision.HasValue && field.Scale.HasValue
                    ? $"DECIMAL({field.Precision.Value},{field.Scale.Value})"
                    : "DECIMAL",
                FieldType.Double => "DOUBLE",
                FieldType.Boolean => "BOOLEAN",
                FieldType.DateTime => "TIMESTAMP",
                FieldType.Guid => "UUID",
                FieldType.Binary => "BLOB",
                FieldType.Json => "TEXT",
                _ => "TEXT"
            };
        }

        private static string FormatValue(object value)
        {
            if (value is string s) return $"'{s.Replace("'", "''")}'";
            if (value is bool b) return b ? "TRUE" : "FALSE";
            if (value is DateTime dt) return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
            if (value is Guid g) return $"'{g}'";
            return value.ToString() ?? "NULL";
        }

        private class SqlCollectionBuilder : ICollectionBuilder
        {
            private readonly string _name;
            private readonly DbConnection _connection;
            private readonly DbTransaction? _transaction;
            private readonly AbstractConnector? _connector;
            private readonly List<FieldDescriptor> _fields = new();
            private readonly List<string> _primaryKeyFields = new();
            private bool _built;

            public SqlCollectionBuilder(string name, DbConnection connection, DbTransaction? transaction, AbstractConnector? connector)
            {
                _name = name;
                _connection = connection;
                _transaction = transaction;
                _connector = connector;
            }

            public ICollectionBuilder WithField(string name, FieldType type,
                bool isPrimary = false, bool isUnique = false,
                bool isRequired = false, int? maxLength = null,
                int? precision = null, int? scale = null,
                bool isAutoIncrement = false, object? defaultValue = null)
            {
                var descriptor = new FieldDescriptor
                {
                    Name = name,
                    Type = type,
                    IsPrimary = isPrimary,
                    IsUnique = isUnique,
                    IsRequired = isRequired,
                    MaxLength = maxLength,
                    Precision = precision,
                    Scale = scale,
                    IsAutoIncrement = isAutoIncrement,
                    DefaultValue = defaultValue
                };
                _fields.Add(descriptor);
                if (isPrimary)
                    _primaryKeyFields.Add(name);
                return this;
            }

            public ICollectionBuilder WithField(FieldDescriptor field)
            {
                _fields.Add(field);
                if (field.IsPrimary)
                    _primaryKeyFields.Add(field.Name);
                return this;
            }

            // Public so it satisfies ICollectionBuilder.Build() — the terminal a migration calls to
            // actually emit the CREATE TABLE. Previously internal + never invoked (CR-C14).
            public void Build()
            {
                if (_built) return;
                _built = true;

                if (_connector != null)
                {
                    _connector.SetExternalTransaction(_connection, _transaction);
                    var fieldDefinitions = _fields.Select(f =>
                    {
                        var schemaField = new SchemaField(f);
                        return _connector.FieldDefinition(schemaField);
                    });
                    _connector.CreateTable(_name, fieldDefinitions);
                    return;
                }

                // Fallback: raw SQL
                var sb = new System.Text.StringBuilder();
                sb.Append($"CREATE TABLE IF NOT EXISTS \"{_name}\" (");

                for (int i = 0; i < _fields.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(FormatColumn(_fields[i]));
                }

                if (_primaryKeyFields.Count > 0)
                {
                    sb.Append($", PRIMARY KEY ({string.Join(", ", _primaryKeyFields.ConvertAll(f => $"\"{f}\""))})");
                }

                sb.Append(")");

                using var command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandText = sb.ToString();
                command.ExecuteNonQuery();
            }

            private static string FormatColumn(FieldDescriptor field)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"\"{field.Name}\" {FieldTypeToSql(field)}");
                if (field.IsAutoIncrement) sb.Append(" AUTOINCREMENT");
                if (field.IsRequired) sb.Append(" NOT NULL");
                if (field.IsUnique && !field.IsPrimary) sb.Append(" UNIQUE");
                if (field.DefaultValue != null)
                    sb.Append($" DEFAULT {FormatValue(field.DefaultValue)}");
                return sb.ToString();
            }
        }

        private class SqlIndexBuilder : IIndexBuilder
        {
            private readonly string _collectionName;
            private readonly string _indexName;
            private readonly DbConnection _connection;
            private readonly DbTransaction? _transaction;
            private readonly AbstractConnector? _connector;
            private readonly List<(string Name, bool Descending)> _fields = new();
            private bool _unique;
            private bool _built;

            public SqlIndexBuilder(string collectionName, string indexName, DbConnection connection, DbTransaction? transaction, AbstractConnector? connector)
            {
                _collectionName = collectionName;
                _indexName = indexName;
                _connection = connection;
                _transaction = transaction;
                _connector = connector;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                // Validated HERE rather than in Build(), so a bad name fails at the declaration site and
                // covers both of Build()'s routes at once.
                //
                // TASK-249. This is the second caller-derived index-column sink, and it was missed when
                // TASK-245 made index columns be emitted BARE (required: a quoted column cannot resolve the
                // case-folded one PostgreSQL stores). `Build()`'s connector path puts this text straight into
                // Tables.IndexColumn.ColumnName and hands it to CreateIndexes -> CreateIndexSql, where
                // QuoteIdentifier had been incidentally containing it. Bare, a migration calling
                // WithField("Rank); CREATE TABLE Pwned (x INTEGER); --") emits and executes two statements —
                // the SH-H023 shape. The guard on SqlIndexManager.ToSqlIndexDefinition does not cover this
                // route: nothing here goes through that translator.
                _fields.Add((Birko.Data.SQL.DataBase.ValidateIndexFieldIdentifier(name), descending));
                return this;
            }

            public IIndexBuilder Unique()
            {
                _unique = true;
                return this;
            }

            public IIndexBuilder Sparse() => this;

            public IIndexBuilder WithProperty(string key, object value) => this;

            // Public so it satisfies IIndexBuilder.Build() — the terminal a migration calls to emit
            // the CREATE INDEX. Previously internal + never invoked (CR-C14).
            public void Build()
            {
                if (_built) return;
                _built = true;

                if (_fields.Count == 0)
                    throw new InvalidOperationException("Index must have at least one field.");

                if (_connector != null)
                {
                    _connector.SetExternalTransaction(_connection, _transaction);
                    var indexDef = new Birko.Data.SQL.Tables.IndexDefinition
                    {
                        Name = _indexName
                    };
                    indexDef.Columns.AddRange(_fields.Select((f, i) => new Birko.Data.SQL.Tables.IndexColumn
                    {
                        ColumnName = f.Name,
                        Order = i,
                        IsDescending = f.Descending
                    }));
                    _connector.CreateIndexes(_collectionName, new[] { indexDef });
                    return;
                }

                // Fallback: raw SQL
                var columns = _fields.Select(f => f.Descending ? $"\"{f.Name}\" DESC" : $"\"{f.Name}\" ASC");
                var uniqueStr = _unique ? "UNIQUE " : "";
                var sql = $"CREATE {uniqueStr}INDEX IF NOT EXISTS \"{_indexName}\" ON \"{_collectionName}\" ({string.Join(", ", columns)})";

                using var command = _connection.CreateCommand();
                command.Transaction = _transaction;
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
