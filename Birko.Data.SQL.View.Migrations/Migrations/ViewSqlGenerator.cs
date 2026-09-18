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
        /// Generates a CREATE OR REPLACE VIEW SQL statement from a view type's metadata.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, derives from type metadata.</param>
        /// <param name="quoteChar">Quote character for identifiers. Defaults to ANSI SQL double quote.</param>
        /// <returns>The CREATE OR REPLACE VIEW SQL statement.</returns>
        public static string GenerateCreateViewSql(Type viewType, string? viewName = null, char quoteChar = DefaultQuoteChar)
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

            var selectSql = ViewSelectSqlBuilder.BuildViewSelectSql(view, id => QuoteIdentifier(id, quoteChar));
            return "CREATE OR REPLACE VIEW " + QuoteIdentifier(name!, quoteChar) + " AS " + selectSql;
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
