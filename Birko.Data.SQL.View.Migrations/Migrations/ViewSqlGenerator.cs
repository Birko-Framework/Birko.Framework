using Birko.Data.SQL;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using System;
using System.Linq;

namespace Birko.Data.SQL.View.Migrations
{
    /// <summary>
    /// Static utility for generating view-related SQL statements from view type metadata.
    /// Used by migration extensions to create/drop views without requiring a connector instance.
    /// </summary>
    public static class ViewSqlGenerator
    {
        /// <summary>
        /// Default quote character for SQL identifiers (ANSI SQL double quote).
        /// </summary>
        public const char DefaultQuoteChar = '"';

        /// <summary>
        /// Generates a <c>CREATE VIEW</c> statement from a view type's metadata. It creates; to replace a view, run
        /// <see cref="GenerateDropViewSql(Type, string?, char)"/> first, as <c>ViewMigrationExtensions.CreateView</c> does.
        /// TASK-717: this was <c>CREATE OR REPLACE VIEW</c>, which SQLite and SQL Server refuse (measured on SQLite:
        /// <c>near "OR": syntax error</c>); a plain create runs on every provider.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <param name="connector">
        /// The provider the view is created on. When given, a view whose own column (a view property) is a name this provider
        /// rejects is refused before any SQL is generated, through the connector's
        /// <see cref="AbstractConnectorBase.RequireUsableViewColumnNames"/>, the check its own view DDL runs (TASK-891).
        /// <c>ViewMigrationExtensions</c> always passes the migration's connector. Without one nothing is checked: the
        /// columns are emitted bare, so on PostgreSQL and SQLite a column named <c>User</c> or <c>CURRENT_DATE</c> is
        /// created and every bare read of it returns the function's value (measured).
        /// </param>
        /// <returns>The CREATE VIEW SQL statement.</returns>
        /// <exception cref="NotSupportedException">A view column name is one <paramref name="connector"/>'s provider rejects.</exception>
        public static string GenerateCreateViewSql(Type viewType, string? viewName = null, char quoteChar = DefaultQuoteChar, AbstractConnector? connector = null)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var view = DataBase.LoadView(viewType);
            if (view == null || view.Tables == null || !view.Tables.Any())
            {
                throw new InvalidOperationException($"Type '{viewType.Name}' does not have valid view attributes.");
            }

            var name = viewName ?? GetViewName(viewType);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty. Provide a viewName parameter or set ViewAttribute.Name.");
            }

            // TASK-272: this generator has no connector, so it cannot know how a provider stores a schema (a qualifier or a
            // schema__ prefix). A view over a table declaring one is refused rather than written against the default schema.
            // A table can be in the view's joins alone (no field selected from it), so both lists count.
            var scoped = view.Tables.Select(t => t.Name)
                .Concat((view.Join ?? Enumerable.Empty<Conditions.Join>()).SelectMany(j => new[] { j.Left, j.Right }))
                .Where(t => !string.IsNullOrEmpty(t) && DataBase.TableSchema(t) != null)
                .Distinct()
                .ToList();
            if (scoped.Count > 0)
            {
                throw new NotSupportedException(
                    $"View '{name}' reads {string.Join(", ", scoped)}, declared in a schema; this generator has no connector to render "
                    + "it. Create the view through the connector's CreateView instead.");
            }
            connector?.RequireUsableViewColumnNames(view);
            var selectSql = ViewSelectSqlBuilder.BuildViewSelectSql(view, id => QuoteIdentifier(id, quoteChar));
            return "CREATE VIEW " + QuoteIdentifier(name!, quoteChar) + " AS " + selectSql;
        }

        /// <summary>
        /// Generates a DROP VIEW IF EXISTS SQL statement from a view type's metadata.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers.</param>
        /// <returns>The DROP VIEW IF EXISTS SQL statement.</returns>
        public static string GenerateDropViewSql(Type viewType, string? viewName = null, char quoteChar = DefaultQuoteChar)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            var name = viewName ?? GetViewName(viewType);
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty. Provide a viewName parameter or set ViewAttribute.Name.");
            }

            return GenerateDropViewSql(name!, quoteChar);
        }

        /// <summary>
        /// Generates a DROP VIEW IF EXISTS SQL statement from a view name.
        /// </summary>
        /// <param name="viewName">The name of the view to drop.</param>
        /// <param name="quoteChar">Quote character for identifiers.</param>
        /// <returns>The DROP VIEW IF EXISTS SQL statement.</returns>
        public static string GenerateDropViewSql(string viewName, char quoteChar = DefaultQuoteChar)
        {
            if (string.IsNullOrWhiteSpace(viewName))
            {
                throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
            }

            return "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName, quoteChar);
        }

        /// <summary>
        /// Extracts the view name from ViewAttribute metadata or defaults to the type name.
        /// Checks the first ViewAttribute's Name property; if null, concatenates the underlying table names.
        /// Falls back to the type name if no tables are resolved.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s).</param>
        /// <returns>The resolved view name.</returns>
        public static string GetViewName(Type viewType)
        {
            if (viewType == null)
            {
                throw new ArgumentNullException(nameof(viewType));
            }

            // Check ViewAttribute.Name first
            var attrs = viewType.GetCustomAttributes(typeof(ViewAttribute), true)
                .OfType<ViewAttribute>()
                .ToArray();

            if (attrs.Length > 0)
            {
                var attrName = attrs[0].Name;
                if (!string.IsNullOrWhiteSpace(attrName))
                {
                    return attrName;
                }
            }

            // Try to derive from the loaded view's table names
            var view = DataBase.LoadView(viewType);
            if (view?.Tables != null && view.Tables.Any())
            {
                var concatenated = string.Join(string.Empty,
                    view.Tables.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct());
                if (!string.IsNullOrEmpty(concatenated))
                {
                    return concatenated;
                }
            }

            // Fall back to type name
            return viewType.Name;
        }

        /// <summary>
        /// Quotes a SQL identifier using the specified quote character.
        /// </summary>
        private static string QuoteIdentifier(string identifier, char quoteChar)
        {
            var quoteStr = quoteChar.ToString();
            return quoteStr + identifier.Replace(quoteStr, quoteStr + quoteStr) + quoteStr;
        }

    }
}
