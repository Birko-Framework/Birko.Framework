using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tables;
using Birko.Data.Stores;
using Birko.Data.Views;

namespace Birko.Data.SQL.Views;

/// <summary>
/// SQL implementation of <see cref="IViewStore{TView}"/>.
/// Uses the existing connector infrastructure to execute view queries.
/// </summary>
public class SqlViewStore<TView> : IViewStore<TView> where TView : class, new()
{
    private readonly AbstractConnector _connector;
    private readonly Tables.View _sqlView;

    public SqlViewStore(AbstractConnector connector, ViewDefinition definition)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        _sqlView = SqlViewTranslator.Translate(definition);
    }

    public SqlViewStore(AbstractConnector connector, Tables.View sqlView)
    {
        _connector = connector ?? throw new ArgumentNullException(nameof(connector));
        _sqlView = sqlView ?? throw new ArgumentNullException(nameof(sqlView));
    }

    public async Task<IEnumerable<TView>> QueryAsync(
        Expression<Func<TView, bool>>? filter = null,
        OrderBy<TView>? orderBy = null,
        int? limit = null,
        int? offset = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conditions = filter != null
            ? DataBase.ParseConditionExpression(filter)
            : null;

        var orderFields = TranslateOrderBy(orderBy);

        // Use the genuine async reader (threads ct into the DB call) when the connector supports it,
        // instead of blocking the calling thread on synchronous I/O (CR-H096).
        if (_connector is AbstractAsyncConnector asyncConnector)
        {
            var list = new List<TView>();
            await foreach (var item in asyncConnector
                .SelectAsync(_sqlView, CreateTransformFunction(), conditions, orderFields, limit, offset, ct)
                .ConfigureAwait(false))
            {
                if (item is TView view) list.Add(view);
            }
            return list;
        }

        return _connector.Select(_sqlView, CreateTransformFunction(), conditions, orderFields, limit, offset).Cast<TView>();
    }

    public async Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conditions = filter != null
            ? DataBase.ParseConditionExpression(filter)
            : null;

        if (_connector is AbstractAsyncConnector asyncConnector)
        {
            await foreach (var item in asyncConnector
                .SelectAsync(_sqlView, CreateTransformFunction(), conditions, null, 1, null, ct)
                .ConfigureAwait(false))
            {
                if (item is TView view) return view;
            }
            return null;
        }

        return _connector.Select(_sqlView, CreateTransformFunction(), conditions, null, 1, null).Cast<TView>().FirstOrDefault();
    }

    public async Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conditions = filter != null
            ? DataBase.ParseConditionExpression(filter)
            : null;

        if (_connector is AbstractAsyncConnector asyncConnector)
        {
            return await asyncConnector.SelectCountAsync(_sqlView, conditions, ct).ConfigureAwait(false);
        }

        return _connector.SelectCount(_sqlView, conditions);
    }

    private Func<IDictionary<int, string>, DbDataReader, object> CreateTransformFunction()
    {
        return (fields, reader) =>
        {
            var instance = new TView();
            var viewType = typeof(TView);
            var tableFields = _sqlView.GetTableFields().ToArray();

            for (int i = 0; i < tableFields.Length && i < reader.FieldCount; i++)
            {
                var field = tableFields[i];
                if (field.Property != null)
                {
                    field.Read(instance, reader, i);
                }
            }

            return instance;
        };
    }

    private static IDictionary<string, bool>? TranslateOrderBy(OrderBy<TView>? orderBy)
    {
        if (orderBy?.Fields == null || !orderBy.Fields.Any())
        {
            return null;
        }

        var result = new Dictionary<string, bool>();
        foreach (var field in orderBy.Fields)
        {
            result[field.PropertyName] = field.Descending;
        }

        return result;
    }
}
