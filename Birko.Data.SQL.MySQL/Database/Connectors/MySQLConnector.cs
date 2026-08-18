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
using MySqlSettings = Birko.Data.SQL.MySQL.Stores.MySqlSettings;
using MySqlConnector;
using PasswordSettings = Birko.Configuration.PasswordSettings;
using RemoteSettings = Birko.Configuration.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// MySQL database connector.
    /// </summary>
    public partial class MySQLConnector : AbstractAsyncConnector
    {
        private const int DefaultBulkInsertBatchSize = 1000;

        /// <summary>
        /// Initializes a new instance of the MySQLConnector class.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        public MySQLConnector(RemoteSettings settings) : base(settings)
        {
            OnException += MySQLConnector_OnException;
        }

        /// <summary>
        /// Detects MySQL transient errors: too many connections (1040), lock wait timeout (1205),
        /// deadlock (1213), query interrupted (1317), can't-connect (2002/2003), server gone (2006),
        /// lost connection (2013).
        /// </summary>
        public override bool IsTransientException(Exception ex)
        {
            if (base.IsTransientException(ex)) return true;
            if (ex is MySqlException mysqlEx)
            {
                switch ((int)mysqlEx.ErrorCode)
                {
                    case 1040:  // Too many connections
                    case 1205:  // Lock wait timeout exceeded
                    case 1213:  // Deadlock found
                    case 1317:  // Query execution was interrupted
                    case 2002:  // Can't connect to local MySQL server
                    case 2003:  // Can't connect to MySQL server
                    case 2006:  // MySQL server has gone away
                    case 2013:  // Lost connection during query
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// MySQL phrases a missing table as "Table 'x' doesn't exist" (error 1146). Adds that to the base
        /// SQLite match so the reader yields an empty result rather than faulting.
        /// </summary>
        /// <remarks>
        /// TASK-211 dropped the bare <c>Message.Contains("doesn't exist")</c> catch-all that sat behind the
        /// error-code test. MySQL uses that phrasing for more than a missing table — <c>1054 Unknown column</c>
        /// aside, a missing routine reads <c>FUNCTION x doesn't exist</c> — and a reader that answers "no rows"
        /// to any of them turns an error into a plausible wrong answer. <c>1146 ER_NO_SUCH_TABLE</c> is the
        /// signal; the chain is walked because the driver wraps in some paths.
        /// </remarks>
        public override bool IsMissingTableException(Exception ex)
        {
            if (base.IsMissingTableException(ex)) return true;

            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is MySqlException mysqlEx) return (int)mysqlEx.ErrorCode == 1146;
            }

            // "Table 'db.widgets' doesn't exist" — the missing-TABLE wording, kept as the untyped fallback.
            // A missing routine reads "FUNCTION db.f does not exist", so requiring "table" is what separates
            // the error this may swallow from the ones it may not. (CR-L183 removed this pair from the
            // OnException handler as dead code — it was, THERE, because of `&&`/`||` precedence. Here it is
            // the whole test.)
            return ex.Message.Contains("table", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase);
        }

        private void MySQLConnector_OnException(Exception ex, string? commandText)
        {
            // The second operand — `Message.Contains("Table") && Message.Contains("doesn't exist")` — was
            // dead: && binds tighter than ||, so it can only be true when the first operand is already
            // true. Reduced to the single meaningful check (CR-L183).
            //
            // TASK-211: and that single check was still a message substring, so any "doesn't exist" error ran
            // DoInit() and RETURNED — reporting success for a statement that never ran. Now the same typed
            // test the reader uses, so the two cannot disagree about what "the table is missing" means.
            if (!IsInitializing && IsMissingTableException(ex))
            {
                DoInit();
            }
            else
            {
                throw new Exception(commandText, ex);
            }
        }

        /// <inheritdoc />
        public override string QuoteIdentifier(string identifier)
        {
            return "`" + identifier.Replace("`", "``") + "`";
        }

        /// <inheritdoc />
        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Location) || string.IsNullOrEmpty(settings.Name))
            {
                throw new Exception("Invalid settings provided for MySQL connection");
            }

            if (settings is MySqlSettings mySettings)
            {
                return new MySqlConnection(mySettings.GetConnectionString());
            }

            if (settings is RemoteSettings remoteSettings)
            {
                var port = remoteSettings.Port > 0 ? remoteSettings.Port : 3306;
                var connectionString = string.Format("Server={0};Port={1};User ID={2};Password={3};Database={4}",
                    remoteSettings.Location,
                    port,
                    remoteSettings.UserName,
                    remoteSettings.Password,
                    remoteSettings.Name);
                if (remoteSettings.UseSecure)
                {
                    connectionString += ";SslMode=Required";
                }
                return new MySqlConnection(connectionString);
            }

            throw new Exception("Invalid settings provided for MySQL connection");
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
                        return string.Format("DECIMAL({0},{1})", decimalField.Precision, decimalField.Scale);
                    }
                    else
                    {
                        return "DECIMAL";
                    }
                case DbType.Double:
                    return "DOUBLE";
                case DbType.Currency:
                    return "DECIMAL(19,4)";
                case DbType.Boolean:
                    return "TINYINT(1)";
                case DbType.Time:
                    return "TIME";
                case DbType.Date:
                    return "DATE";
                case DbType.DateTime:
                case DbType.DateTime2:
                    return "DATETIME";
                case DbType.DateTimeOffset:
                    return "DATETIME";
                case DbType.Int16:
                    return "SMALLINT";
                case DbType.UInt16:
                    return "SMALLINT UNSIGNED";
                case DbType.UInt32:
                    return "INT UNSIGNED";
                case DbType.Int32:
                    return "INT";
                case DbType.Int64:
                    return "BIGINT";
                case DbType.UInt64:
                    return "BIGINT UNSIGNED";
                case DbType.Single:
                    return "FLOAT";
                case DbType.SByte:
                    return "TINYINT";
                case DbType.Byte:
                    return "TINYINT UNSIGNED";
                case DbType.Object:
                case DbType.Binary:
                    return "LONGBLOB";
                case DbType.Guid:
                    return "CHAR(36)";
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
                        return "LONGTEXT";
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
                if (field.IsAutoincrement)
                {
                    result.AppendFormat(" AUTO_INCREMENT");
                }
            }
            return result.ToString();
        }

        /// <inheritdoc />
        public override DbCommand AddParameter(DbCommand command, string name, object? value)
        {
            // Enums persist as INTEGER (IntegerField) — bind the underlying integral value, never the
            // boxed enum, or the provider maps it to its own type and the comparison never matches.
            value = NormalizeParameterValue(value);
            if (command.Parameters.Contains(name))
            {
                ((MySqlParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
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

        // NOTE (CR-L185): if a bulk operation fails because the table does not yet exist, the transaction
        // is rolled back and InitException -> MySQLConnector_OnException creates the table (DoInit) but does
        // NOT re-run the bulk command, so the bulk payload is silently dropped (no exception, no retry).
        // This is the inherited framework auto-init behavior shared by the other SQL dialects — the
        // single-row CRUD path tolerates it, the bulk path does not. Callers must ensure the schema exists
        // (InitAsync / a prior single-row write / CreateTable) before the first bulk operation.

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

            var fieldCount = fields.Count;
            var batchSize = _settings is MySqlSettings mySettings ? mySettings.BulkInsertBatchSize : DefaultBulkInsertBatchSize;
            var maxBatchSize = Math.Min(batchSize, 65535 / fieldCount);
            if (maxBatchSize < 1)
                maxBatchSize = 1;

            var columnNames = string.Join(", ", fields.Select(f => QuoteIdentifier(f.Name)));
            var modelList = models as IList<object> ?? models.ToList();

            // A bulk write must JOIN an open boundary on this database rather than open a second connection.
            // On MySQL two connections are perfectly legal, so before this the statements committed on their
            // own transaction and SURVIVED the owner's rollback with no error anywhere — the quiet half of
            // the defect, and the one most likely to be in production. retryWhenOwned: false keeps the
            // own-connection path exactly as it shipped; it never retried and this fix is not the place to
            // start (see AbstractConnector.RunBulk).
            RunBulk("BulkInsert into " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    for (var batchStart = 0; batchStart < modelList.Count; batchStart += maxBatchSize)
                    {
                        var batchEnd = Math.Min(batchStart + maxBatchSize, modelList.Count);
                        var batchCount = batchEnd - batchStart;

                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;

                        var sb = new StringBuilder();
                        sb.Append("INSERT INTO ");
                        sb.Append(QuoteIdentifier(table.Name));
                        sb.Append(" (");
                        sb.Append(columnNames);
                        sb.Append(") VALUES ");

                        for (var rowIdx = 0; rowIdx < batchCount; rowIdx++)
                        {
                            if (rowIdx > 0)
                                sb.Append(", ");

                            sb.Append('(');
                            for (var fieldIdx = 0; fieldIdx < fieldCount; fieldIdx++)
                            {
                                if (fieldIdx > 0)
                                    sb.Append(", ");

                                var paramName = "@P" + rowIdx + "_" + fieldIdx;
                                sb.Append(paramName);

                                var model = modelList[batchStart + rowIdx];
                                command.Parameters.Add(new MySqlParameter(paramName, fields[fieldIdx].Write(model) ?? DBNull.Value));
                            }
                            sb.Append(')');
                        }

                        command.CommandText = sb.ToString();
                        commandText = command.CommandText;
                        command.ExecuteNonQuery();
                    }

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkInsert into " + table.Name);
                }
            }, retryWhenOwned: false);
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

            var fieldCount = fields.Count;
            var batchSize = _settings is MySqlSettings mySettings ? mySettings.BulkInsertBatchSize : DefaultBulkInsertBatchSize;
            var maxBatchSize = Math.Min(batchSize, 65535 / fieldCount);
            if (maxBatchSize < 1)
                maxBatchSize = 1;

            var columnNames = string.Join(", ", fields.Select(f => QuoteIdentifier(f.Name)));
            var modelList = models as IList<object> ?? models.ToList();

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            await RunBulkAsync("BulkInsertAsync into " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
                string? commandText = null;
                try
                {
                    for (var batchStart = 0; batchStart < modelList.Count; batchStart += maxBatchSize)
                    {
                        ct.ThrowIfCancellationRequested();

                        var batchEnd = Math.Min(batchStart + maxBatchSize, modelList.Count);
                        var batchCount = batchEnd - batchStart;

                        using var command = connection.CreateCommand();
                        command.Transaction = transaction;

                        var sb = new StringBuilder();
                        sb.Append("INSERT INTO ");
                        sb.Append(QuoteIdentifier(table.Name));
                        sb.Append(" (");
                        sb.Append(columnNames);
                        sb.Append(") VALUES ");

                        for (var rowIdx = 0; rowIdx < batchCount; rowIdx++)
                        {
                            if (rowIdx > 0)
                                sb.Append(", ");

                            sb.Append('(');
                            for (var fieldIdx = 0; fieldIdx < fieldCount; fieldIdx++)
                            {
                                if (fieldIdx > 0)
                                    sb.Append(", ");

                                var paramName = "@P" + rowIdx + "_" + fieldIdx;
                                sb.Append(paramName);

                                var model = modelList[batchStart + rowIdx];
                                command.Parameters.Add(new MySqlParameter(paramName, fields[fieldIdx].Write(model) ?? DBNull.Value));
                            }
                            sb.Append(')');
                        }

                        command.CommandText = sb.ToString();
                        commandText = command.CommandText;
                        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    }

                    if (owned) await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    InitException(ex, commandText ?? "BulkInsertAsync into " + table.Name);
                }
            }, ct, retryWhenOwned: false);
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            RunBulk("BulkUpdate " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkUpdate " + table.Name);
                }
            }, retryWhenOwned: false);
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            await RunBulkAsync("BulkUpdateAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                    }
                    foreach (var field in primaryFields)
                    {
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

                    if (owned) await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    InitException(ex, commandText ?? "BulkUpdateAsync " + table.Name);
                }
            }, ct, retryWhenOwned: false);
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            RunBulk("BulkDelete " + table.Name, (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

                    if (owned) transaction.Commit();
                }
                catch (Exception ex)
                {
                    if (owned) transaction.Rollback();
                    InitException(ex, commandText ?? "BulkDelete " + table.Name);
                }
            }, retryWhenOwned: false);
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

            // Joins an open boundary instead of opening a second connection — see BulkInsert above.
            await RunBulkAsync("BulkDeleteAsync " + table.Name, async (dbConnection, dbTransaction, owned) =>
            {
                var connection = (MySqlConnection)dbConnection;
                var transaction = (MySqlTransaction)dbTransaction;
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
                        command.Parameters.Add(new MySqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

                    if (owned) await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    if (owned) await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    InitException(ex, commandText ?? "BulkDeleteAsync " + table.Name);
                }
            }, ct, retryWhenOwned: false);
        }

        #endregion
    }
}
