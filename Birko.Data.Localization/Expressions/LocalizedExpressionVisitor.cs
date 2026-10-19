using Birko.Data.Localization.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Localization.Expressions;

/// <summary>
/// Result of splitting an expression into localized and non-localized parts.
/// </summary>
public class LocalizedFilterSplit<T> where T : Data.Models.AbstractModel, ILocalizable
{
    /// <summary>
    /// The remaining filter with localized field conditions removed.
    /// Null if the entire expression was localized.
    /// </summary>
    public Expression<Func<T, bool>>? RemainingFilter { get; init; }

    /// <summary>
    /// Extracted conditions on localized fields: FieldName → list of conditions on the translation Value.
    /// Each condition is an expression over <c>string</c> (the translation value).
    /// </summary>
    public IReadOnlyList<LocalizedFieldCondition> LocalizedConditions { get; init; } = Array.Empty<LocalizedFieldCondition>();

    /// <summary>
    /// Whether the original expression contained any conditions on localized fields.
    /// </summary>
    public bool HasLocalizedConditions => LocalizedConditions.Count > 0;
}

/// <summary>
/// A single condition extracted from a filter that targets a localized field.
/// </summary>
public class LocalizedFieldCondition
{
    /// <summary>The property name on the entity (e.g., "Name", "Description").</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>
    /// A predicate that tests a translation value string.
    /// Built from the original expression (e.g., x.Name == "Foo" → value == "Foo").
    /// </summary>
    public Func<string?, bool> ValuePredicate { get; init; } = _ => true;

    /// <summary>
    /// The filter part this condition came from, over the entity parameter: the same test on the <b>base</b>
    /// (default-culture) column. An entity with no translation for the field is matched by it (SH-M070, TASK-681).
    /// </summary>
    public Expression Part { get; init; } = Expression.Constant(false);

    /// <summary>
    /// The exact values an <c>==</c> or <c>IN</c> condition accepts, so the translation query can select rows by them
    /// (by <c>ValueHash</c>) instead of reading the whole field; <c>null</c> for every other shape.
    /// </summary>
    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>
/// Analyzes an expression tree to split conditions on localizable fields from non-localizable conditions.
/// Supports: ==, !=, Contains, StartsWith, EndsWith (optionally with a <see cref="StringComparison"/>) on
/// localizable string properties, against a value, and <c>values.Contains(x.Field)</c> (IN, TASK-681), combined with
/// &amp;&amp; (AndAlso) at the top level.
/// </summary>
/// <remarks>
/// CR-L137: two localized-filter semantics to be aware of:
/// <list type="bullet">
/// <item><description>
/// <b>Null comparisons are not localized.</b> <c>x.Name == null</c> / <c>x.Name != null</c> on a
/// localizable field are NOT extracted (the analyzer requires a non-null constant) — they pass through to
/// the inner store as a base-column predicate, so they test the base value, not the translation.
/// </description></item>
/// <item><description>
/// <b>An entity without a translation is matched on its base value</b> (TASK-681, SH-M070): every extracted
/// condition keeps its original <see cref="LocalizedFieldCondition.Part"/>, which the wrappers evaluate on the base
/// column for exactly the entities that have no translation for that field and culture. CR-L137's "<c>!=</c> only
/// matches entities that have a translation" no longer holds.
/// </description></item>
/// <item><description>
/// <b>Anything else that reads a localizable field is refused</b> with <see cref="NotSupportedException"/>
/// (SH-M077, TASK-168): a condition inside <c>||</c> or <c>!</c>, a chained call
/// (<c>x.Name.ToLower()</c>), a static call (<c>string.IsNullOrEmpty(x.Name)</c>), or a comparison with
/// another column. Such a condition used to reach the inner store unchanged and test the base
/// (default-culture) column, returning wrong rows with no error.
/// </description></item>
/// </list>
/// </remarks>
public static class LocalizedExpressionAnalyzer
{
    /// <summary>
    /// Splits a filter expression into localized and non-localized parts.
    /// </summary>
    /// <param name="filter">The original filter expression.</param>
    /// <param name="localizableFields">The set of property names that are localizable.</param>
    /// <returns>A split result with remaining filter and extracted localized conditions.</returns>
    public static LocalizedFilterSplit<T> Split<T>(
        Expression<Func<T, bool>>? filter,
        IReadOnlyList<string> localizableFields)
        where T : Data.Models.AbstractModel, ILocalizable
    {
        if (filter == null || localizableFields.Count == 0)
        {
            return new LocalizedFilterSplit<T> { RemainingFilter = filter };
        }

        var fieldSet = new HashSet<string>(localizableFields);
        var param = filter.Parameters[0];
        var localizedConditions = new List<LocalizedFieldCondition>();
        var remainingParts = new List<Expression>();

        // Flatten top-level AndAlso chain
        var parts = FlattenAndAlso(filter.Body);

        foreach (var part in parts)
        {
            var extracted = TryExtractLocalizedCondition(part, param, fieldSet);
            if (extracted != null)
            {
                localizedConditions.Add(extracted);
                continue;
            }

            RefuseUntranslatedLocalizedField(part, param, fieldSet);
            remainingParts.Add(part);
        }

        Expression<Func<T, bool>>? remaining = null;
        if (remainingParts.Count > 0)
        {
            var combined = remainingParts.Aggregate(Expression.AndAlso);
            remaining = Expression.Lambda<Func<T, bool>>(combined, param);
        }

        return new LocalizedFilterSplit<T>
        {
            RemainingFilter = remaining,
            LocalizedConditions = localizedConditions
        };
    }

