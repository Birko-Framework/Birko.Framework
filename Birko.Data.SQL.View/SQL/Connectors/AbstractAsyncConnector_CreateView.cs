using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.SQL.Connectors
{
    public abstract partial class AbstractAsyncConnector
    {
        /// <summary>
        /// Creates a persistent SQL VIEW from a view type decorated with ViewAttribute.
        /// </summary>
        /// <param name="viewType">The type decorated with ViewAttribute(s) and ViewFieldAttribute(s).</param>
        /// <param name="viewName">Optional custom view name. If null, uses the View.Name from metadata.</param>
        /// <param name="ct">Cancellation token.</param>
        public Task CreateViewAsync(Type viewType, string? viewName = null, CancellationToken ct = default)
        {
            var view = DataBase.LoadView(viewType);
            if (view == null || view.Tables == null || !view.Tables.Any())
            {
                throw new InvalidOperationException($"Type '{viewType.Name}' does not have valid view attributes.");
            }

            return CreateViewAsync(view, viewName, ct);
        }

        /// <summary>
        /// Creates persistent SQL VIEWs from view types.
        /// </summary>
        public async Task CreateViewsAsync(IEnumerable<Type> viewTypes, CancellationToken ct = default)
        {
            if (viewTypes == null) return;

            foreach (var viewType in viewTypes)
            {
                await CreateViewAsync(viewType, null, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Creates a persistent SQL VIEW from a View metadata object.
        /// </summary>
        public async Task CreateViewAsync(Tables.View view, string? viewName = null, CancellationToken ct = default)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            var name = viewName ?? view.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty. Provide a viewName parameter or set View.Name.");
            }

            var selectSql = BuildViewSelectSql(view);

            await DoCommandWithTransactionAsync(async (command) =>
            {
                command.CommandText = BuildCreateViewSql(name!, selectSql);
                await Task.CompletedTask;
            }, async (command) =>
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, true).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops a persistent SQL VIEW by type.
        /// </summary>
        public Task DropViewAsync(Type viewType, string? viewName = null, CancellationToken ct = default)
        {
            var view = DataBase.LoadView(viewType);
            var name = viewName ?? view?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty.");
            }

            return DropViewAsync(name!, ct);
        }

        /// <summary>
        /// Drops a persistent SQL VIEW by name.
        /// </summary>
        public async Task DropViewAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName)) throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));

            await DoCommandWithTransactionAsync(async (command) =>
            {
                command.CommandText = "DROP VIEW IF EXISTS " + QuoteIdentifier(viewName);
                await Task.CompletedTask;
            }, async (command) =>
            {
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }, true).ConfigureAwait(false);
        }

        /// <summary>
        /// Drops multiple views by name.
        /// </summary>
        public async Task DropViewsAsync(IEnumerable<string> viewNames, CancellationToken ct = default)
        {
            if (viewNames == null) return;

            foreach (var name in viewNames.Where(x => !string.IsNullOrEmpty(x)))
            {
                await DropViewAsync(name, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Drops and recreates a persistent SQL VIEW from a view type.
        /// </summary>
        public async Task RecreateViewAsync(Type viewType, string? viewName = null, CancellationToken ct = default)
        {
            await DropViewAsync(viewType, viewName, ct).ConfigureAwait(false);
            await CreateViewAsync(viewType, viewName, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if a SQL VIEW exists in the database.
        /// Override in database-specific connectors for optimal implementation.
        /// </summary>
        public virtual async Task<bool> ViewExistsAsync(string viewName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(viewName)) throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));

            try
            {
                await DoCommandAsync(async (command) =>
                {
                    command.CommandText = "SELECT 1 FROM " + QuoteIdentifier(viewName) + " WHERE 1=0";
                    await Task.CompletedTask;
                }, async (command) =>
                {
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }).ConfigureAwait(false);
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
        public async Task CreateViewIfNotExistsAsync(Type viewType, string? viewName = null, CancellationToken ct = default)
        {
            var view = DataBase.LoadView(viewType);
            var name = viewName ?? view?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("View name cannot be empty.");
            }

            if (!await ViewExistsAsync(name!, ct).ConfigureAwait(false))
            {
                await CreateViewAsync(viewType, viewName, ct).ConfigureAwait(false);
            }
        }
    }
}
