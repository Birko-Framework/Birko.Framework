using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.Stores;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// TASK-882 — a rule run in SQL (<see cref="RuleConditionConverter"/>) and the rule compiled to C#
/// (<see cref="RuleExpressionConverter"/>) run through the store's lambda path select, count and delete the rows the
/// equivalent C# lambda selects (<see cref="RuleLeftoverShapes"/>), or both refuse the shape by name. The in-memory
/// runners are measured on the same shapes by <c>tests/Birko.Rules.Tests/Task882RuleLeftoversTests.cs</c>.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): a <c>Between</c> with a null bound threw
/// <see cref="ArgumentException"/> where C# answers; an interior <c>%</c> ran as the literal; <c>Like</c>, <c>Contains</c>,
/// <c>StartsWith</c> and <c>EndsWith</c> followed the column's collation where the compiled rule ignores case ordinally; a
/// string operator on a non-string column ran on its text; and a value in another form than the column's (an enum by
/// name, a Guid or a date as text) was bound raw, so it matched other rows, none, or failed.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class RuleLeftoversContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="RuleLeftoverRow"/> and its connector.</summary>
    protected abstract (IBulkStore<RuleLeftoverRow> Store, AbstractConnector Connector) NewStore();

    private (IBulkStore<RuleLeftoverRow> Store, AbstractConnector Connector) Seeded()
    {
        var (store, connector) = NewStore();
        store.Create(RuleLeftoverShapes.Rows());
        return (store, connector);
    }

    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in RuleLeftoverShapes.Cases())
            data.Add((string)c[0], (string)c[1]);
        return data;
    }

    private static int[] Nums(IEnumerable<object> rows) => RuleLeftoverShapes.Nums(rows.Cast<RuleLeftoverRow>());

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_sql_rule_reads_what_the_lambda_selects(string shape, string form)
    {
        if (!Ready()) return;
        var rule = RuleLeftoverShapes.Build(shape, form);
        var expected = RuleLeftoverShapes.Expected(shape, form);
        var (_, connector) = Seeded();

        if (expected == null)
        {
            Action convert = () => RuleConditionConverter.ToConditions<RuleLeftoverRow>(rule).ToList();
            convert.Should().Throw<NotSupportedException>($"{form} {shape}").WithMessage("*Like*");
            return;
        }

        var conditions = RuleConditionConverter.ToConditions<RuleLeftoverRow>(rule).ToList();
        Nums(connector.Select(typeof(RuleLeftoverRow), conditions)).Should().Equal(expected, $"{form} {shape}: read");
        connector.SelectCount(typeof(RuleLeftoverRow), conditions).Should().Be(expected.Length, $"{form} {shape}: count");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_sql_rule_deletes_what_the_lambda_selects(string shape, string form)
    {
        if (!Ready()) return;
        var rule = RuleLeftoverShapes.Build(shape, form);
        var expected = RuleLeftoverShapes.Expected(shape, form);
        if (expected == null) return; // refused before anything runs: the read test asserts it
        var (_, connector) = Seeded();
        var all = RuleLeftoverShapes.Rows().Select(r => r.Num).ToArray();

        var conditions = RuleConditionConverter.ToConditions<RuleLeftoverRow>(rule).ToList();
        try
        {
            connector.Delete(typeof(RuleLeftoverRow), conditions);
        }
        catch (Birko.Data.Exceptions.WholeTableWriteException) when (expected.Length == all.Length)
        {
            // Every row, and a filter that reduces to it: the connector's whole-table guard refuses it (TASK-861).
            Nums(connector.Select(typeof(RuleLeftoverRow), (IEnumerable<Condition>?)null)).Should().Equal(all);
            return;
        }

        Nums(connector.Select(typeof(RuleLeftoverRow), (IEnumerable<Condition>?)null))
            .Should().Equal(all.Except(expected).OrderBy(n => n), $"{form} {shape}: delete");
    }

    /// <summary>The compiled rule is a lambda: the store's lambda path translates it to the rows C# selects.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_compiled_rule_reads_what_the_lambda_selects_through_the_store(string shape, string form)
    {
        if (!Ready()) return;
        var rule = RuleLeftoverShapes.Build(shape, form);
        var expected = RuleLeftoverShapes.Expected(shape, form);
        if (expected == null) return; // refused by RuleExpressionConverter itself: Task882RuleLeftoversTests
        var (store, _) = Seeded();

        var filter = RuleExpressionConverter.ToExpression<RuleLeftoverRow>(rule);
        RuleLeftoverShapes.Nums(store.Read(filter)).Should().Equal(expected, $"{form} {shape}: compiled rule through the store");
    }
}
