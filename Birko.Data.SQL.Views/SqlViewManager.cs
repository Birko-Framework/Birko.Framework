using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.Views;

namespace Birko.Data.SQL.Views;

/// <summary>
/// SQL implementation of <see cref="IViewManager"/>.
/// Uses the existing connector infrastructure for DDL operations.
/// </summary>
public class SqlViewManager : IViewManager
{
    private readonly AbstractConnector _connector;

    public SqlViewManager(AbstractConnector connector)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
    }

    public async Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (definition.QueryMode == Birko.Data.Views.ViewQueryMode.OnTheFly)
        {
            return;
        }

        var sqlView = SqlViewTranslator.Translate(definition);
        var viewName = definition.Name ?? sqlView.Name;

        if (string.IsNullOrEmpty(viewName))
        {
            throw new InvalidOperationException("View name is required for persistent views.");
        }

        // Use genuine async DDL (threads ct) when the connector supports it (CR-H096).
        if (_connector is AbstractAsyncConnector asyncConnector)
        {
            if (!await asyncConnector.ViewExistsAsync(viewName!, ct).ConfigureAwait(false))
            {
                await asyncConnector.CreateViewAsync(sqlView, viewName, ct).ConfigureAwait(false);
            }
            return;
        }

        if (!_connector.ViewExists(viewName!))
        {
            _connector.CreateView(sqlView, viewName);
        }
    }

    public async Task DropAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        if (_connector is AbstractAsyncConnector asyncConnector)
        {
            await asyncConnector.DropViewAsync(viewName, ct).ConfigureAwait(false);
            return;
        }

        _connector.DropView(viewName);
    }

    public async Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        if (_connector is AbstractAsyncConnector asyncConnector)
        {
            return await asyncConnector.ViewExistsAsync(viewName, ct).ConfigureAwait(false);
        }

        return _connector.ViewExists(viewName);
    }

    public Task RefreshAsync(string viewName, CancellationToken ct = default)
    {
        // Standard SQL views are auto-maintained; no refresh needed.
        // Platform-specific implementations (PostgreSQL materialized, MSSql indexed)
        // can override this in derived classes.
        return Task.CompletedTask;
    }
}
