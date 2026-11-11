using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Rules;

/// <summary>
/// Converts Birko.Rules rule trees into LINQ Expression&lt;Func&lt;T, bool&gt;&gt; predicates.
/// Works with any store that accepts LINQ expressions (SQL, Elasticsearch, MongoDB, JSON, etc.).
/// <para>
/// <b>The one rule-to-LINQ translator (TASK-144).</b> <c>Birko.Data.Patterns</c>' <c>RuleSpecification</c> used to carry
/// a second, independent copy, and the two drifted both ways: the specification lacked <c>Like</c>, <c>In</c> and
/// <c>NotIn</c> (match-all, TASK-116), while this converter kept defects the specification had fixed (a null needle
/// matching every row, an interior <c>%</c> treated as a literal, a Guid given as text throwing, <c>10.7</c> rounded
/// to <c>11</c> against an int, a string ordered with <c>&gt;</c> throwing). It is now one translator, with this
/// converter's nested fields and the specification's safety rules, and <c>RuleSpecification.ToExpression</c> calls it.
/// </para>
/// <para>
/// <b>A rule that cannot become a predicate MATCHES NOTHING</b> (FIELD-025, TASK-558): a field that resolves to no
/// property, a disabled rule, an empty or all-disabled group, a disabled <see cref="RuleSet"/>, and a leaf whose value or
/// member type it cannot express. The entry points never return <c>null</c>: nothing-to-match is <c>x =&gt; false</c>.
/// </para>
/// <para>
/// <b>THE invariant (TASK-116):</b> such a leaf is never negated. <c>Not(match-none)</c> is match-ALL, which is how an
/// inapplicable predicate became "every row" under <c>NotContains</c> or a negated rule. Negation is pushed down to the
/// leaves by De Morgan and skips them; a nested field's null guards are applied after the negation, so a null
/// intermediate never matches either way.
/// </para>
/// <para>
/// <b>Store-translatable output only.</b> String <c>Equal</c> / <c>NotEqual</c> / <c>In</c> / <c>NotIn</c> is plain
/// <c>==</c>, C#'s ordinal equality: no case-insensitive equality has one form every store translates (SH-M295). The
/// in-memory <see cref="RuleEvaluator"/> compares two strings ordinally too, and so does every SQL provider, the lambda's
/// <c>==</c> since TASK-873 and <c>RuleConditionConverter</c> since TASK-895 (the user's decision, 2026-11-10): one answer
/// for a rule. It used to be the one recorded divergence (the evaluator ignored case). The string-method operators keep
/// <c>StringComparison.OrdinalIgnoreCase</c>, which the stores this framework ships translate.
/// </para>
/// <para>
/// An operator with no arm here throws <see cref="NotSupportedException"/>: that is a gap in the code, not a property of
/// the data, and a constant in its place changed the filter silently (SH-H041). So does a <c>Like</c> pattern with an
/// interior <c>%</c> (<see cref="RuleLikePattern"/>), which no runner has one answer for (TASK-882).
/// </para>
/// <para>
/// <b>TASK-882: the C# lambda is the reference.</b> A null against a non-nullable member is C#'s lifted comparison (<c>!=</c>
/// true, the rest false), an enum orders by its underlying value, and a value is read through
/// <see cref="RuleValueConverter"/>, as <see cref="RuleEvaluator"/> and the SQL rule converter read it.
/// </para>
/// </summary>
public static class RuleExpressionConverter
{
    /// <summary>
    /// Converts a single rule (leaf or group) into a LINQ predicate expression. Never null: a disabled rule, an
    /// empty group or an unresolved field yields <c>x =&gt; false</c>.
    /// </summary>
    public static Expression<Func<T, bool>> ToExpression<T>(IRule rule) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, bool>>(BuildExpression(rule, param, negate: false), param);
    }

    /// <summary>
    /// Converts a RuleSet into a LINQ predicate (all enabled rules AND'd). A disabled set, or one with no enabled rule,
    /// yields <c>x =&gt; false</c>.
    /// </summary>
    public static Expression<Func<T, bool>> ToExpression<T>(RuleSet ruleSet) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        if (!ruleSet.IsEnabled)
            return Expression.Lambda<Func<T, bool>>(Expression.Constant(false), param);
        return Expression.Lambda<Func<T, bool>>(BuildConjunction(Snapshot(ruleSet.Rules), param), param);
    }

    /// <summary>
    /// Converts multiple rules into a combined AND predicate. No enabled rule yields <c>x =&gt; false</c>.
    /// </summary>
    public static Expression<Func<T, bool>> ToExpression<T>(IEnumerable<IRule> rules) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, bool>>(BuildConjunction(rules, param), param);
    }

    /// <summary>
    /// The rules as they are now (SH-M370, TASK-156): a <see cref="RuleList"/>'s current array, which no edit changes.
    /// </summary>
    internal static IRule[] Snapshot(IEnumerable<IRule> rules) => rules is RuleList list ? list.Snapshot() : rules.ToArray();

    private static Expression BuildConjunction(IEnumerable<IRule> rules, ParameterExpression param)
    {
        var built = rules.Where(r => r.IsEnabled).Select(r => BuildExpression(r, param, negate: false)).ToList();
        return built.Count == 0 ? Expression.Constant(false) : built.Aggregate(Expression.AndAlso);
    }

    // ── The tree ──

    private readonly struct Leaf
    {
        public readonly Expression Expr;
        public readonly bool Degraded;

        private Leaf(Expression expr, bool degraded)
        {
            Expr = expr;
            Degraded = degraded;
        }

        public static Leaf Predicate(Expression expr) => new(expr, false);

        /// <summary>A leaf that cannot be evaluated: matches nothing, and is never negated.</summary>
        public static Leaf Unevaluable() => new(Expression.Constant(false), true);
    }

    private static Expression BuildExpression(IRule rule, ParameterExpression param, bool negate)
    {
        // A disabled rule matches nothing, as RuleEvaluator answers NoMatch (SH-H042). A disabled ROOT that became
        // `x => true` was the explicit all-rows door: Delete(spec.ToExpression()) emptied the table.
        if (!rule.IsEnabled)
            return Expression.Constant(false);

        return rule switch
        {
            Rule leaf => BuildLeafExpression(leaf, param, negate),
            RuleGroup group => BuildGroupExpression(group, param, negate),
            _ => Expression.Constant(false)
        };
    }

    private static Expression BuildGroupExpression(RuleGroup group, ParameterExpression param, bool negate)
    {
        var enabled = Snapshot(group.Rules).Where(r => r.IsEnabled).ToList();
        if (enabled.Count == 0)
            return Expression.Constant(false);

        var negated = group.IsNegated != negate;
        // NOT (A AND B) is NOT A OR NOT B: under negation the group's connective flips.
        var conjunction = (group.Logic == LogicOperator.And) != negated;
        // Every enabled child is built, so each one's own errors surface.
        var built = enabled.Select(r => BuildExpression(r, param, negated)).ToList();
        return built.Aggregate((a, b) => conjunction ? Expression.AndAlso(a, b) : Expression.OrElse(a, b));
    }

    private static Expression BuildLeafExpression(Rule rule, ParameterExpression param, bool negate)
    {
        var member = BuildMemberAccess(param, rule.Field);
        // An unresolved field is unevaluable: matches nothing, negated or not.
        if (member is null)
            return Leaf.Unevaluable().Expr;

        Leaf leaf = rule.Operator switch
        {
            ComparisonOperator.IsNull => Leaf.Predicate(BuildNullCheck(member, isNull: true)),
            ComparisonOperator.IsNotNull => Leaf.Predicate(BuildNullCheck(member, isNull: false)),
            ComparisonOperator.Equal => BuildComparison(member, rule.Value, Expression.Equal),
            ComparisonOperator.NotEqual => BuildComparison(member, rule.Value, Expression.NotEqual),
            ComparisonOperator.GreaterThan => BuildComparison(member, rule.Value, Expression.GreaterThan),
            ComparisonOperator.GreaterThanOrEqual => BuildComparison(member, rule.Value, Expression.GreaterThanOrEqual),
            ComparisonOperator.LessThan => BuildComparison(member, rule.Value, Expression.LessThan),
            ComparisonOperator.LessThanOrEqual => BuildComparison(member, rule.Value, Expression.LessThanOrEqual),
            ComparisonOperator.Between => BuildBetween(member, rule.Value, rule.UpperValue),
            ComparisonOperator.Contains => BuildStringMethod(member, rule.Value, nameof(string.Contains)),
            ComparisonOperator.NotContains => Negate(BuildStringMethod(member, rule.Value, nameof(string.Contains))),
            ComparisonOperator.StartsWith => BuildStringMethod(member, rule.Value, nameof(string.StartsWith)),
            ComparisonOperator.EndsWith => BuildStringMethod(member, rule.Value, nameof(string.EndsWith)),
            ComparisonOperator.Like => BuildLike(member, rule.Value, rule.Field),
            ComparisonOperator.In => BuildIn(member, rule.Value),
            ComparisonOperator.NotIn => Negate(BuildIn(member, rule.Value)),
            _ => throw new NotSupportedException(
                $"RuleExpressionConverter cannot translate ComparisonOperator.{rule.Operator} on field '{rule.Field}'. "
                + "Add an arm to BuildLeafExpression; do not let it fall through to a constant, which would change the "
                + "filter silently.")
        };

        // THE invariant: a degraded leaf is never negated. The leaf's own flag and an enclosing negated group's cancel
        // out: NOT(NOT a) is a.
        var body = rule.IsNegated != negate && !leaf.Degraded ? Expression.Not(leaf.Expr) : leaf.Expr;
        // Null guards for a nested field (Address.City), after the negation: a null intermediate never matches.
        return WrapWithNullGuards(param, rule.Field, body);
    }

    private static Leaf Negate(Leaf leaf) => leaf.Degraded ? leaf : Leaf.Predicate(Expression.Not(leaf.Expr));

    // ── Members (nested: "Address.City") ──

    // ConcurrentDictionary: the entry points can be called concurrently. Mirrors ObjectRuleContext's cache.
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo[]?> PathCache = new();

    private static PropertyInfo[]? ResolvePath(Type type, string field)
    {
        return PathCache.GetOrAdd((type, field), static key =>
        {
            var parts = key.Item2.Split('.');
            var path = new PropertyInfo[parts.Length];
            var current = key.Item1;
            for (var i = 0; i < parts.Length; i++)
            {
                var prop = current.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop is null)
                    return null;
                path[i] = prop;
                current = prop.PropertyType;
            }
            return path;
        });
    }

    private static MemberExpression? BuildMemberAccess(ParameterExpression param, string field)
    {
        var path = ResolvePath(param.Type, field);
        if (path is null)
            return null;
        Expression current = param;
        foreach (var prop in path)
            current = Expression.Property(current, prop);
        return (MemberExpression)current;
    }

    private static Expression WrapWithNullGuards(ParameterExpression param, string field, Expression inner)
    {
        var path = ResolvePath(param.Type, field);
        if (path is null || path.Length <= 1)
            return inner;

        Expression? guard = null;
        Expression current = param;
        for (var i = 0; i < path.Length - 1; i++)
        {
            current = Expression.Property(current, path[i]);
            if (!current.Type.IsValueType || Nullable.GetUnderlyingType(current.Type) is not null)
            {
                var notNull = Expression.NotEqual(current, Expression.Constant(null, current.Type));
                guard = guard is null ? notNull : Expression.AndAlso(guard, notNull);
            }
        }
        return guard is null ? inner : Expression.AndAlso(guard, inner);
    }

    // ── Leaves ──

    private static Expression BuildNullCheck(MemberExpression member, bool isNull)
    {
        // SH-M290 (TASK-171): Constant(null, int) throws. A non-nullable value is never null, as the evaluator answers.
        if (member.Type.IsValueType && Nullable.GetUnderlyingType(member.Type) is null)
            return Expression.Constant(!isNull);
        var nullExpr = Expression.Constant(null, member.Type);
        return isNull ? Expression.Equal(member, nullExpr) : Expression.NotEqual(member, nullExpr);
    }

    private static Leaf BuildComparison(MemberExpression member, object? value, Func<Expression, Expression, BinaryExpression> comparison)
    {
        // A value that cannot be converted to the member type (a mistyped value) makes the leaf unevaluable rather than
        // throwing; NOT false, which a negation would invert into match-all.
        // SH-M294 (TASK-171): ChangeType(10.7, int) is 11, so a fractional value against an integral member is compared
        // in double, as the evaluator compares.
        Expression left = member;
        if (RuleValueConverter.ComparesAsFraction(value, member.Type))
        {
            left = Expression.Convert(member, Nullable.GetUnderlyingType(member.Type) is null ? typeof(double) : typeof(double?));
        }
        else if (value is null && left.Type.IsValueType && Nullable.GetUnderlyingType(left.Type) is null)
        {
            // TASK-882: null against a non-nullable member is C#'s lifted comparison, `(int?)x.Amount == null`: false for
            // == and every ordering, true for !=, and so true for their negations. The C# compiler emits exactly this lift.
            // It matched nothing before, in both polarities, where RuleEvaluator and SQL gave C#'s answer.
            left = Expression.Convert(member, typeof(Nullable<>).MakeGenericType(left.Type));
        }

        if (!TryConvertConstant(value, left.Type, out var constant))
            return Leaf.Unevaluable();

        try
        {
            return Leaf.Predicate(comparison(left, constant));
        }
        catch (InvalidOperationException) when (EnumUnderlying(left.Type) is { } underlying)
        {
            // TASK-882: an enum has no ordering operator in an expression tree; C# orders it by its underlying value, and the
            // C# compiler emits `(int)x.State > 0` for `x.State > State.Pending`. It matched nothing before.
            return Leaf.Predicate(comparison(Expression.Convert(left, underlying), Expression.Convert(constant, underlying)));
        }
        catch (InvalidOperationException)
        {
            // SH-M291 (TASK-171): no such operator for the type (`string > string`, `bool >= bool`). No store can order
            // strings as the evaluator does (culturally), so it degrades.
            return Leaf.Unevaluable();
        }
    }

    /// <summary>The underlying integral type of an enum (or nullable enum) type, nullable as the type is; else null.</summary>
    private static Type? EnumUnderlying(Type type)
    {
        var inner = Nullable.GetUnderlyingType(type);
        var enumType = inner ?? type;
        if (!enumType.IsEnum)
            return null;
        var underlying = Enum.GetUnderlyingType(enumType);
        return inner is null ? underlying : typeof(Nullable<>).MakeGenericType(underlying);
    }

    private static Leaf BuildBetween(MemberExpression member, object? lower, object? upper)
    {
        // TASK-645: each bound through BuildComparison, the one place a fractional value against an integral member is
        // compared in double (SH-M294). ChangeType rounded here, so `Qty Between 10.5..20` selected Qty 10.
        var from = BuildComparison(member, lower, Expression.GreaterThanOrEqual);
        var to = BuildComparison(member, upper, Expression.LessThanOrEqual);
        if (from.Degraded || to.Degraded)
            return Leaf.Unevaluable(); // an unconvertible bound, or SH-M291: no ordering for this type
        return Leaf.Predicate(Expression.AndAlso(from.Expr, to.Expr));
    }

    private static Leaf BuildLike(MemberExpression member, object? value, string field)
    {
        // TASK-882: one parser for every runner (RuleLikePattern), first, so an interior '%' is refused whatever the member.
        var parsed = RuleLikePattern.Parse(value, field);
        if (member.Type != typeof(string) || parsed is not { } pattern)
            return Leaf.Unevaluable();

        return (pattern.AnyPrefix, pattern.AnySuffix) switch
        {
            (true, true) => BuildStringMethod(member, pattern.Core, nameof(string.Contains)),
            (false, true) => BuildStringMethod(member, pattern.Core, nameof(string.StartsWith)),
            (true, false) => BuildStringMethod(member, pattern.Core, nameof(string.EndsWith)),
            // TASK-882: no wildcard is the string operators' OrdinalIgnoreCase equality, as RuleEvaluator and the SQL rule
            // compare it. It was ordinal ==, so `Like "ABC"` missed "abc" here and matched it in memory.
            _ => Leaf.Predicate(Expression.Call(
                typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string), typeof(StringComparison)])!,
                member,
                Expression.Constant(pattern.Core, typeof(string)),
                Expression.Constant(StringComparison.OrdinalIgnoreCase, typeof(StringComparison)))),
        };
    }

    private static Leaf BuildIn(MemberExpression member, object? value)
    {
        // A null set is unevaluable (In and NotIn both match nothing). A single non-collection value is one candidate,
        // as RuleEvaluator reads it.
        if (value is null)
            return Leaf.Unevaluable();
        IEnumerable candidates = value is IEnumerable raw and not string ? raw : new[] { value };

        Expression? combined = null;
        foreach (var item in candidates)
        {
            // TASK-645: through BuildComparison, as Between; ChangeType rounded, so `Qty In [10.6]` selected Qty 11.
            var eq = BuildComparison(member, item, Expression.Equal);
            if (eq.Degraded)
                return Leaf.Unevaluable();
            combined = combined is null ? eq.Expr : Expression.OrElse(combined, eq.Expr);
        }
        // Empty set: a genuine predicate matching nothing, so NotIn over it matches everything. Predicate, not
        // Unevaluable, precisely so Negate CAN invert it.
        return Leaf.Predicate(combined ?? Expression.Constant(false));
    }

    private static Leaf BuildStringMethod(MemberExpression member, object? value, string methodName)
    {
        // String methods apply only to string members. Unevaluable, not false: under NotContains a false became every
        // row (SH-H043 / SH-H044).
        if (member.Type != typeof(string))
            return Leaf.Unevaluable();
        // SH-M296 (TASK-171): a null needle became "", which every string contains. "Contains nothing" is not a predicate.
        if (value is null)
            return Leaf.Unevaluable();

        var method = typeof(string).GetMethod(methodName, [typeof(string), typeof(StringComparison)])!;
        var call = Expression.Call(member, method,
            Expression.Constant(value.ToString() ?? string.Empty, typeof(string)),
            Expression.Constant(StringComparison.OrdinalIgnoreCase, typeof(StringComparison)));
        // A null string field does not match; that IS a predicate, so it may be negated.
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        return Leaf.Predicate(Expression.AndAlso(notNull, call));
    }

    // ── Values ──

    private static bool TryConvertConstant(object? value, Type targetType, out Expression constant)
    {
        // TASK-882: through RuleValueConverter, the producer RuleEvaluator and the SQL rule converter read values through.
        if (!RuleValueConverter.TryConvert(value, targetType, out var converted))
        {
            constant = Expression.Constant(false);
            return false;
        }
        constant = Expression.Constant(converted, targetType);
        return true;
    }
}
