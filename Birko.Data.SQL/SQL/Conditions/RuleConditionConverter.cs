using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Birko.Rules;

namespace Birko.Data.SQL.Conditions;

/// <summary>
/// Converts Birko.Rules rule trees into Birko.Data.SQL Condition lists for dynamic SQL query building.
/// <para>
/// <b>SH-H023 (TASK-111).</b> Every overload here now checks <c>rule.Field</c> before it becomes a
/// <see cref="Condition.Name"/>, because that name is interpolated straight into <c>CommandText</c> by
/// every condition strategy. Prefer the overloads that take an entity type: they resolve the field against
/// table metadata, so a <c>[NamedField]</c>-remapped property filters on the right column and no caller
/// text can reach the statement. The type-less overloads have no metadata to resolve against and fall back
/// to requiring a bare column identifier — enough to close the injection, not enough to fix a remapping.
/// See <see cref="DataBase.ResolveRuleField"/> for the measured payloads.
/// </para>
/// </summary>
public static class RuleConditionConverter
{
    /// <summary>
    /// Converts a single rule (leaf or group) into a list of SQL conditions, resolving each
    /// <c>rule.Field</c> against <typeparamref name="T"/>'s table metadata.
    /// </summary>
    /// <exception cref="ArgumentException">A rule's field names no column of <typeparamref name="T"/>.</exception>
    public static IEnumerable<Condition> ToConditions<T>(IRule rule) => ToConditions(typeof(T), rule);

    /// <summary>
    /// Converts all rules in a RuleSet into SQL conditions (AND-joined), resolving each <c>rule.Field</c>
    /// against <typeparamref name="T"/>'s table metadata.
    /// </summary>
    /// <exception cref="ArgumentException">A rule's field names no column of <typeparamref name="T"/>.</exception>
    public static IEnumerable<Condition> ToConditions<T>(RuleSet ruleSet) => ToConditions(typeof(T), ruleSet);

