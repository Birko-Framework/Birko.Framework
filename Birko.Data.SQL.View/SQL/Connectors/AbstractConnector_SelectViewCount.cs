using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        public long SelectCountView(Type type, LambdaExpression expr)
        {
            return SelectCountView(type, (expr != null) ? DataBase.ParseConditionExpression(expr) : null);
        }

        public long SelectCountView(Type type, IEnumerable<Conditions.Condition>? conditions = null)
        {
            return SelectCount(DataBase.LoadView(type), conditions);
        }

        public long SelectCount(Tables.View view, LambdaExpression expr)
        {
            return SelectCount(view, (expr != null) ? DataBase.ParseConditionExpression(expr) : null);
        }

        public long SelectCount(Tables.View view, IEnumerable<Conditions.Condition>? conditions = null)
        {
            if (view == null)
            {
                return 0;
            }

            var usePersistent = ShouldUsePersistentView(view, name => ViewExists(name));

            if (usePersistent && !string.IsNullOrEmpty(view.Name))
            {
                return SelectCountPersistentView(view.Name!, conditions);
            }

            return SelectCount(view.Tables.Select(x => x.Name), view.Join, conditions);
        }

        private long SelectCountPersistentView(string viewName, IEnumerable<Conditions.Condition>? conditions = null)
        {
            long count = 0;
            DoCommand((command) =>
            {
                command = CreatePersistentViewSelectCountCommand(command, viewName, conditions);
            }, (command) =>
            {
                var result = command.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    count = Convert.ToInt64(result);
                }
            });
            return count;
        }
    }
}