    /// <summary>
    /// Throws when a part that could not be extracted still reads a localizable field, because the inner
    /// store would evaluate it against the base column (SH-M077). The documented null comparison is the one
    /// exception.
    /// </summary>
    private static void RefuseUntranslatedLocalizedField(Expression part, ParameterExpression param, HashSet<string> localizableFields)
    {
        if (IsNullComparison(part, param, localizableFields))
        {
            return;
        }

        var finder = new LocalizedFieldFinder(param, localizableFields);
        finder.Visit(part);
        if (finder.Found == null)
        {
            return;
        }

        throw new NotSupportedException(
            $"A {part.NodeType} filter condition reads the localizable field '{finder.Found}' in a form that cannot be "
            + "translated, so it would test the default-culture value instead of the current culture's. Supported: "
            + "==, !=, Contains, StartsWith and EndsWith on the field itself against a value, and values.Contains(field) (IN), "
            + "joined by &&.");
    }

    private static bool IsNullComparison(Expression part, ParameterExpression param, HashSet<string> localizableFields)
    {
        if (part is not BinaryExpression binary
            || (binary.NodeType != ExpressionType.Equal && binary.NodeType != ExpressionType.NotEqual))
        {
            return false;
        }

        return (ExtractFieldName(binary.Left, param, localizableFields) != null && IsNullValue(binary.Right))
            || (ExtractFieldName(binary.Right, param, localizableFields) != null && IsNullValue(binary.Left));
    }

    /// <summary>A literal <c>null</c>, or a captured value that is <c>null</c> — never an operand reading the entity.</summary>
    private static bool IsNullValue(Expression expression)
    {
        var parameters = new ParameterFinder();
        parameters.Visit(expression);
        return !parameters.Found && EvaluateExpression(expression) == null;
    }

    private sealed class LocalizedFieldFinder : ExpressionVisitor
    {
        private readonly ParameterExpression _param;
        private readonly HashSet<string> _fields;

        public LocalizedFieldFinder(ParameterExpression param, HashSet<string> fields)
        {
            _param = param;
            _fields = fields;
        }

        public string? Found { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (Found == null && ReadsParameter(node.Expression) && _fields.Contains(node.Member.Name))
            {
                Found = node.Member.Name;
            }
            return base.VisitMember(node);
        }

