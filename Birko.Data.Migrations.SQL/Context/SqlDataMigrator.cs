using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Birko.Data.Migrations.Context;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL.Context
{
    public class SqlDataMigrator : IDataMigrator
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction? _transaction;
        private readonly AbstractConnector _connector;

        /// <summary>
        /// <paramref name="connector"/> is <b>required</b> (TASK-247) — same reasoning as
        /// <see cref="SqlSchemaBuilder"/>: identifier quoting is a dialect decision and the null fallback
        /// hardcoded ANSI double quotes, which is wrong on MySQL and case-wrong on PostgreSQL.
        /// </summary>
        public SqlDataMigrator(DbConnection connection, DbTransaction? transaction, AbstractConnector connector)
        {
            _connection = connection;
            _transaction = transaction;
            _connector = connector;
        }

        // CR-L150: route identifier quoting through the connector dialect (e.g. [brackets] on SQL Server)
        // instead of hardcoding ANSI double quotes, matching SqlSchemaBuilder.
        private string QuoteIdentifier(string name)
            => _connector.QuoteIdentifier(name);

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;

            var paramIndex = 0;
            var parameters = new List<(string Name, object? Value)>();
            var setClauses = new List<string>();

            foreach (var kvp in updates)
            {
                var paramName = $"@p{paramIndex++}";
                setClauses.Add($"{QuoteIdentifier(kvp.Key)} = {paramName}");
                parameters.Add((paramName, kvp.Value));
            }

            var whereClause = ParseFilterToWhere(filterJson, ref paramIndex, parameters, QuoteIdentifier);
            var sql = $"UPDATE {QuoteIdentifier(collection)} SET {string.Join(", ", setClauses)}";
            if (!string.IsNullOrEmpty(whereClause))
                sql += $" WHERE {whereClause}";

            ExecuteCommand(sql, parameters);
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var paramIndex = 0;
            var parameters = new List<(string Name, object? Value)>();
            var whereClause = ParseFilterToWhere(filterJson, ref paramIndex, parameters, QuoteIdentifier);

            var sql = $"DELETE FROM {QuoteIdentifier(collection)}";
            if (!string.IsNullOrEmpty(whereClause))
                sql += $" WHERE {whereClause}";

            ExecuteCommand(sql, parameters);
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            var paramIndex = 0;
            var parameters = new List<(string Name, object? Value)>();
            var sql = $"SELECT COUNT(*) FROM {QuoteIdentifier(collection)}";

            if (!string.IsNullOrEmpty(filterJson))
            {
                var whereClause = ParseFilterToWhere(filterJson, ref paramIndex, parameters, QuoteIdentifier);
                if (!string.IsNullOrEmpty(whereClause))
                    sql += $" WHERE {whereClause}";
            }

            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
                AddParameter(command, name, value);
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public void CopyData(string sourceCollection, string targetCollection, string? transformJson = null)
        {
            ExecuteCommand($"INSERT INTO {QuoteIdentifier(targetCollection)} SELECT * FROM {QuoteIdentifier(sourceCollection)}");
        }

        public void BulkInsert(string collection, IEnumerable<IDictionary<string, object>> documents)
        {
            if (documents == null) return;

            foreach (var doc in documents)
            {
                if (doc == null || doc.Count == 0) continue;

                var columns = new List<string>();
                var parameters = new List<(string Name, object? Value)>();
                var paramIndex = 0;

                foreach (var kvp in doc)
                {
                    var paramName = $"@p{paramIndex++}";
                    columns.Add(QuoteIdentifier(kvp.Key));
                    parameters.Add((paramName, kvp.Value));
                }

                var values = string.Join(", ", parameters.ConvertAll(p => p.Name));
                var sql = $"INSERT INTO {QuoteIdentifier(collection)} ({string.Join(", ", columns)}) VALUES ({values})";
                ExecuteCommand(sql, parameters);
            }
        }

        private void ExecuteCommand(string sql, List<(string Name, object? Value)>? parameters = null)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            if (parameters != null)
            {
                foreach (var (name, value) in parameters)
                    AddParameter(command, name, value);
            }
            command.ExecuteNonQuery();
        }

        private static void AddParameter(DbCommand command, string name, object? value)
        {
            var param = command.CreateParameter();
            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            command.Parameters.Add(param);
        }

        internal static string ParseFilterToWhere(string filterJson, ref int paramIndex, List<(string Name, object? Value)> parameters, Func<string, string>? quoteIdentifier = null)
        {
            // CR-L150: quote field identifiers via the caller's dialect-aware quoter; fall back to ANSI quotes.
            quoteIdentifier ??= id => $"\"{id}\"";

            if (string.IsNullOrWhiteSpace(filterJson) || filterJson.Trim() == "{}")
                return string.Empty;

            var conditions = new List<string>();
            using var doc = JsonDocument.Parse(filterJson);

            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var fieldName = property.Name;

                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var op in property.Value.EnumerateObject())
                    {
                        var sqlOp = op.Name switch
                        {
                            "$gt" => ">",
                            "$gte" => ">=",
                            "$lt" => "<",
                            "$lte" => "<=",
                            "$ne" => "<>",
                            _ => "="
                        };
                        var paramName = $"@p{paramIndex++}";
                        conditions.Add($"{quoteIdentifier(fieldName)} {sqlOp} {paramName}");
                        parameters.Add((paramName, ExtractValue(op.Value)));
                    }
                }
                else
                {
                    var paramName = $"@p{paramIndex++}";
                    conditions.Add($"{quoteIdentifier(fieldName)} = {paramName}");
                    parameters.Add((paramName, ExtractValue(property.Value)));
                }
            }

            return string.Join(" AND ", conditions);
        }

        private static object? ExtractValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }
    }
}
