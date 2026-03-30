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

    public Task EnsureAsync(ViewDefinition definition, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (definition.QueryMode == Birko.Data.Views.ViewQueryMode.OnTheFly)
        {
            return Task.CompletedTask;
        }

        var sqlView = SqlViewTranslator.Translate(definition);
        var viewName = definition.Name ?? sqlView.Name;

        if (string.IsNullOrEmpty(viewName))
        {
            throw new InvalidOperationException("View name is required for persistent views.");
        }

        if (!_connector.ViewExists(viewName!))
        {
            _connector.CreateView(sqlView, viewName);
        }

        return Task.CompletedTask;
    }

    public Task DropAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        _connector.DropView(viewName);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string viewName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            throw new ArgumentException("View name cannot be null or empty.", nameof(viewName));
        }

        var exists = _connector.ViewExists(viewName);
        return Task.FromResult(exists);
    }

    public Task RefreshAsync(string viewName, CancellationToken ct = default)
    {
        // Standard SQL views are auto-maintained; no refresh needed.
        // Platform-specific implementations (PostgreSQL materialized, MSSql indexed)
        // can override this in derived classes.
        return Task.CompletedTask;
    }
}
