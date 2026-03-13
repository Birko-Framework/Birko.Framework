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

            if (view.Join == null)
            {
                throw new ArgumentNullException(nameof(view.Join));
            }

            var leftTables = view.Join?.Select(x => x.Left).Distinct().Where(x => !string.IsNullOrEmpty(x)).ToList();
            if (leftTables != null)
            {
                foreach (var tableName in view.Join!.Select(x => x.Right).Distinct().Where(x => !string.IsNullOrEmpty(x)))
                {
                    leftTables.Remove(tableName);
                }
            }
            return CreateSelectCommand(command, leftTables ?? view.Tables.Select(x => x.Name), view.GetSelectFields(), view.Join, conditions, view.HasAggregateFields() ? view.GetSelectFields(true) : null, orderFields, limit, offset);
        }
    }
}
