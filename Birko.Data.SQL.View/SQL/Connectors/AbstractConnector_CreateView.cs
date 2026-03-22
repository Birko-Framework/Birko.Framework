using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractConnector
    {
        /// <summary>
        /// Creates a persistent SQL VIEW from a view type decorated with ViewAttribute.
        /// Uses CREATE OR REPLACE VIEW where supported, otherwise DROP + CREATE.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, uses the View.Name from metadata.</param>
        public void CreateView(Type viewType, string? viewName = null)
        {
            var view = DataBase.LoadView(viewType);
            if (view == null || view.Tables == null || !view.Tables.Any())
            {
                throw new InvalidOperationException($"Type '{viewType.Name}' does not have valid view attributes.");
            }

            CreateView(view, viewName);
        }

        /// <summary>
        /// Creates persistent SQL VIEWs from view types.
        /// </summary>
        public void CreateViews(IEnumerable<Type> viewTypes)
        {
            if (viewTypes == null) return;

            foreach (var viewType in viewTypes)
            {
                CreateView(viewType);
            }
        }

        /// <summary>
        /// Creates a persistent SQL VIEW from a View metadata object.
        /// </summary>
        public void CreateView(Tables.View view, string? viewName = null)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var name = viewName ?? view.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty. Provide a viewName parameter or set View.Name.");
            }

            var selectSql = BuildViewSelectSql(view);

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = BuildCreateViewSql(name!, selectSql);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);

            InvalidateViewExistsCache(name!);
        }

        /// <summary>
        /// Drops a persistent SQL VIEW by type.
        /// </summary>
        public void DropView(Type viewType, string? viewName = null)
        {
            var view = DataBase.LoadView(viewType);
            var name = viewName ?? view?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty.");
            }

            DropView(name!);
        }

        /// <summary>
        /// Drops a persistent SQL VIEW by name.
        /// </summary>
        public void DropView(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName)) throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));

            DoCommandWithTransaction((command) =>
            {
                command.CommandText = "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName);
            }, (command) =>
            {
                command.ExecuteNonQuery();
            }, true);

            InvalidateViewExistsCache(viewName);
        }

        /// <summary>
        /// Drops multiple views by name.
        /// </summary>
        public void DropViews(IEnumerable<string> viewNames)
        {
            if (viewNames == null) return;

            foreach (var name in viewNames.Where(x => !string.IsNullOrEmpty(x)))
            {
                DropView(name);
            }
        }

        /// <summary>
        /// Drops and recreates a persistent SQL VIEW from a view type.
        /// </summary>
        public void RecreateView(Type viewType, string? viewName = null)
        {
            DropView(viewType, viewName);
            CreateView(viewType, viewName);
        }

        /// <summary>
        /// Checks if a SQL VIEW exists in the database.
        /// Override in database-specific connectors for optimal implementation.
        /// </summary>
        public virtual bool ViewExists(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName)) throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));

            try
            {
                DoCommand((command) =>
                {
                    command.CommandText = "SELECT 1 FROM " + QuoteIdentifier(viewName) + " WHERE 1=0";
                }, (command) =>
                {
                    command.ExecuteNonQuery();
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a view only if it doesn't already exist.
        /// </summary>
        public void CreateViewIfNotExists(Type viewType, string? viewName = null)
        {
            var view = DataBase.LoadView(viewType);
            var name = viewName ?? view?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty.");
            }

            if (!ViewExists(name!))
            {
                CreateView(viewType, viewName);
            }
        }
    }
}
