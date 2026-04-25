using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.Stores;
using PostgreSqlSettings = Birko.Data.SQL.PostgreSQL.Stores.PostgreSqlSettings;
using Npgsql;
using NpgsqlTypes;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// PostgreSQL database connector.
    /// </summary>
    public partial class PostgreSQLConnector : AbstractAsyncConnector
    {
        /// <summary>
        /// Initializes a new instance of the PostgreSQLConnector class.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        public PostgreSQLConnector(RemoteSettings settings) : base(settings)
        {
            OnException += PostgreSQLConnector_OnException;
        }

        /// <summary>
        /// Detects PostgreSQL transient errors: deadlocks (40P01), serialization failures (40001),
        /// connection exceptions (08xxx), insufficient resources (53xxx), operator intervention (57xxx).
        /// </summary>
        public override bool IsTransientException(Exception ex)
        {
            if (base.IsTransientException(ex)) return true;
            if (ex is NpgsqlException npgsqlEx && npgsqlEx is PostgresException pgEx)
            {
                var code = pgEx.SqlState;
                if (code != null)
                {
                    // Class 08 — Connection Exception
                    if (code.StartsWith("08")) return true;
                    // Class 40 — Transaction Rollback (deadlock, serialization failure)
                    if (code.StartsWith("40")) return true;
                    // Class 53 — Insufficient Resources (disk full, out of memory, too many connections)
                    if (code.StartsWith("53")) return true;
                    // Class 57 — Operator Intervention (crash recovery, cannot connect now)
                    if (code.StartsWith("57")) return true;
                }
            }
            // Npgsql wrapper exceptions (broken connection)
            if (ex is NpgsqlException && ex.InnerException is System.IO.IOException) return true;
            return false;
        }

        private void PostgreSQLConnector_OnException(Exception ex, string? commandText)
        {
            if (!IsInitializing && ex.Message.Contains("does not exist"))
            {
                DoInit();
            }
            else
            {
                throw new Exception(commandText, ex);
            }
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Location) || string.IsNullOrEmpty(settings.Name))
            {
                throw new Exception("Invalid settings provided for PostgreSQL connection");
            }

            if (settings is PostgreSqlSettings pgSettings)
            {
                return new NpgsqlConnection(pgSettings.GetConnectionString());
            }

            if (settings is RemoteSettings remoteSettings)
            {
                var port = remoteSettings.Port > 0 ? remoteSettings.Port : 5432;
                var connectionString = string.Format("Host={0};Port={1};Username={2};Password={3};Database={4}",
                    remoteSettings.Location,
                    port,
                    remoteSettings.UserName,
                    remoteSettings.Password,
                    remoteSettings.Name);
                if (remoteSettings.UseSecure)
                {
                    connectionString += ";SSL Mode=Require";
                }
                return new NpgsqlConnection(connectionString);
            }

            throw new Exception("Invalid settings provided for PostgreSQL connection");
        }

        /// <inheritdoc />
        public override string ConvertType(DbType type, AbstractField field)
        {
            switch (type)
            {
                case DbType.VarNumeric:
                case DbType.Decimal:
                    if (field is DecimalField decimalField && decimalField.Precision != null && decimalField.Scale != null)
                    {
                        return string.Format("NUMERIC({0},{1})", decimalField.Precision, decimalField.Scale);
                    }
                    else
                    {
                        return "NUMERIC";
                    }
                case DbType.Double:
                    return "DOUBLE PRECISION";
                case DbType.Currency:
                    return "MONEY";
                case DbType.Boolean:
                    return "BOOLEAN";
                case DbType.Time:
                    return "TIME";
                case DbType.Date:
                    return "DATE";
                case DbType.DateTime:
                case DbType.DateTime2:
                    return "TIMESTAMP";
                case DbType.DateTimeOffset:
                    return "TIMESTAMPTZ";
                case DbType.Int16:
                case DbType.UInt16:
                    return "SMALLINT";
                case DbType.UInt32:
                case DbType.Int32:
                    return "INTEGER";
                case DbType.Int64:
                case DbType.UInt64:
                    return "BIGINT";
                case DbType.Single:
                case DbType.SByte:
                case DbType.Byte:
                    return "SMALLINT";
                case DbType.Object:
                case DbType.Binary:
                    return "BYTEA";
                case DbType.Guid:
                    return "UUID";
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                default:
                    if (field is CharField charField)
                    {
                        return string.Format("VARCHAR({0})", charField.Lenght);
                    }
                    else
                    {
                        return "TEXT";
                    }
            }
        }

        /// <inheritdoc />
        public override string FieldDefinition(AbstractField field)
        {
            var result = new StringBuilder();
            if (field != null)
            {
                result.Append(field.Name);
                result.AppendFormat(" {0}", ConvertType(field.Type, field));

                // Handle auto-increment with SERIAL types
                if (field.IsAutoincrement)
                {
                    if (field.Type == DbType.Int64 || field.Type == DbType.UInt64)
                    {
                        // Replace BIGINT with BIGSERIAL for auto-increment
                        var sqlType = result.ToString();
                        sqlType = sqlType.Replace("BIGINT", "BIGSERIAL");
                        result.Clear();
                        result.Append(sqlType);
                    }
                    else if (field.Type == DbType.Int32 || field.Type == DbType.UInt32)
                    {
                        // Replace INTEGER with SERIAL for auto-increment
                        var sqlType = result.ToString();
                        sqlType = sqlType.Replace("INTEGER", "SERIAL");
                        result.Clear();
                        result.Append(sqlType);
                    }
                    else if (field.Type == DbType.Int16 || field.Type == DbType.UInt16)
                    {
                        // Replace SMALLINT with SMALLSERIAL for auto-increment
                        var sqlType = result.ToString();
                        sqlType = sqlType.Replace("SMALLINT", "SMALLSERIAL");
                        result.Clear();
                        result.Append(sqlType);
                    }
                }

                if (field.IsPrimary)
                {
                    result.AppendFormat(" PRIMARY KEY");
                }
                if (field.IsUnique && !field.IsPrimary)
                {
                    result.AppendFormat(" UNIQUE");
                }
                if (field.IsNotNull)
                {
                    result.AppendFormat(" NOT NULL");
                }
            }
            return result.ToString();
        }

        /// <inheritdoc />
        public override DbCommand AddParameter(DbCommand command, string name, object? value)
        {
            if (command.Parameters.Contains(name))
            {
                ((NpgsqlParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
            }
            else
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value ?? DBNull.Value;
                command.Parameters.Add(parameter);
            }
            return command;
        }

        /// <inheritdoc />
        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            DoCommand((command) =>
            {
                command.CommandText =
                    "CREATE TABLE IF NOT EXISTS "
                    + QuoteIdentifier(name)
                    + " ("
                    + string.Join(", ", fields.Where(x => !string.IsNullOrEmpty(x)))
                    + ")";
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }

        #region Native Bulk Operations

        private static NpgsqlDbType DbTypeToNpgsqlDbType(DbType dbType)
        {
            return dbType switch
            {
                DbType.Boolean => NpgsqlDbType.Boolean,
                DbType.Byte or DbType.SByte => NpgsqlDbType.Smallint,
                DbType.Single => NpgsqlDbType.Real,
                DbType.Int16 or DbType.UInt16 => NpgsqlDbType.Smallint,
                DbType.Int32 or DbType.UInt32 => NpgsqlDbType.Integer,
                DbType.Int64 or DbType.UInt64 => NpgsqlDbType.Bigint,
                DbType.Decimal or DbType.VarNumeric or DbType.Currency => NpgsqlDbType.Numeric,
                DbType.Double => NpgsqlDbType.Double,
                DbType.Guid => NpgsqlDbType.Uuid,
                DbType.Date => NpgsqlDbType.Date,
                DbType.Time => NpgsqlDbType.Time,
                DbType.DateTime or DbType.DateTime2 => NpgsqlDbType.Timestamp,
                DbType.DateTimeOffset => NpgsqlDbType.TimestampTz,
                DbType.Binary or DbType.Object => NpgsqlDbType.Bytea,
                _ => NpgsqlDbType.Text,
            };
        }

        public void BulkInsert(Type type, IEnumerable<object> models)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var fields = table.Fields.Select(f => f.Value).Where(f => !f.IsAutoincrement).ToList();
            if (!fields.Any())
                return;

            var columnList = string.Join(", ", fields.Select(f => QuoteIdentifier(f.Name)));
            var copyCommand = "COPY " + QuoteIdentifier(table.Name)
                + " (" + columnList + ") FROM STDIN (FORMAT BINARY)";

            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            connection.Open();
            try
            {
                using var writer = connection.BeginBinaryImport(copyCommand);
                foreach (var model in models)
                {
                    writer.StartRow();
                    foreach (var field in fields)
                    {
                        var value = field.Write(model);
                        if (value == null)
                        {
                            writer.WriteNull();
                        }
                        else
                        {
                            writer.Write(value, DbTypeToNpgsqlDbType(field.Type));
                        }
                    }
                }
                writer.Complete();
            }
            catch (Exception ex)
            {
                InitException(ex, copyCommand);
            }
        }

        public async Task BulkInsertAsync(Type type, IEnumerable<object> models, CancellationToken ct = default)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var fields = table.Fields.Select(f => f.Value).Where(f => !f.IsAutoincrement).ToList();
            if (!fields.Any())
                return;

            var columnList = string.Join(", ", fields.Select(f => QuoteIdentifier(f.Name)));
            var copyCommand = "COPY " + QuoteIdentifier(table.Name)
                + " (" + columnList + ") FROM STDIN (FORMAT BINARY)";

            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            try
            {
                await using var writer = await connection.BeginBinaryImportAsync(copyCommand, ct).ConfigureAwait(false);
                foreach (var model in models)
                {
                    ct.ThrowIfCancellationRequested();
                    await writer.StartRowAsync(ct).ConfigureAwait(false);
                    foreach (var field in fields)
                    {
                        var value = field.Write(model);
                        if (value == null)
                        {
                            await writer.WriteNullAsync(ct).ConfigureAwait(false);
                        }
                        else
                        {
                            await writer.WriteAsync(value, DbTypeToNpgsqlDbType(field.Type), ct).ConfigureAwait(false);
                        }
                    }
                }
                await writer.CompleteAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                InitException(ex, copyCommand);
            }
        }

        public void BulkUpdate(Type type, IEnumerable<object> models)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            var allFields = table.Fields.Select(f => f.Value).ToList();
            var updateFields = allFields.Where(f => !f.IsPrimary && !f.IsAutoincrement).ToList();
            if (!updateFields.Any())
                return;

            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;

                var setClauses = updateFields.Select(f => f.Name + " = @SET_" + f.Name.Replace(".", ""));
                var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                command.CommandText = "UPDATE " + QuoteIdentifier(table.Name)
                    + " SET " + string.Join(", ", setClauses)
                    + " WHERE " + string.Join(" AND ", whereClauses);
                commandText = command.CommandText;

                foreach (var field in updateFields)
                {
                    command.Parameters.Add(new NpgsqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                foreach (var field in primaryFields)
                {
                    command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                command.Prepare();

                foreach (var model in models)
                {
                    foreach (var field in updateFields)
                    {
                        command.Parameters["@SET_" + field.Name.Replace(".", "")].Value = field.Write(model) ?? DBNull.Value;
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                    }
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                InitException(ex, commandText ?? "BulkUpdate " + table.Name);
            }
        }

        public async Task BulkUpdateAsync(Type type, IEnumerable<object> models, CancellationToken ct = default)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            var allFields = table.Fields.Select(f => f.Value).ToList();
            var updateFields = allFields.Where(f => !f.IsPrimary && !f.IsAutoincrement).ToList();
            if (!updateFields.Any())
                return;

            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = (NpgsqlTransaction)transaction;

                var setClauses = updateFields.Select(f => f.Name + " = @SET_" + f.Name.Replace(".", ""));
                var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                command.CommandText = "UPDATE " + QuoteIdentifier(table.Name)
                    + " SET " + string.Join(", ", setClauses)
                    + " WHERE " + string.Join(" AND ", whereClauses);
                commandText = command.CommandText;

                foreach (var field in updateFields)
                {
                    command.Parameters.Add(new NpgsqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                foreach (var field in primaryFields)
                {
                    command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                await command.PrepareAsync(ct).ConfigureAwait(false);

                foreach (var model in models)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var field in updateFields)
                    {
                        command.Parameters["@SET_" + field.Name.Replace(".", "")].Value = field.Write(model) ?? DBNull.Value;
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                    }
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                InitException(ex, commandText ?? "BulkUpdateAsync " + table.Name);
            }
        }

        public void BulkDelete(Type type, IEnumerable<object> models)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;

                var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                command.CommandText = "DELETE FROM " + QuoteIdentifier(table.Name)
                    + " WHERE " + string.Join(" AND ", whereClauses);
                commandText = command.CommandText;

                foreach (var field in primaryFields)
                {
                    command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                command.Prepare();

                foreach (var model in models)
                {
                    foreach (var field in primaryFields)
                    {
                        command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                    }
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                InitException(ex, commandText ?? "BulkDelete " + table.Name);
            }
        }

        public async Task BulkDeleteAsync(Type type, IEnumerable<object> models, CancellationToken ct = default)
        {
            if (models == null || !models.Any())
                return;

            var table = DataBase.LoadTable(type);
            if (table == null)
                return;

            var primaryFields = (table.GetPrimaryFields() ?? Enumerable.Empty<AbstractField>()).ToList();
            if (!primaryFields.Any())
                return;

            using var connection = (NpgsqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = (NpgsqlTransaction)transaction;

                var whereClauses = primaryFields.Select(f => f.Name + " = @PK_" + f.Name.Replace(".", ""));
                command.CommandText = "DELETE FROM " + QuoteIdentifier(table.Name)
                    + " WHERE " + string.Join(" AND ", whereClauses);
                commandText = command.CommandText;

                foreach (var field in primaryFields)
                {
                    command.Parameters.Add(new NpgsqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                await command.PrepareAsync(ct).ConfigureAwait(false);

                foreach (var model in models)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var field in primaryFields)
                    {
                        command.Parameters["@PK_" + field.Name.Replace(".", "")].Value = field.Property.GetValue(model) ?? DBNull.Value;
                    }
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                InitException(ex, commandText ?? "BulkDeleteAsync " + table.Name);
            }
        }

        #endregion
    }
}
