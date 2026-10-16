using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        /// <summary>
        /// Creates a SELECT command from a View definition.
        /// </summary>
        public virtual DbCommand CreateSelectCommand(DbCommand command, Tables.View view, IEnumerable<Conditions.Condition>? conditions = null, IDictionary<string, bool>? orderFields = null, int? limit = null, int? offset = null)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            // TASK-151 (SH-M401): a view with no join (a single source) is a plain SELECT from its table. It threw
            // ArgumentNullException("Join"), so the minimal documented view could not be queried at all.
            return CreateSelectCommand(command, ViewFromTables(view), view.GetSelectFields(), view.Join, conditions, view.HasAggregateFields() ? view.GetSelectFields(true) : null, orderFields, limit, offset);
        }

        /// <summary>
        /// The tables a view's FROM lists: those no join brings in, or every table when there is no join.
        /// </summary>
        internal static IEnumerable<string> ViewFromTables(Tables.View view)
        {
            var joins = view.Join?.ToList();
            if (joins == null || joins.Count == 0)
            {
                return view.Tables.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct();
            }
            var leftTables = joins.Select(x => x.Left).Distinct().Where(x => !string.IsNullOrEmpty(x)).ToList();
            foreach (var tableName in joins.Select(x => x.Right).Distinct().Where(x => !string.IsNullOrEmpty(x)))
            {
                leftTables.Remove(tableName);
            }
            return leftTables;
        }

        /// <summary>
        /// The count of a view's rows: COUNT(*) over the view's own SELECT, so it counts what a query returns.
        /// </summary>
        /// <remarks>
        /// TASK-151 (SH-M404): the count was a COUNT(*) over the joined source tables with no GROUP BY, so a view grouping
        /// a million orders into 12 rows counted a million; and the sync path listed every view table in FROM besides
        /// the joins, which multiplied the count by the joined tables.
        /// </remarks>
        public virtual DbCommand CreateViewCountCommand(DbCommand command, Tables.View view, IEnumerable<Conditions.Condition>? conditions = null)
        {
            // The inner SELECT projects a constant, grouped as the view is: the view's own column list can repeat a
            // name (two joined tables' Guid), which SQL Server refuses in a derived table. One row per view row: per
            // group for a grouped view, a single row for an aggregate over everything, one per joined row otherwise.
            var groupFields = view.HasAggregateFields() ? view.GetSelectFields(true) : null;
            var wholeTableAggregate = view.HasAggregateFields() && (groupFields == null || groupFields.Count == 0);
            var fields = new Dictionary<int, string> { [0] = wholeTableAggregate ? "COUNT(*) AS birko_one" : "1 AS birko_one" };
            command = CreateSelectCommand(command, ViewFromTables(view), fields, view.Join, conditions,
                wholeTableAggregate ? null : groupFields, null, null, null);
            command.CommandText = "SELECT COUNT(*) FROM (" + command.CommandText + ") birko_view_count";
            return command;
        }
    }
}
