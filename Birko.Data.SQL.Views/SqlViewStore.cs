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

    public Task<IEnumerable<TView>> QueryAsync(
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

        var results = _connector.Select(
            _sqlView,
            CreateTransformFunction(),
            conditions,
            orderFields,
            limit,
            offset);

        return Task.FromResult(results.Cast<TView>());
    }

    public Task<TView?> QueryFirstAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conditions = filter != null
            ? DataBase.ParseConditionExpression(filter)
            : null;

        var results = _connector.Select(
            _sqlView,
            CreateTransformFunction(),
            conditions,
            null,
            1,
            null);

        return Task.FromResult(results.Cast<TView>().FirstOrDefault());
    }

    public Task<long> CountAsync(
        Expression<Func<TView, bool>>? filter = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conditions = filter != null
            ? DataBase.ParseConditionExpression(filter)
            : null;

        var count = _connector.SelectCount(_sqlView, conditions);
        return Task.FromResult(count);
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
