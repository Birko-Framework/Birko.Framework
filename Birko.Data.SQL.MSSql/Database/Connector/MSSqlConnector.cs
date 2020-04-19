using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;

namespace Birko.Data.SQL.Connectors
{
    public class MSSqlConnector : AbstractConnector
    {
        public MSSqlConnector(Data.Stores.RemoteSettings settings) : base(settings)
        {
            OnException += MSSqlConnector_OnException;
        }

        private void MSSqlConnector_OnException(Exception ex, string commandText)
        {
            if (!IsInit && ex.Message.Contains("Invalid object name"))
            {
                DoInit();
            }
            else
            {
                throw new Exception(commandText, ex);
            }
        }


        public override DbConnection CreateConnection(Stores.PasswordSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.Location) && !string.IsNullOrEmpty(settings.Name) && !string.IsNullOrEmpty(settings.Location) && settings is Stores.RemoteSettings remotesettings)
            {

                var connection = new SqlConnection(string.Format("Server=tcp:{0},{4};Initial Catalog={1};Persist Security Info=False;User ID={2};Password={3};MultipleActiveResultSets=False;Encrypt=True;", new object[] {
                    remotesettings.Location,
                    remotesettings.Name,
                    remotesettings.UserName,
                    remotesettings.Password,
                    remotesettings.Port
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

        public override DbCommand AddParameter(DbCommand command, string name, object value)
        {
            if (command.Parameters.Contains(name))
            {
                (command.Parameters[name] as SqlParameter).Value = value ?? DBNull.Value;
            }
            else
            {
                (command as SqlCommand).Parameters.AddWithValue(name, value ?? DBNull.Value);
            }
            return command;
        }

        public override void CreateTable(string name, IEnumerable<string> fields)
        {
            DoCommand((command) =>
            {
                command.CommandText =
                    "IF NOT EXISTS (SELECT * FROM sys.tables WHERE name='" + name +"') "
                    + "CREATE TABLE "
                    + name
                    + " ("
                    + string.Join(", ", fields.Where(x => !string.IsNullOrEmpty(x)))
                    + ")";
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);
        }
    }
}