    /// <summary>
    /// Converts a single rule (leaf or group) into a list of SQL conditions, resolving each
    /// <c>rule.Field</c> against <paramref name="entityType"/>'s table metadata.
    /// </summary>
    /// <exception cref="ArgumentException">A rule's field names no column of the entity.</exception>
    public static IEnumerable<Condition> ToConditions(Type entityType, IRule rule)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        return Convert(rule, entityType);
    }

    /// <summary>
    /// Converts all rules in a RuleSet into SQL conditions (AND-joined), resolving each <c>rule.Field</c>
    /// against <paramref name="entityType"/>'s table metadata.
    /// </summary>
    /// <exception cref="ArgumentException">A rule's field names no column of the entity.</exception>
    public static IEnumerable<Condition> ToConditions(Type entityType, RuleSet ruleSet)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));
        if (ruleSet == null)
            throw new ArgumentNullException(nameof(ruleSet));

        return ConvertSet(ruleSet, entityType);
    }

    /// <summary>
    /// Converts a single rule (leaf or group) into a list of SQL conditions.
    /// <para>
    /// Without an entity type the field cannot be resolved, so it must already be a bare column
    /// identifier; anything else throws. Prefer <see cref="ToConditions{T}(IRule)"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">A rule's field is not a bare column identifier.</exception>
    public static IEnumerable<Condition> ToConditions(IRule rule)
    {
        if (rule == null)
            throw new ArgumentNullException(nameof(rule));

        return Convert(rule, entityType: null);
    }

    /// <summary>
    /// Converts all rules in a RuleSet into SQL conditions (AND-joined).
    /// <para>
    /// Without an entity type the fields cannot be resolved, so each must already be a bare column
    /// identifier; anything else throws. Prefer <see cref="ToConditions{T}(RuleSet)"/>.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentException">A rule's field is not a bare column identifier.</exception>
    public static IEnumerable<Condition> ToConditions(RuleSet ruleSet)
    {
        if (ruleSet == null)
            throw new ArgumentNullException(nameof(ruleSet));

        return ConvertSet(ruleSet, entityType: null);
    }

    // FIELD-026 (TASK-559). A disabled rule, an empty or all-disabled group and a disabled RuleSet MATCH NOTHING —
    // the answer RuleSpecification and RuleEvaluator give for the same tree. They used to convert to no condition,
    // and an empty condition list is no WHERE: every row. A destructive write was already refused by the connector's
    // whole-table guard, but a read silently returned the whole table, and an empty group nested in an AND was
    // dropped, so `A AND <empty>` selected A where the other engines select nothing.
    //
    // Negation is pushed down to the leaves by De Morgan, as RuleSpecification does (TASK-320), instead of a SQL
    // NOT around a sub-condition: a match-none is a fact about the rule tree, not a predicate, so an enclosing
    // negation must not invert it into match-all. A null node below means "matches nothing, under any negation".
    //
    // The renderer joins a group's children with the GROUP's IsOr (AbstractConnectorBase.AppendSubConditionsTo),
    // and a flat chain joins each term to the one before it by that term's IsOr. This converter used to build an OR
    // group as AndSubCondition with IsOr set on the children, which the renderer ignores: every OR group ran as AND,
    // so `A OR B` returned the intersection (measured on SQLite, TASK-559). An OR group is now an OR sub-condition,
    // and a top-level term is wrapped so that flag never joins it to its sibling.

    // TASK-864. A leaf over a column that can hold NULL records it as a nullable operand, and the finished tree goes through
    // TASK-859's ApplyCSharpNullSemantics — the one place an IS NULL leg is added, for a lambda and for a rule alike — so a
    // rule answers on a NULL row what the same rule compiled to C# (RuleExpressionConverter) answers. Negation is already at
    // the leaves (no group built here carries IsNot), so each leaf's own IsNot is its whole polarity.

    private static IEnumerable<Condition> Convert(IRule rule, Type? entityType)
    {
        var node = Build(rule, negate: false, entityType);
        return node == null ? [MatchNone()] : DataBase.ApplyCSharpNullSemantics([AsAndTerm(node)]).ToList();
    }

    private static IEnumerable<Condition> ConvertSet(RuleSet ruleSet, Type? entityType)
    {
        if (!ruleSet.IsEnabled)
            return [MatchNone()];

        // Materialised, not lazy: the field guard must run when the caller asks for the conditions, not
        // whenever the statement builder happens to enumerate them. A deferred throw surfaces from inside
        // the connector, where it reads as a database fault rather than a bad rule.
        var nodes = ruleSet.Rules
            .Where(r => r.IsEnabled)
            .Select(r => Build(r, negate: false, entityType))
            .ToList();

        // The set is an AND of its rules, so one match-none child (or none at all) makes the whole set match nothing.
        if (nodes.Count == 0 || nodes.Any(n => n == null))
            return [MatchNone()];

        return DataBase.ApplyCSharpNullSemantics(nodes.Select(n => AsAndTerm(n!)).ToList()).ToList();
    }

    /// <summary>A term of a flat AND chain: an OR group carries IsOr for its children, so it is wrapped.</summary>
    private static Condition AsAndTerm(Condition node) => node.IsOr ? Condition.AndSubCondition([node]) : node;

    private static Condition? Build(IRule rule, bool negate, Type? entityType)
    {
        if (!rule.IsEnabled)
            return null;

        return rule switch
        {
            Rules.Rule leaf => ConvertLeaf(leaf, negate, entityType),
            RuleGroup group => BuildGroup(group, negate, entityType),
            _ => null
        };
    }

    private static Condition? BuildGroup(RuleGroup group, bool negate, Type? entityType)
    {
        var negated = group.IsNegated != negate;
        // NOT (A AND B) is NOT A OR NOT B: under negation the group's connective flips.
        var conjunction = (group.Logic == LogicOperator.And) != negated;

        // Every enabled child is built before any is discarded, so the field guard runs on all of them.
        var nodes = group.Rules
            .Where(r => r.IsEnabled)
            .Select(r => Build(r, negated, entityType))
            .ToList();

        // A AND <nothing> matches nothing; A OR <nothing> is A.
        if (conjunction && nodes.Any(n => n == null))
            return null;

        var children = nodes.Where(n => n != null).Select(n => n!).ToList();
        if (children.Count == 0)
            return null;

        return conjunction ? Condition.AndSubCondition(children) : Condition.OrSubCondition(children);
    }

    /// <summary>
    /// The always-false term: an <c>IN</c> over no values, which every provider renders as
    /// <see cref="Connectors.AbstractConnectorBase.AlwaysFalseSql"/> — a non-empty <c>WHERE</c>, so the
    /// connector's whole-table guard does not mistake it for "every row". The name is never rendered.
    /// </summary>
    private static Condition MatchNone() => new("1", Array.Empty<object>(), ConditionType.In);

    private static Condition? ConvertLeaf(Rules.Rule rule, bool negate, Type? entityType)
    {
        // SH-H023 (TASK-111). The single place a rule.Field becomes a Condition.Name, and therefore the
        // single place to check it — Condition.Name is interpolated raw by every strategy. With an entity
        // type this resolves against table metadata (the resolution IS the whitelist, and it fixes
        // [NamedField] remapping); without one it can only insist on a bare identifier.
        var name = entityType != null
            ? DataBase.ResolveRuleField(entityType, rule.Field)
            : DataBase.ValidateRuleFieldIdentifier(rule.Field);

        // TASK-153. Each operator below answers what RuleEvaluator (ComparisonHelper) answers for the same rule. On a NULL
        // row both follow C# (RuleExpressionConverter): this converter since TASK-864, the evaluator since TASK-881.
        var isNegated = rule.IsNegated != negate;
        // TASK-864: the column's IS NULL operand, or null where it cannot hold NULL (never assumed without metadata).
        var nullable = DataBase.RuleFieldCanBeNull(entityType, rule.Field) ? new NullableOperand(name) : null;

        // TASK-882: the column the rule names (null without metadata), for the value's stored form and the string operators.
        var column = DataBase.RuleFieldColumn(entityType, rule.Field);

        // SH-M129: Between kept only `>= lower`. Inclusive on both bounds, as the evaluator; negated by De Morgan
        // (`NOT >= lower OR NOT <= upper`) rather than a NOT around a group, as the rest of this converter does. TASK-864:
        // each bound keeps its own IsNot, so a negated Between gains the IS NULL leg C# gives it (`< lower OR > upper` had
        // none, and dropped the NULL rows). TASK-882: a null bound is C#'s lifted ordering, false (true negated), as every
        // other ordering against null; it threw ArgumentException here while both in-memory runners answered.
        if (rule.Operator == ComparisonOperator.Between)
        {
            if (!DataBase.TryRuleStoredValue(column, rule.Value, out var lowerValue)
                || !DataBase.TryRuleStoredValue(column, rule.UpperValue, out var upperValue))
                return null;
            var lower = Bound(name, lowerValue, ConditionType.GreatherAndEqual, isNegated, nullable);
            var upper = Bound(name, upperValue, ConditionType.LessAndEqual, isNegated, nullable);
            return isNegated ? Condition.OrSubCondition([lower, upper]) : Condition.AndSubCondition([lower, upper]);
        }

        // SH-M132: Like wrapped the author's pattern in %…% again, so `FV2026%` matched anywhere. TASK-882: the pattern is
        // read by RuleLikePattern, the parser every runner uses (an interior '%' is refused by name, as in memory; it ran
        // here as the literal), and Like / Contains / StartsWith / EndsWith compare as the compiled rule does,
        // OrdinalIgnoreCase, through TASK-869's exact form (SqlStringMatch) instead of the column's collation. A string
        // operator on a column that holds no text matches nothing, as in memory (it ran on the column's text here).
        if (rule.Operator == ComparisonOperator.Like)
        {
            var parsed = RuleLikePattern.Parse(rule.Value, rule.Field);
            if (parsed is not { } pattern || !DataBase.RuleColumnHoldsText(column))
                return null;
            var likeType = (pattern.AnyPrefix, pattern.AnySuffix) switch
            {
                (true, true) => ConditionType.Like,
                (false, true) => ConditionType.StartsWith,
                (true, false) => ConditionType.EndsWith,
                _ => ConditionType.TextEqual,
            };
            return IgnoringCase(name, pattern.Core, likeType, isNegated, nullable);
        }

        if (rule.Operator is ComparisonOperator.Contains or ComparisonOperator.NotContains
            or ComparisonOperator.StartsWith or ComparisonOperator.EndsWith)
        {
            // SH-M296 (TASK-171): a null needle is no predicate; both in-memory engines match nothing under any negation.
            if (rule.Value is null || !DataBase.RuleColumnHoldsText(column))
                return null;
            var methodType = rule.Operator switch
            {
                ComparisonOperator.StartsWith => ConditionType.StartsWith,
                ComparisonOperator.EndsWith => ConditionType.EndsWith,
                _ => ConditionType.Like,
            };
            var methodNot = rule.Operator == ComparisonOperator.NotContains ? !isNegated : isNegated;
            return IgnoringCase(name, rule.Value.ToString() ?? string.Empty, methodType, methodNot, nullable);
        }

        var (condType, values) = MapOperator(rule);
        var isNot = isNegated;

        // Special handling for certain operators that map to IsNot
        if (rule.Operator == ComparisonOperator.NotEqual)
        {
            condType = ConditionType.Equal;
            isNot = !isNegated; // NotEqual = Equal + IsNot (double negation if already negated)
        }
        else if (rule.Operator == ComparisonOperator.NotIn)
        {
            condType = ConditionType.In;
            isNot = !isNegated;
        }
        else if (rule.Operator == ComparisonOperator.IsNotNull)
        {
            condType = ConditionType.IsNull;
            isNot = !isNegated;
        }

        // TASK-156: a null set is no predicate (RuleExpressionConverter degrades it), so In and NotIn match nothing under any
        // negation. It used to bind no values: In matched nothing, but NotIn — and a negated In — matched every row.
        if (condType == ConditionType.In && values == null)
            return null;

        // TASK-882: each value in the column's stored form — read as the compiled rule reads it (RuleValueConverter), then
        // through AbstractField.ToStoredValue, the producer the lambda path binds through (rule 16). It was bound raw, so an
        // enum by name or a Guid / date as text matched other rows, none, or failed, by provider. A value with no form for
        // the column (an unknown enum name, text that is no Guid) matches nothing under any negation, as the compiled rule.
        if (values != null && condType != ConditionType.IsNull)
        {
            var stored = new List<object?>();
            foreach (var value in values)
            {
                if (!DataBase.TryRuleStoredValue(column, value, out var one))
                    return null;
                stored.Add(one);
            }
            values = stored.ToArray();
        }

        var condition = new Condition(name, values, condType, false, isNot);
        if (condType == ConditionType.IsNull)
            return condition;

        // SH-M130 / TASK-859: against a null value, Equal / NotEqual is the column's IS [NOT] NULL (it rendered `Field = NULL`,
        // which no row satisfies) and an ordering is C#'s constant false — always true when negated, which TASK-861's guard
        // refuses as a whole-table write. One producer with the lambda parser. On a column that cannot hold NULL that is C#'s
        // lifted answer too (TASK-882): `(int?)x.Amount != null` is every row.
        if (values == null && condType is ConditionType.Equal or ConditionType.Greather or ConditionType.GreatherAndEqual
                or ConditionType.Less or ConditionType.LessAndEqual)
        {
            DataBase.ComparedWithNull(condition);
            return condition;
        }

        if (condType == ConditionType.In)
            DataBase.MarkNullableIn(condition, nullable);
        else
            MarkNullable(condition, nullable);

        // TASK-895 (the user's decision): string Equal / NotEqual / In / NotIn are exact, as a lambda's == and Contains are —
        // one answer for a rule in SQL, compiled to C# and in RuleEvaluator.
        if (rule.Operator is ComparisonOperator.Equal or ComparisonOperator.NotEqual or ComparisonOperator.In or ComparisonOperator.NotIn)
            DataBase.MarkRuleOrdinalText(condition, entityType, rule.Field);
        return condition;
    }

    /// <summary>
    /// TASK-882 — one bound of a <c>Between</c>: against a null, C#'s constant lifted ordering (<see cref="DataBase.ComparedWithNull"/>).
    /// </summary>
    private static Condition Bound(string name, object? value, ConditionType type, bool isNot, NullableOperand? nullable)
    {
        var condition = Comparison(name, new[] { value }, type, isNot, nullable);
        if (value == null)
            DataBase.ComparedWithNull(condition);
        return condition;
    }

    /// <summary>
    /// TASK-882 — a string operator of a rule: <see cref="StringComparison.OrdinalIgnoreCase"/>, as the compiled rule calls it,
    /// rendered by the provider's exact form (TASK-869's <see cref="SqlStringMatch"/>), never the collation's <c>LIKE</c>.
    /// </summary>
    private static Condition IgnoringCase(string name, string term, ConditionType type, bool isNot, NullableOperand? nullable)
    {
        var condition = Comparison(name, new object?[] { term }, type, isNot, nullable);
        condition.Comparison = StringComparison.OrdinalIgnoreCase;
        return condition;
    }

    /// <summary>A comparison leaf over <paramref name="name"/>, marked nullable for TASK-859's legs.</summary>
    private static Condition Comparison(string name, object?[] values, ConditionType type, bool isNot, NullableOperand? nullable)
    {
        var condition = new Condition(name, values, type, false, isNot);
        MarkNullable(condition, nullable);
        return condition;
    }

    /// <summary>
    /// TASK-864: C# answers false on a NULL operand for every comparison and string test this converter builds (the
    /// string tests guard `x != null` first, as RuleExpressionConverter does), so the leg is <c>OR IS NULL</c> exactly where the
    /// leaf is negated.
    /// </summary>
    private static void MarkNullable(Condition condition, NullableOperand? nullable)
    {
        if (nullable == null)
            return;
        condition.NullableOperands = new[] { nullable };
        condition.NullOperandMatches = false;
    }

    /// <summary>The comparison operators; the string operators and Between are built before this is reached.</summary>
    private static (ConditionType type, IEnumerable? values) MapOperator(Rules.Rule rule)
    {
        return rule.Operator switch
        {
            ComparisonOperator.Equal => (ConditionType.Equal, WrapValue(rule.Value)),
            ComparisonOperator.NotEqual => (ConditionType.Equal, WrapValue(rule.Value)),
            ComparisonOperator.GreaterThan => (ConditionType.Greather, WrapValue(rule.Value)),
            ComparisonOperator.GreaterThanOrEqual => (ConditionType.GreatherAndEqual, WrapValue(rule.Value)),
            ComparisonOperator.LessThan => (ConditionType.Less, WrapValue(rule.Value)),
            ComparisonOperator.LessThanOrEqual => (ConditionType.LessAndEqual, WrapValue(rule.Value)),
            ComparisonOperator.IsNull => (ConditionType.IsNull, null),
            ComparisonOperator.IsNotNull => (ConditionType.IsNull, null),
            // SH-M131: a string is IEnumerable, so `In "ABC"` bound 'A', 'B', 'C'. The evaluator treats it as one value.
            ComparisonOperator.In => (ConditionType.In, InValues(rule.Value)),
            ComparisonOperator.NotIn => (ConditionType.In, InValues(rule.Value)),
            _ => throw new NotSupportedException(
                $"RuleConditionConverter cannot translate ComparisonOperator.{rule.Operator} on field '{rule.Field}'."),
        };
    }

    private static IEnumerable? InValues(object? value)
        => value is IEnumerable values and not string ? values : WrapValue(value);

    private static object[]? WrapValue(object? value)
    {
        return value is null ? null : [value];
    }
}
