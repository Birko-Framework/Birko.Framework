using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnectorBase
    {
        /// <summary>
        /// Cache for persistent view existence checks. Maps view name to whether the view exists.
        /// Thread-safe for concurrent access.
        /// </summary>
        private readonly ConcurrentDictionary<string, bool> _viewExistsCache = new();

        /// <summary>
        /// Checks whether a persistent view should be used based on the view's QueryMode
        /// and (for Auto mode) the cached view existence status.
        /// </summary>
        /// <param name="view">The view metadata.</param>
        /// <param name="checkViewExists">Function to check if the view exists in the database (used only for Auto mode).</param>
        /// <returns>True if the persistent view should be queried directly; false for on-the-fly SELECT.</returns>
        protected bool ShouldUsePersistentView(Tables.View view, Func<string, bool> checkViewExists)
        {
            if (view.QueryMode == ViewQueryMode.OnTheFly)
            {
                return false;
            }

            if (view.QueryMode == ViewQueryMode.Persistent)
            {
                return true;
            }

            // Auto mode: check cache, then database.
            var viewName = view.Name;
            if (string.IsNullOrEmpty(viewName))
            {
                return false;
            }

            // CR-M149: only cache a definitive positive result. ViewExists swallows every exception and
            // returns false, so a transient failure (connection blip, lock timeout) is indistinguishable
            // from "view absent" — caching that false would poison Auto mode permanently (the view is
            // never used again until ClearViewExistsCache) and also hide a view created after the first
            // probe. A false is therefore re-checked on the next query; only a true is memoized.
            if (_viewExistsCache.TryGetValue(viewName!, out var cached))
            {
                return cached;
            }

            var exists = checkViewExists(viewName!);
            if (exists)
            {
                _viewExistsCache[viewName!] = true;
            }
            return exists;
        }

        /// <summary>
        /// Creates a SELECT command targeting a persistent database VIEW (no joins needed).
        /// </summary>
        protected DbCommand CreatePersistentViewSelectCommand(
            DbCommand command,
            Tables.View view,
            IEnumerable<Conditions.Condition>? conditions = null,
            IDictionary<string, bool>? orderFields = null,
            int? limit = null,
            int? offset = null)
        {
            var viewName = view.Name;
            if (string.IsNullOrEmpty(viewName))
            {
                throw new InvalidOperationException("View name cannot be empty for persistent view query.");
            }

            var fields = view.GetPersistentViewSelectFields();
            if (fields == null || !fields.Any())
            {
                throw new InvalidOperationException("View must have at least one field.");
            }

            // TASK-209: columns BARE, view name QUOTED — "quote tables, never quote columns" (§ Conventions).
            // These columns are created by the view DDL's bare `AS <ViewProperty>` alias, so on PostgreSQL
            // they are folded; asking for QuoteIdentifier(f) made them case-sensitive and the read failed
            // with `column "Name" does not exist` on every PascalCase view property. Measured on 16.4.
            command.CommandText = "SELECT " + string.Join(", ", fields.Values)
                + " FROM " + QuoteIdentifier(viewName!);

            AddWhere(conditions, command);

            if (orderFields != null && orderFields.Any())
            {
                command.CommandText += " ORDER BY " + string.Join(", ", orderFields.Select(kvp =>
                    string.Format("{0} {1}", kvp.Key, kvp.Value ? "DESC" : "ASC")));
            }

            if (limit != null)
            {
                command.CommandText += LimitOffsetDefinition(command, limit, offset) ?? string.Empty;
            }

            return command;
        }

        /// <summary>
        /// Creates a SELECT COUNT(*) command targeting a persistent database VIEW.
        /// </summary>
        protected DbCommand CreatePersistentViewSelectCountCommand(
            DbCommand command,
            string viewName,
            IEnumerable<Conditions.Condition>? conditions = null)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new InvalidOperationException("View name cannot be empty for persistent view count query.");
            }

            command.CommandText = "SELECT COUNT(*) FROM " + QuoteIdentifier(viewName);

            AddWhere(conditions, command);

            return command;
        }

        /// <summary>
        /// Clears the cached view existence results. Call this after creating or dropping views
        /// to ensure Auto mode re-checks view existence.
        /// </summary>
        public void ClearViewExistsCache()
        {
            _viewExistsCache.Clear();
        }

        /// <summary>
        /// Removes a specific view name from the existence cache.
        /// </summary>
        public void InvalidateViewExistsCache(string viewName)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                return;
            }

            _viewExistsCache.TryRemove(viewName, out _);
        }
        /// <summary>
        /// Builds the CREATE VIEW SQL statement.
        /// Override in database-specific connectors for syntax differences.
        /// Default uses CREATE OR REPLACE VIEW (PostgreSQL, MySQL compatible).
        /// </summary>
        protected virtual string BuildCreateViewSql(string viewName, string selectSql)
        {
            return "CREATE OR REPLACE VIEW " + QuoteIdentifier(viewName) + " AS " + selectSql;
        }

        /// <summary>
        /// Builds the SELECT SQL that defines a view's body (no WHERE, ORDER BY, LIMIT, OFFSET).
        /// Used by both sync and async connectors for CREATE VIEW operations. Delegates to the shared
        /// <see cref="ViewSelectSqlBuilder"/> (CR-M140/CR-M151) so the connector, the provider-specific
        /// indexed/materialized-view builders and the migration generator share one implementation.
        /// Table names use the plain <see cref="QuoteIdentifier"/>; providers needing a qualified name
        /// (e.g. SQL Server SCHEMABINDING's two-part <c>[dbo].[Table]</c>) call the shared builder
        /// directly with a qualifying delegate for just that path.
        /// </summary>
        protected string BuildViewSelectSql(Tables.View view)
        {
            return ViewSelectSqlBuilder.BuildViewSelectSql(view, QuoteIdentifier);
        }
    }
}
