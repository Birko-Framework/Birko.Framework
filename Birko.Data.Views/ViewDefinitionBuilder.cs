using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Views;

/// <summary>
/// Fluent builder for defining cross-platform views.
/// <typeparamref name="TView"/> is the view result type (the projection shape).
/// </summary>
public class ViewDefinitionBuilder<TView> where TView : class
{
    private string? _name;
    private ViewQueryMode _queryMode = ViewQueryMode.OnTheFly;
    private Type? _primarySource;
    private readonly List<FieldSelector> _fields = new();
    private readonly List<JoinClause> _joins = new();
    private readonly List<AggregateClause> _aggregates = new();
    private readonly List<GroupByClause> _groupBy = new();
    private Dictionary<string, object>? _hints;

    /// <summary>Sets the persistent view name (required for Persistent/Auto modes).</summary>
    public ViewDefinitionBuilder<TView> HasName(string name)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        return this;
    }

    /// <summary>Sets the query execution mode.</summary>
    public ViewDefinitionBuilder<TView> HasQueryMode(ViewQueryMode mode)
    {
        _queryMode = mode;
        return this;
    }

    /// <summary>Declares the primary source entity.</summary>
    public ViewDefinitionBuilder<TView> From<TSource>() where TSource : class
    {
        _primarySource = typeof(TSource);
        return this;
    }

    /// <summary>Selects a field from a source entity, mapped to a view property.</summary>
    public ViewDefinitionBuilder<TView> Select<TSource, TProp>(
        Expression<Func<TSource, TProp>> sourceProperty,
        Expression<Func<TView, TProp>> viewProperty) where TSource : class
    {
        var sourceName = GetMemberName(sourceProperty);
        var viewName = GetMemberName(viewProperty);
        _fields.Add(new FieldSelector(typeof(TSource), sourceName, viewName));
        return this;
    }

    /// <summary>Joins another source entity.</summary>
    public ViewDefinitionBuilder<TView> Join<TLeft, TRight, TKey>(
        Expression<Func<TLeft, TKey>> leftKey,
        Expression<Func<TRight, TKey>> rightKey,
        JoinType joinType = JoinType.Inner) where TLeft : class where TRight : class
    {
        var leftProp = GetMemberName(leftKey);
        var rightProp = GetMemberName(rightKey);
        _joins.Add(new JoinClause(typeof(TLeft), typeof(TRight), leftProp, rightProp, joinType));
        return this;
    }

    /// <summary>Adds a LeftOuter join (convenience).</summary>
    public ViewDefinitionBuilder<TView> LeftJoin<TLeft, TRight, TKey>(
        Expression<Func<TLeft, TKey>> leftKey,
        Expression<Func<TRight, TKey>> rightKey) where TLeft : class where TRight : class
    {
        return Join(leftKey, rightKey, JoinType.LeftOuter);
    }

    /// <summary>Groups by a source field.</summary>
    public ViewDefinitionBuilder<TView> GroupBy<TSource, TProp>(
        Expression<Func<TSource, TProp>> property) where TSource : class
    {
        var propName = GetMemberName(property);
        _groupBy.Add(new GroupByClause(typeof(TSource), propName));
        return this;
    }

    /// <summary>Adds an aggregate operation.</summary>
    public ViewDefinitionBuilder<TView> Aggregate<TSource, TProp>(
        AggregateFunction function,
        Expression<Func<TSource, TProp>> sourceProperty,
        Expression<Func<TView, TProp>> viewProperty) where TSource : class
    {
        var sourceName = GetMemberName(sourceProperty);
        var viewName = GetMemberName(viewProperty);
        _aggregates.Add(new AggregateClause(function, typeof(TSource), sourceName, viewName));
        return this;
    }

    /// <summary>Count aggregate (COUNT(*) convenience).</summary>
    public ViewDefinitionBuilder<TView> Count<TSource>(
        Expression<Func<TView, int>> viewProperty) where TSource : class
    {
        var viewName = GetMemberName(viewProperty);
        _aggregates.Add(new AggregateClause(AggregateFunction.Count, typeof(TSource), null, viewName));
        return this;
    }

    /// <summary>Count aggregate returning long.</summary>
    public ViewDefinitionBuilder<TView> Count<TSource>(
        Expression<Func<TView, long>> viewProperty) where TSource : class
    {
        var viewName = GetMemberName(viewProperty);
        _aggregates.Add(new AggregateClause(AggregateFunction.Count, typeof(TSource), null, viewName));
        return this;
    }

    /// <summary>Sum aggregate.</summary>
    public ViewDefinitionBuilder<TView> Sum<TSource, TProp>(
        Expression<Func<TSource, TProp>> sourceProperty,
        Expression<Func<TView, TProp>> viewProperty) where TSource : class
    {
        return Aggregate(AggregateFunction.Sum, sourceProperty, viewProperty);
    }

    /// <summary>Avg aggregate.</summary>
    public ViewDefinitionBuilder<TView> Avg<TSource, TProp>(
        Expression<Func<TSource, TProp>> sourceProperty,
        Expression<Func<TView, TProp>> viewProperty) where TSource : class
    {
        return Aggregate(AggregateFunction.Avg, sourceProperty, viewProperty);
    }

    /// <summary>Min aggregate.</summary>
    public ViewDefinitionBuilder<TView> Min<TSource, TProp>(
        Expression<Func<TSource, TProp>> sourceProperty,
        Expression<Func<TView, TProp>> viewProperty) where TSource : class
    {
        return Aggregate(AggregateFunction.Min, sourceProperty, viewProperty);
    }

    /// <summary>Max aggregate.</summary>
    public ViewDefinitionBuilder<TView> Max<TSource, TProp>(
        Expression<Func<TSource, TProp>> sourceProperty,
        Expression<Func<TView, TProp>> viewProperty) where TSource : class
    {
        return Aggregate(AggregateFunction.Max, sourceProperty, viewProperty);
    }

    /// <summary>Sets a platform-specific hint.</summary>
    public ViewDefinitionBuilder<TView> Hint(string key, object value)
    {
        _hints ??= new Dictionary<string, object>();
        _hints[key] = value;
        return this;
    }

    /// <summary>Validates and builds the immutable <see cref="ViewDefinition"/>.</summary>
    public ViewDefinition Build()
    {
        if (_primarySource == null)
        {
            throw new InvalidOperationException("From<TSource>() must be called to set the primary source entity.");
        }

        if (_queryMode is ViewQueryMode.Persistent or ViewQueryMode.Auto && string.IsNullOrWhiteSpace(_name))
        {
            throw new InvalidOperationException("HasName() is required for Persistent or Auto query modes.");
        }

        if (_aggregates.Count > 0 && _groupBy.Count == 0 && _fields.Count > 0)
        {
            throw new InvalidOperationException(
                "When aggregates are present, all non-aggregate selected fields must appear in GroupBy. " +
                "Call GroupBy() for each selected field, or remove Select() calls to aggregate the entire source.");
        }

        if (_aggregates.Count > 0 && _groupBy.Count > 0)
        {
            var groupByKeys = _groupBy.Select(g => $"{g.SourceType.FullName}.{g.PropertyName}").ToHashSet();
            foreach (var field in _fields)
            {
                var key = $"{field.SourceType.FullName}.{field.SourceProperty}";
                if (!groupByKeys.Contains(key))
                {
                    throw new InvalidOperationException(
                        $"Field '{field.SourceProperty}' on '{field.SourceType.Name}' is selected but not in GroupBy. " +
                        "All non-aggregate fields must be grouped when aggregates are present.");
                }
            }
        }

        ValidateNumericAggregates();

        var hints = _hints != null
            ? new Dictionary<string, object>(_hints)
            : new Dictionary<string, object>();

        return new ViewDefinition(
            _name,
            _queryMode,
            _primarySource,
            typeof(TView),
            _fields.AsReadOnly(),
            _joins.AsReadOnly(),
            _aggregates.AsReadOnly(),
            _groupBy.AsReadOnly(),
            hints);
    }

    private void ValidateNumericAggregates()
    {
        var numericTypes = new HashSet<Type>
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal),
            typeof(byte?), typeof(sbyte?), typeof(short?), typeof(ushort?),
            typeof(int?), typeof(uint?), typeof(long?), typeof(ulong?),
            typeof(float?), typeof(double?), typeof(decimal?)
        };

        foreach (var agg in _aggregates)
        {
            if (agg.Function is AggregateFunction.Count)
            {
                continue;
            }

            var viewProp = typeof(TView).GetProperty(agg.ViewProperty);
            if (viewProp == null)
            {
                throw new InvalidOperationException(
                    $"View property '{agg.ViewProperty}' not found on '{typeof(TView).Name}'.");
            }

            if (agg.Function is AggregateFunction.Sum or AggregateFunction.Avg
                && !numericTypes.Contains(viewProp.PropertyType))
            {
                throw new InvalidOperationException(
                    $"Aggregate {agg.Function} on '{agg.ViewProperty}' requires a numeric property type, " +
                    $"but found '{viewProp.PropertyType.Name}'.");
            }
        }
    }

    private static string GetMemberName<TObj, TProp>(Expression<Func<TObj, TProp>> expression)
    {
        if (expression.Body is MemberExpression member)
        {
            return member.Member.Name;
        }

        if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
        {
            return unaryMember.Member.Name;
        }

        throw new ArgumentException("Expression must be a member access expression.", nameof(expression));
    }
}
