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
        /// The table <paramref name="type"/> maps to, refused if any of its columns is a name this provider rejects
        /// (TASK-872). The one place the typed connector methods (<c>Select</c>, <c>SelectCount</c>, <c>Insert</c>,
        /// <c>Update</c>, <c>Delete</c>, <c>DeleteAll</c>, <c>SelectAggregate</c>, sync and async, taking a
        /// <see cref="Type"/>) resolve a type.
        /// </summary>
        /// <remarks>
        /// A store refuses such a type in its initialisation, but a direct connector call on a table Birko did not build
        /// never runs that. Measured live on all four providers: a write's WHERE emits the column bare, so
        /// <c>WHERE User = 'postgres'</c> compared <c>current_user</c> (PostgreSQL; <c>USER</c> on SQL Server,
        /// <c>CURRENT_USER</c> on MySQL, <c>CURRENT_DATE</c> on SQLite) and a filtered delete or update hit 2 of 2 rows
        /// that matched nothing in C#. Not virtual (rule 12). The overloads that take a <see cref="Tables.Table"/> check it
        /// through <see cref="RequireUsableTable"/>, and those that take a table name through
        /// <see cref="RequireUsableStatementColumns"/> (TASK-891).
        /// </remarks>
        /// <exception cref="NotSupportedException">A column name is one this provider rejects.</exception>
        protected Tables.Table LoadUsableTable(Type type)
        {
            var table = DataBase.LoadTable(type);
            RequireUsableTable(table);
            return table;
        }

        /// <summary>
        /// Refuses <paramref name="table"/> if any of its columns is a name this provider rejects: the check
        /// <see cref="LoadUsableTable"/> runs, for the public overloads that take a resolved <see cref="Tables.Table"/>
        /// (<c>Delete</c>, <c>Insert</c>, <c>Update</c>, <c>Select</c>, <c>SelectCount</c>, sync and async; TASK-891).
        /// </summary>
        /// <remarks>
        /// TASK-872 left these unchecked, trusting the caller's resolution. Measured live on all four providers with
        /// <c>DataBase.LoadTable(t)</c> of an entity whose column is <c>User</c> (PostgreSQL, SQL Server), <c>CURRENT_USER</c>
        /// (MySQL) or <c>CURRENT_DATE</c> (SQLite): <c>Delete(table, filter)</c> deleted 2 of 2 rows and every
        /// <c>Update(table, …)</c> rewrote 2 of 2, where C# matches none. A null table is passed through, so an overload
        /// that answers a null table with nothing keeps doing so. Not virtual (rule 12).
        /// </remarks>
        /// <exception cref="NotSupportedException">A column name is one this provider rejects.</exception>
        protected void RequireUsableTable(Tables.Table? table)
        {
            if (table == null)
            {
                return;
            }
            RequireUsableColumnNames(table.Name, table.Fields.Values);
        }

        /// <summary>
        /// Refuses, before any SQL is sent, a statement on tables given by name that names a column this provider rejects:
        /// one it writes (<paramref name="columns"/>, the INSERT column list or the UPDATE SET targets) or one its filter
        /// compares (a condition's <see cref="Conditions.Condition.Name"/>, at any depth), bare or qualified by one of
        /// <paramref name="tableNames"/>. TASK-891.
        /// </summary>
        /// <remarks>
        /// The overloads that take a table name have no entity to check, so the statement's own names are checked, through
        /// the funnels every write, read and count by name runs (<c>Delete</c>, <c>Update</c>, <c>Insert</c>,
        /// <c>Select</c>, <c>SelectCount</c>, sync and async). A store's bulk update runs through the name overload, on an
        /// entity its initialisation already checked, so it passes. The parser qualifies a filter's columns
        /// (<c>T.User</c>) and a write strips its target's qualifier (<c>StripTargetTableQualifier</c>), so the server reads
        /// the bare name: measured live, <c>Update(name, …)</c> with the filter <c>User = 'postgres'</c> rewrote 2 of 2 rows
        /// on all four providers, and a count by name failed on PostgreSQL and SQL Server. A name qualified by another table
        /// and a value fragment are not refused. Not virtual (rule 12).
        /// </remarks>
        /// <exception cref="NotSupportedException">A column name is one this provider rejects.</exception>
        protected void RequireUsableStatementColumns(IReadOnlyCollection<string> tableNames, IEnumerable<string>? columns, IEnumerable<Conditions.Condition>? conditions)
        {
            if (ColumnNameRefusals == null || tableNames == null)
            {
                return;
            }
            var label = string.Join(", ", tableNames);
            foreach (var column in (columns ?? Enumerable.Empty<string>()).Concat(ConditionColumns(conditions)))
            {
                RequireUsableColumnName(label, Unqualified(column, tableNames));
            }
        }

        /// <summary><paramref name="name"/> without a qualifier that is one of <paramref name="tableNames"/>.</summary>
        private static string Unqualified(string name, IReadOnlyCollection<string> tableNames)
        {
            var dot = name.LastIndexOf('.');
            if (dot <= 0)
            {
                return name;
            }
            var qualifier = name.Substring(0, dot);
            return tableNames.Contains(qualifier, StringComparer.OrdinalIgnoreCase) ? name.Substring(dot + 1) : name;
        }

        /// <summary>The column names <paramref name="conditions"/> compare, at any depth; value fragments excluded.</summary>
        private static IEnumerable<string> ConditionColumns(IEnumerable<Conditions.Condition>? conditions)
        {
            if (conditions == null)
            {
                yield break;
            }
            foreach (var condition in conditions)
            {
                if (condition == null)
                {
                    continue;
                }
                if (condition.NameFragment == null && !string.IsNullOrEmpty(condition.Name))
                {
                    yield return condition.Name!;
                }
                foreach (var column in ConditionColumns(condition.SubConditions))
                {
                    yield return column;
                }
            }
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
