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
            // TASK-639: a persistent view's WHERE names its own columns, not the source ones.
            var terms = DataBase.ResolveViewConditions(view, conditions, usePersistent)?.ToList();

            // TASK-837: past the provider's limit the count is the sum of the chunks' counts (TASK-833's rules); a list that
            // cannot be split is refused here, before anything runs.
            var plan = PlanViewCountChunks(view, usePersistent, terms);
            plan.ThrowIfRefused();

            long count = 0;
            foreach (var chunk in plan.Statements(terms))
            {
                // TASK-724: no name test. A view's name comes from its tables when none is given, so only a view with no
                // tables is nameless, and it fails before here. The fallback this had counted on-the-fly with conditions
                // already resolved to the persistent view's columns (rule 53: removed, not kept).
                if (usePersistent)
                {
                    count += SelectCountPersistentView(view.Name!, chunk);
                    continue;
                }

                DoCommand(
                    command => { CreateViewCountCommand(command, view, chunk); },
                    command =>
                    {
                        var result = command.ExecuteScalar();
                        if (result != null && result != DBNull.Value) count += Convert.ToInt64(result);
                    });
            }
            return count;
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