        /// <summary>The parameter itself, or the parameter cast to an interface or base type (<c>((INamed)x).Name</c>).</summary>
        private bool ReadsParameter(Expression? expression)
        {
            while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } cast)
            {
                expression = cast.Operand;
            }
            return expression == _param;
        }
    }

    /// <summary>
    /// Finds a parameter no lambda inside the visited expression binds — the filter's own entity parameter. The
    /// parameter of a nested lambda (<c>names.First(n =&gt; n.StartsWith("S"))</c>) is bound there and does not
    /// make the operand read the entity.
    /// </summary>
    private sealed class ParameterFinder : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> _bound = new();

        public bool Found { get; private set; }

        protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
        {
            var added = node.Parameters.Where(_bound.Add).ToList();
            Visit(node.Body);
            _bound.ExceptWith(added);
            return node;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (!_bound.Contains(node))
            {
                Found = true;
            }
            return node;
        }
    }

    /// <summary>
    /// Flattens a chain of AndAlso expressions into individual parts.
    /// </summary>
    private static List<Expression> FlattenAndAlso(Expression expression)
    {
        var parts = new List<Expression>();
        FlattenAndAlsoRecursive(expression, parts);
        return parts;
    }

    private static void FlattenAndAlsoRecursive(Expression expression, List<Expression> parts)
    {
        if (expression is BinaryExpression binary && binary.NodeType == ExpressionType.AndAlso)
        {
            FlattenAndAlsoRecursive(binary.Left, parts);
            FlattenAndAlsoRecursive(binary.Right, parts);
        }
        else
        {
            parts.Add(expression);
        }
    }

    /// <summary>
    /// Tries to extract a localized field condition from an expression node.
    /// Returns null if the node doesn't reference a localizable field.
    /// </summary>
    private static LocalizedFieldCondition? TryExtractLocalizedCondition(
        Expression node, ParameterExpression param, HashSet<string> localizableFields)
    {
        // Binary: x.Name == "value", x.Name != "value"
        if (node is BinaryExpression binary)
        {
            var (fieldName, constantValue) = ExtractFieldAndConstant(binary.Left, binary.Right, param, localizableFields);
            if (fieldName == null)
            {
                (fieldName, constantValue) = ExtractFieldAndConstant(binary.Right, binary.Left, param, localizableFields);
            }

            if (fieldName != null && constantValue != null)
            {
                Func<string?, bool>? predicate = binary.NodeType switch
                {
                    ExpressionType.Equal => v => v == constantValue,
                    ExpressionType.NotEqual => v => v != constantValue,
                    _ => null
                };

                if (predicate != null)
                {
                    return new LocalizedFieldCondition
                    {
                        FieldName = fieldName,
                        ValuePredicate = predicate,
                        Part = node,
                        Values = binary.NodeType == ExpressionType.Equal ? new[] { constantValue } : null,
                    };
                }
            }
        }

        // IN: values.Contains(x.Field) or Enumerable.Contains(values, x.Field) — and, on C# 14, the span overload an
        // array binds to, unwrapped the way every provider translator unwraps it (rule 47).
        if (node is MethodCallExpression inCall && inCall.Method.Name == "Contains")
        {
            Expression? source = null;
            Expression? item = null;
            if (inCall.Object != null && inCall.Arguments.Count == 1)
            {
                source = inCall.Object;
                item = inCall.Arguments[0];
            }
            else if (inCall.Object == null && inCall.Arguments.Count == 2)
            {
                source = Birko.Data.Expressions.SpanContains.UnwrapSpanConversion(inCall.Arguments[0]);
                item = inCall.Arguments[1];
            }

            var inField = item == null ? null : ExtractFieldName(item, param, localizableFields);
            if (inField != null && source != null && EvaluateExpression(source) is IEnumerable<string?> candidates
                && ComparesOrdinally(candidates))
            {
                var values = candidates.Where(v => v != null).Select(v => v!).Distinct(StringComparer.Ordinal).ToList();
                var accepted = new HashSet<string>(values, StringComparer.Ordinal);
                return new LocalizedFieldCondition
                {
                    FieldName = inField,
                    ValuePredicate = v => v != null && accepted.Contains(v),
                    Part = node,
                    Values = values,
                };
            }
        }

        // Method call: x.Name.Contains("value"), x.Name.StartsWith("value"), x.Name.EndsWith("value"), each
        // optionally with a StringComparison (SH-M078: it used to be dropped, so the match went case-sensitive).
        if (node is MethodCallExpression methodCall && methodCall.Object != null)
        {
            var fieldName = ExtractFieldName(methodCall.Object, param, localizableFields);
            if (fieldName != null && methodCall.Arguments.Count is 1 or 2)
            {
                var argValue = EvaluateExpression(methodCall.Arguments[0]);
                // Without the argument, each method keeps its own .NET default: Contains is ordinal, StartsWith and
                // EndsWith compare with the current culture.
                var comparison = methodCall.Arguments.Count == 2
                    ? EvaluateExpression(methodCall.Arguments[1]) as StringComparison?
                    : methodCall.Method.Name == "Contains" ? StringComparison.Ordinal : StringComparison.CurrentCulture;
                if (argValue is string strArg && comparison is StringComparison cmp)
                {
                    Func<string?, bool>? predicate = methodCall.Method.Name switch
                    {
                        "Contains" => v => v != null && v.Contains(strArg, cmp),
                        "StartsWith" => v => v != null && v.StartsWith(strArg, cmp),
                        "EndsWith" => v => v != null && v.EndsWith(strArg, cmp),
                        _ => null
                    };

                    if (predicate != null)
                    {
                        return new LocalizedFieldCondition { FieldName = fieldName, ValuePredicate = predicate, Part = node };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an IN source matches ordinally, as the translation lookup (by hash) and its confirmation do. A set with a
    /// case-insensitive comparer would match differently on the translated side than on the base column, so it is not
    /// extracted and the condition is refused (rule 41).
    /// </summary>
    private static bool ComparesOrdinally(IEnumerable<string?> source)
        => source switch
        {
            HashSet<string?> set => set.Comparer.Equals(EqualityComparer<string?>.Default) || set.Comparer.Equals(StringComparer.Ordinal),
            SortedSet<string?> => false,
            _ => true,
        };

    /// <summary>
    /// Tries to extract a (fieldName, constantValue) pair from left/right operands.
    /// </summary>
    private static (string? fieldName, string? constantValue) ExtractFieldAndConstant(
        Expression candidate, Expression other, ParameterExpression param, HashSet<string> localizableFields)
    {
        var fieldName = ExtractFieldName(candidate, param, localizableFields);
        if (fieldName == null)
        {
            return (null, null);
        }

        var value = EvaluateExpression(other);
        return (fieldName, value as string);
    }

    /// <summary>
    /// Extracts the property name if the expression is a member access on a localizable field.
    /// </summary>
    private static string? ExtractFieldName(
        Expression expression, ParameterExpression param, HashSet<string> localizableFields)
    {
        if (expression is MemberExpression member &&
            member.Expression == param &&
            member.Member is PropertyInfo prop &&
            prop.PropertyType == typeof(string) &&
            localizableFields.Contains(prop.Name))
        {
            return prop.Name;
        }
        return null;
    }

    /// <summary>
    /// Evaluates a constant or captured-variable expression to get its runtime value.
    /// </summary>
    private static object? EvaluateExpression(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        // Handle captured variables (closures): field access on a constant (compiler-generated class)
        if (expression is MemberExpression memberExpr && memberExpr.Expression is ConstantExpression outerConstant)
        {
            if (memberExpr.Member is FieldInfo field)
            {
                return field.GetValue(outerConstant.Value);
            }
            if (memberExpr.Member is PropertyInfo prop)
            {
                return prop.GetValue(outerConstant.Value);
            }
        }

        // An operand that reads the entity is not a value (x.Name == x.Code); it is left unextracted, and
        // RefuseUntranslatedLocalizedField reports it.
        var parameters = new ParameterFinder();
        parameters.Visit(expression);
        if (parameters.Found)
        {
            return null;
        }

        // SH-M079: a value that fails to evaluate is reported, not swallowed. Swallowing it left the condition
        // unextracted, so it tested the base column.
        try
        {
            return Expression.Lambda(expression).Compile().DynamicInvoke();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
