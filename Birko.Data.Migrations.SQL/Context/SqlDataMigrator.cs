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

        /// <summary>A collection's relation, schema included (TASK-272).</summary>
        private string TableIdentifier(string name)
            => _connector.TableIdentifier(name);

        /// <summary>
        /// A column of <paramref name="collection"/>, bare: the mapped property's stored column, else a validated plain
        /// identifier (TASK-637). Quoted, a model's property name missed PostgreSQL's folded column.
        /// </summary>
        private static Func<string, string> Columns(string collection)
            => name => Birko.Data.SQL.DataBase.MigrationColumn(collection, name);

        public void UpdateDocuments(string collection, string filterJson, IDictionary<string, object> updates)
        {
            if (updates == null || updates.Count == 0) return;

            var paramIndex = 0;
            var parameters = new List<(string Name, object? Value)>();
            var setClauses = new List<string>();

            foreach (var kvp in updates)
            {
                var paramName = $"@p{paramIndex++}";
                setClauses.Add($"{Birko.Data.SQL.DataBase.MigrationColumn(collection, kvp.Key)} = {paramName}");
                parameters.Add((paramName, kvp.Value));
            }

            var whereClause = ParseFilterToWhere(filterJson, ref paramIndex, parameters, Columns(collection));
            // SH-H032: an empty clause means either "no filter" or "every term was dropped" (e.g.
            // {"status":{}} takes the object branch and the operator loop adds nothing). Only the first is
            // a deliberate match-all; the second used to rewrite every row.
            MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0, "update", collection,
                "every row in the table");
            var sql = $"UPDATE {TableIdentifier(collection)} SET {string.Join(", ", setClauses)}";
            if (!string.IsNullOrEmpty(whereClause))
                sql += $" WHERE {whereClause}";

            ExecuteCommand(sql, parameters);
        }

        public void DeleteDocuments(string collection, string filterJson)
        {
            var paramIndex = 0;
            var parameters = new List<(string Name, object? Value)>();
            var whereClause = ParseFilterToWhere(filterJson, ref paramIndex, parameters, Columns(collection));
            // SH-H032 — see UpdateDocuments. This path used to emit a bare DELETE FROM {table}.
            MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0, "delete", collection,
                "every row in the table");

            var sql = $"DELETE FROM {TableIdentifier(collection)}";
            if (!string.IsNullOrEmpty(whereClause))
                sql += $" WHERE {whereClause}";

            ExecuteCommand(sql, parameters);
        }

        public long CountDocuments(string collection, string? filterJson = null)
        {
            var paramIndex = 0;
            var parameters = new List<(string Name, object? Value)>();
            var sql = $"SELECT COUNT(*) FROM {TableIdentifier(collection)}";

            if (!string.IsNullOrEmpty(filterJson))
            {
                var whereClause = ParseFilterToWhere(filterJson, ref paramIndex, parameters, Columns(collection));
                // SH-H032: the read is guarded too, so a count cannot silently answer for the whole table
                // while the delete built from the same filter is refused (§ TASK-215, § TASK-313).
                MigrationFilter.RequireBounded(filterJson, whereClause.Length > 0, "count", collection,
                    "every row in the table");
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
            MigrationFilter.RefuseTransform(transformJson, "SQL"); // SH-M196 (TASK-152): it was never read
            ExecuteCommand($"INSERT INTO {TableIdentifier(targetCollection)} SELECT * FROM {TableIdentifier(sourceCollection)}");
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
                    columns.Add(Birko.Data.SQL.DataBase.MigrationColumn(collection, kvp.Key));
                    parameters.Add((paramName, kvp.Value));
                }

                var values = string.Join(", ", parameters.ConvertAll(p => p.Name));
                var sql = $"INSERT INTO {TableIdentifier(collection)} ({string.Join(", ", columns)}) VALUES ({values})";
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

            // TASK-152: terms from the one shared reader (SH-M175: an unknown operator was equality; SH-M176: null was
            // compared with = / <>, which matches nothing). NotEqual is null-safe, as the filter's dialect defines it.
            var conditions = new List<string>();
            foreach (var term in MigrationFilterTerms.Parse(filterJson))
            {
                var column = quoteIdentifier(term.Field);
                if (term.Value == null)
                {
                    conditions.Add(term.Operator == MigrationFilterOperator.Equal ? $"{column} IS NULL" : $"{column} IS NOT NULL");
                    continue;
                }

                var paramName = $"@p{paramIndex++}";
                parameters.Add((paramName, term.Value));
                conditions.Add(term.Operator switch
                {
                    MigrationFilterOperator.Equal => $"{column} = {paramName}",
                    MigrationFilterOperator.NotEqual => $"({column} <> {paramName} OR {column} IS NULL)",
                    MigrationFilterOperator.GreaterThan => $"{column} > {paramName}",
                    MigrationFilterOperator.GreaterThanOrEqual => $"{column} >= {paramName}",
                    MigrationFilterOperator.LessThan => $"{column} < {paramName}",
                    _ => $"{column} <= {paramName}",
                });
            }

            return string.Join(" AND ", conditions);
        }

    }
}
