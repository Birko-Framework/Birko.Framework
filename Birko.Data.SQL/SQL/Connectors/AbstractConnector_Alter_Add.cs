using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        public void AlterTableAdd(Type type, IEnumerable<Fields.AbstractField> fields)
        {
            AlterTableAdd(DataBase.LoadTable(type), fields);
        }

        public void AlterTableAdd(Tables.Table table, IEnumerable<Fields.AbstractField> fields)
        {
            if (table != null && fields != null && fields.Any())
            {
                AlterTableAdd(table.Name, fields);
            }
        }

        public void AlterTableAdd(string tableName, IEnumerable<Fields.AbstractField> fields)
        {
            if (!string.IsNullOrEmpty(tableName) && fields != null && fields.Any())
            {
                foreach (var field in fields.Where(x => x != null))
                {
                    DoDdlCommand((command) => {
                        command.CommandText = AddColumnSql(tableName, field);
                    },  (command) => {
                        command.ExecuteNonQuery();
                    }, true);
                }
            }
        }

        /// <summary>
        /// The one <c>ALTER TABLE … ADD</c> statement, shared by the sync and async paths (TASK-510).
        /// </summary>
        protected string AddColumnSql(string tableName, Fields.AbstractField field)
            => "ALTER TABLE " + QuoteIdentifier(tableName) + " " + AddColumnClause + " " + AddColumnDefinition(field);
    }
}
