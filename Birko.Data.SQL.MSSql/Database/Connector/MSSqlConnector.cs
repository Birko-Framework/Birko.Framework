using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using PasswordSettings = Birko.Data.Stores.PasswordSettings;
using RemoteSettings = Birko.Data.Stores.RemoteSettings;

namespace Birko.Data.SQL.Connectors
{
    public class MSSqlConnector : AbstractConnector
    {
        public MSSqlConnector(Data.Stores.RemoteSettings settings) : base(settings)
        {
            OnException += MSSqlConnector_OnException;
        }

        private void MSSqlConnector_OnException(Exception ex, string? commandText)
        {
            if (!IsInitializing && ex.Message.Contains("Invalid object name"))
            {
                DoInit();
            }
            else
            {
                throw new Exception(commandText, ex);
            }
        }


        public override string QuoteIdentifier(string identifier)
        {
            return "[" + identifier.Replace("]", "]]") + "]";
        }

        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.Location) && !string.IsNullOrEmpty(settings.Name) && !string.IsNullOrEmpty(settings.Location) && settings is RemoteSettings remotesettings)
            {

                var connection = new SqlConnection(string.Format("Server=tcp:{0},{4};Initial Catalog={1};Persist Security Info=False;User ID={2};Password={3};MultipleActiveResultSets=False;Encrypt={5};", new object[] {
                    remotesettings.Location,
                    remotesettings.Name,
                    remotesettings.UserName,
                    remotesettings.Password,
                    remotesettings.Port,
                    remotesettings.UseSecure ? "True" : "False"
                }));
                return connection;
            }
            else
            {
                throw new Exception("No path provided");
            }
        }

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
                    return "FLOAT";
                case DbType.Currency:
                    return "Money";
                case DbType.Boolean:
                    return "BIT";
                case DbType.Time:
                case DbType.Date:
                case DbType.DateTime:
                    return "DATE";
                case DbType.DateTime2:
                    return "DATETIME2";
                case DbType.DateTimeOffset:
                    return "DateTimeOffset";
                case DbType.Int16:
                case DbType.UInt16:
                    return "SMALLINT";
                case DbType.UInt32:
                case DbType.Int32:
                    return "INT";
                case DbType.Int64:
                case DbType.UInt64:
                    return "BIGINT";
                case DbType.Single:
                case DbType.SByte:
                case DbType.Byte:
                    return "TINYINT";
                case DbType.Xml:
                    return "XML";
                case DbType.Object:
                case DbType.Binary:
                    return "BINARY";
                case DbType.Guid:
                    return "UNIQUEIDENTIFIER";
                case DbType.String:
                case DbType.StringFixedLength:
                case DbType.AnsiString:
                case DbType.AnsiStringFixedLength:
                default:
                    if (field is CharField charField)
                    {
                        return string.Format("NVARCHAR({0})", charField.Lenght);
                    }
                    else
                    {
                        return "TEXT";
                    }
            }
        }

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
                    result.AppendFormat(" IDENTITY(1,1)");
                }
            }
            return result.ToString();
        }

        public override string LimitOffsetDefinition(DbCommand command, int? limit = null, int? offset = null)
        {
            var result = new StringBuilder();
            if (limit != null)
            {
                if (offset != null)
                {
                    result.Append(" OFFSET @OFFSET ROWS");
                    AddParameter(command, "@OFFSET", offset.Value);
                }
                result.Append(" FETCH NEXT @LIMIT ROWS ONLY");
                AddParameter(command, "@LIMIT", limit.Value);
            }
            return result.ToString();
        }

        public override DbCommand AddParameter(DbCommand command, string name, object? value)
        {
            if (command.Parameters.Contains(name))
            {
                ((SqlParameter)command.Parameters[name]).Value = value ?? DBNull.Value;
            }
            else
            {
                ((SqlCommand)command).Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
            return command;
        }

        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            DoCommand((command) =>
            {
                command.CommandText =
                    "IF NOT EXISTS (SELECT * FROM sys.tables WHERE name='" + name.Replace("'", "''") +"') "
                    + "CREATE TABLE "
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
            var dataTable = BuildDataTable(table.Name, fields, models);

            using var connection = (SqlConnection)CreateConnection(_settings);
            connection.Open();
            try
            {
                using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null);
                bulkCopy.DestinationTableName = QuoteIdentifier(table.Name);
                foreach (DataColumn col in dataTable.Columns)
                {
                    bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                }
                bulkCopy.WriteToServer(dataTable);
            }
            catch (Exception ex)
            {
                InitException(ex, "BulkInsert into " + table.Name);
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
            var dataTable = BuildDataTable(table.Name, fields, models);

            using var connection = (SqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            try
            {
                using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null);
                bulkCopy.DestinationTableName = QuoteIdentifier(table.Name);
                foreach (DataColumn col in dataTable.Columns)
                {
                    bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                }
                await bulkCopy.WriteToServerAsync(dataTable, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InitException(ex, "BulkInsertAsync into " + table.Name);
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

            using var connection = (SqlConnection)CreateConnection(_settings);
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
                    command.Parameters.Add(new SqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                foreach (var field in primaryFields)
                {
                    command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

            using var connection = (SqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
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
                    command.Parameters.Add(new SqlParameter("@SET_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                foreach (var field in primaryFields)
                {
                    command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                command.Prepare();

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

            using var connection = (SqlConnection)CreateConnection(_settings);
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
                    command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
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

            using var connection = (SqlConnection)CreateConnection(_settings);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
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
                    command.Parameters.Add(new SqlParameter("@PK_" + field.Name.Replace(".", ""), DBNull.Value));
                }
                command.Prepare();

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

        private DataTable BuildDataTable(string tableName, IList<AbstractField> fields, IEnumerable<object> models)
        {
            var dataTable = new DataTable(tableName);
            foreach (var field in fields)
            {
                var colType = DbTypeToClrType(field.Type);
                dataTable.Columns.Add(field.Name, colType);
            }

            foreach (var model in models)
            {
                var row = dataTable.NewRow();
                foreach (var field in fields)
                {
                    var value = field.Write(model);
                    row[field.Name] = value ?? DBNull.Value;
                }
                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        #endregion
    }
}
