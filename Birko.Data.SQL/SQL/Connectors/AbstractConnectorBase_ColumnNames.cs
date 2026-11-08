using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnectorBase
    {
        /// <summary>
        /// The column names this provider rejects (TASK-862), or null where it rejects none. One producer per provider
        /// (rule 20): each provider overrides this once, and TimescaleDB inherits PostgreSQL's.
        /// </summary>
        /// <remarks>
        /// Column identifiers are emitted bare (rule 17), so a reserved word as a column name fails, or is read as a
        /// value function (<c>USER</c>, <c>CURRENT_DATE</c>) instead of the column. The user decided on 2026-11-08 to
        /// refuse such a name by name rather than to quote columns (TASK-862, option A).
        /// </remarks>
        protected virtual RejectedColumnNames? ColumnNameRefusals => null;

        /// <summary>Whether this provider rejects <paramref name="columnName"/> as a column name (TASK-862).</summary>
        public bool RejectsColumnName(string? columnName) => ColumnNameRefusals?.Contains(columnName) ?? false;

        /// <summary>
        /// Refuses, before any SQL is sent, a table whose columns include a name this provider rejects (TASK-862).
        /// </summary>
        /// <remarks>
        /// Called where a table's columns are built: <c>CreateTable</c> (which every store's lazy initialisation runs
        /// before its first operation, including against a table that already exists) and <c>AlterTableAdd</c>.
        /// Not virtual (rule 12). Thrown before the DDL wrapper, which re-wraps a callback's exception in a bare
        /// <see cref="Exception"/>, so a caller can select it by type.
        /// </remarks>
        /// <exception cref="NotSupportedException">A column name is one this provider rejects.</exception>
        public void RequireUsableColumnNames(string tableName, IEnumerable<Fields.AbstractField> fields)
        {
            var refusals = ColumnNameRefusals;
            if (refusals == null || fields == null)
            {
                return;
            }

            var rejected = fields.Where(f => f != null && refusals.Contains(f.Name)).ToList();
            if (rejected.Count == 0)
            {
                return;
            }

            throw new NotSupportedException(string.Join(" ", rejected.Select(f => DescribeRejected(refusals, tableName, f))));
        }

        /// <summary>
        /// Refuses a single column name this provider rejects, where no entity field exists (a migration's own column).
        /// </summary>
        /// <exception cref="NotSupportedException">The name is one this provider rejects.</exception>
        public void RequireUsableColumnName(string tableName, string columnName)
        {
            var refusals = ColumnNameRefusals;
            if (refusals == null || !refusals.Contains(columnName))
            {
                return;
            }

            throw new NotSupportedException(
                $"Column \"{columnName}\" of table \"{tableName}\" is a reserved word on {refusals.Provider}, which rejects it as a "
                + "column name, because Birko emits column names unquoted. Choose another column name.");
        }

        private static string DescribeRejected(RejectedColumnNames refusals, string tableName, Fields.AbstractField field)
        {
            PropertyInfo? property = field.Property;
            Type? entity = field.Table?.Type ?? property?.ReflectedType;
            if (entity == null || property == null)
            {
                return $"Column \"{field.Name}\" of table \"{tableName}\" is a reserved word on {refusals.Provider}, which rejects it "
                    + "as a column name, because Birko emits column names unquoted. Choose another column name.";
            }

            return $"Column \"{field.Name}\" of entity {entity.FullName} (property {property.Name}, table \"{tableName}\") is a "
                + $"reserved word on {refusals.Provider}, which rejects it as a column name, because Birko emits column names "
                + $"unquoted. Map the property to another column name: [NamedField(\"...\")] on the property, or "
                + $"Property(x => x.{property.Name}).HasColumnName(\"...\") in its ModelMap.";
        }
    }
}
