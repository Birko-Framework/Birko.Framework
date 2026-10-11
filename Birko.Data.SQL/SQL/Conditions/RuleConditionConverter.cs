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

    private static IEnumerable<Condition> Convert(IRule rule, Type? entityType)
    {
        var node = Build(rule, negate: false, entityType);
        return [node == null ? MatchNone() : AsAndTerm(node)];
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

        return nodes.Select(n => AsAndTerm(n!)).ToList();
    }

    /// <summary>A term of a flat AND chain: an OR group carries IsOr for its children, so it is wrapped.</summary>
    private static Condition AsAndTerm(Condition node) => node.IsOr ? Condition.AndSubCondition([node]) : node;

    private static Condition? Build(IRule rule, bool negate, Type? entityType)
    {
        if (!rule.IsEnabled)
            return null;

        return rule switch
        {
            Rules.Rule leaf => NegateLeaf(ConvertLeaf(leaf, entityType), negate),
            RuleGroup group => BuildGroup(group, negate, entityType),
            _ => null
        };
    }

    private static Condition NegateLeaf(Condition leaf, bool negate)
    {
        if (negate)
            leaf.IsNot = !leaf.IsNot;
        return leaf;
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

    private static Condition ConvertLeaf(Rules.Rule rule, Type? entityType)
    {
        // SH-H023 (TASK-111). The single place a rule.Field becomes a Condition.Name, and therefore the
        // single place to check it — Condition.Name is interpolated raw by every strategy. With an entity
        // type this resolves against table metadata (the resolution IS the whitelist, and it fixes
        // [NamedField] remapping); without one it can only insist on a bare identifier.
        var name = entityType != null
            ? DataBase.ResolveRuleField(entityType, rule.Field)
            : DataBase.ValidateRuleFieldIdentifier(rule.Field);

        var (condType, values) = MapOperator(rule);
        var isNot = rule.IsNegated;

        // Special handling for certain operators that map to IsNot
        if (rule.Operator == ComparisonOperator.NotEqual)
        {
            condType = ConditionType.Equal;
            isNot = !rule.IsNegated; // NotEqual = Equal + IsNot (double negation if already negated)
        }
        else if (rule.Operator == ComparisonOperator.NotContains)
        {
            condType = ConditionType.Like;
            isNot = !rule.IsNegated;
        }
        else if (rule.Operator == ComparisonOperator.NotIn)
        {
            condType = ConditionType.In;
            isNot = !rule.IsNegated;
        }
        else if (rule.Operator == ComparisonOperator.IsNotNull)
        {
            condType = ConditionType.IsNull;
            isNot = !rule.IsNegated;
        }

        return new Condition(name, values, condType, false, isNot);
    }

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
            ComparisonOperator.Between => (ConditionType.GreatherAndEqual, WrapValue(rule.Value)),
            ComparisonOperator.IsNull => (ConditionType.IsNull, null),
            ComparisonOperator.IsNotNull => (ConditionType.IsNull, null),
            ComparisonOperator.Contains => (ConditionType.Like, WrapValue(rule.Value)),
            ComparisonOperator.NotContains => (ConditionType.Like, WrapValue(rule.Value)),
            ComparisonOperator.StartsWith => (ConditionType.StartsWith, WrapValue(rule.Value)),
            ComparisonOperator.EndsWith => (ConditionType.EndsWith, WrapValue(rule.Value)),
            ComparisonOperator.Like => (ConditionType.Like, WrapValue(rule.Value)),
            ComparisonOperator.In => (ConditionType.In, rule.Value as IEnumerable ?? WrapValue(rule.Value)),
            ComparisonOperator.NotIn => (ConditionType.In, rule.Value as IEnumerable ?? WrapValue(rule.Value)),
            _ => (ConditionType.Equal, WrapValue(rule.Value))
        };
    }

    private static object[]? WrapValue(object? value)
    {
        return value is null ? null : [value];
    }
}
