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
using MySqlConnector;
using PasswordSettings = Birko.Data.Stores.PasswordSettings;
using RemoteSettings = Birko.Data.Stores.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    /// <summary>
    /// MySQL database connector.
    /// </summary>
    public class MySQLConnector : AbstractConnector
    {
        /// <summary>
        /// Initializes a new instance of the MySQLConnector class.
        /// </summary>
        /// <param name="settings">The remote settings for connection.</param>
        public MySQLConnector(RemoteSettings settings) : base(settings)
        {
            OnException += MySQLConnector_OnException;
        }

        private void MySQLConnector_OnException(Exception ex, string? commandText)
        {
            if (!IsInitializing && (ex.Message.Contains("doesn't exist") || ex.Message.Contains("Table") && ex.Message.Contains("doesn't exist")))
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
            if (settings != null && !string.IsNullOrEmpty(settings.Location) && !string.IsNullOrEmpty(settings.Name) && settings is RemoteSettings remoteSettings)
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
            else
            {
                throw new Exception("Invalid settings provided for MySQL connection");
            }
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

            using var connection = (MySqlConnection)CreateConnection(_settings);
            connection.Open();
            using var transaction = connection.BeginTransaction();
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;

                var columnNames = string.Join(", ", fields.Select(f => f.Name));
                var paramNames = string.Join(", ", fields.Select(f => "@INS_" + f.Name.Replace(".", "")));
                command.CommandText = "INSERT INTO " + QuoteIdentifier(table.Name)
                    + " (" + columnNames + ") VALUES (" + paramNames + ")";
                commandText = command.CommandText;

                foreach (var field in fields)
                {
                    command.Parameters.Add(new MySqlParameter("@INS_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                command.Prepare();

                foreach (var model in models)
                {
                    foreach (var field in fields)
                    {
                        command.Parameters["@INS_" + field.Name.Replace(".", "")].Value = field.Write(model) ?? DBNull.Value;
                    }
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                InitException(ex, commandText ?? "BulkInsert into " + table.Name);
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

            using var connection = (MySqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = (MySqlTransaction)transaction;

                var columnNames = string.Join(", ", fields.Select(f => f.Name));
                var paramNames = string.Join(", ", fields.Select(f => "@INS_" + f.Name.Replace(".", "")));
                command.CommandText = "INSERT INTO " + QuoteIdentifier(table.Name)
                    + " (" + columnNames + ") VALUES (" + paramNames + ")";
                commandText = command.CommandText;

                foreach (var field in fields)
                {
                    command.Parameters.Add(new MySqlParameter("@INS_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                await command.PrepareAsync(ct).ConfigureAwait(false);

                foreach (var model in models)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (var field in fields)
                    {
                        command.Parameters["@INS_" + field.Name.Replace(".", "")].Value = field.Write(model) ?? DBNull.Value;
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
                InitException(ex, commandText ?? "BulkInsertAsync into " + table.Name);
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

            using var connection = (MySqlConnection)CreateConnection(_settings);
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

            using var connection = (MySqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = (MySqlTransaction)transaction;

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

            using var connection = (MySqlConnection)CreateConnection(_settings);
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

            using var connection = (MySqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            string? commandText = null;
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = (MySqlTransaction)transaction;

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
