using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-1002 — the whole-table scope rule has two reductions since TASK-861: <see cref="PredicateScope"/> reduces the LINQ
/// expression for the portable stores, and <see cref="AbstractConnectorBase.WouldTargetEveryRow"/> reduces the parsed SQL
/// condition tree for the SQL stores. A form taught to one and not the other gives a whole-table write on the other family
/// with no exception (TASK-861 is that bug). One corpus, each entry labelled, is asked of both; adding a form to one
/// reduction without the other fails here.
/// </summary>
public class Task1002ScopeReductionCorpusTests
{
    [Table("T1002Rows")]
    public class Row : AbstractModel
    {
        public int A { get; set; }
        public int? Score { get; set; }
        public string? Name { get; set; }
        public bool Flag { get; set; }
    }

    public enum Scope { EveryRow, NoRow, RowDependent }

    private static readonly int[] Empty = Array.Empty<int>();
    private static readonly int[] Some = { 1, 2 };
    private static readonly int? Nul = null;
    private static readonly bool On = true;
    private static readonly bool Off = false;
    private static readonly int[]? NoneArray = null;

    /// <summary>The corpus. Add a shape here when either reduction learns one.</summary>
    public static readonly IReadOnlyDictionary<string, (Expression<Func<Row, bool>> Filter, Scope Scope)> Corpus =
        new Dictionary<string, (Expression<Func<Row, bool>>, Scope)>
        {
            // ── every row ──
            ["x => true"] = (x => true, Scope.EveryRow),
            ["x => 1 == 1"] = (x => 1 == 1, Scope.EveryRow),
            ["x => on (captured true)"] = (x => On, Scope.EveryRow),
            ["!empty.Contains(A)"] = (x => !Empty.Contains(x.A), Scope.EveryRow),
            ["!(Score > nul)"] = (x => !(x.Score > Nul), Scope.EveryRow),
            ["!(Score < nul)"] = (x => !(x.Score < Nul), Scope.EveryRow),
            ["!(nul >= Score)"] = (x => !(Nul >= x.Score), Scope.EveryRow),
            ["!(empty.Contains(A) && A > 0)"] = (x => !(Empty.Contains(x.A) && x.A > 0), Scope.EveryRow),
            ["!(A > 0 && empty.Contains(A))"] = (x => !(x.A > 0 && Empty.Contains(x.A)), Scope.EveryRow),
            ["!(Score > nul && A > 0)"] = (x => !(x.Score > Nul && x.A > 0), Scope.EveryRow),
            ["!(empty.Contains(A) || Score > nul)"] = (x => !(Empty.Contains(x.A) || x.Score > Nul), Scope.EveryRow),
            ["A > 3 || !(empty.Contains(A) && A > 1)"] = (x => x.A > 3 || !(Empty.Contains(x.A) && x.A > 1), Scope.EveryRow),
            ["A > 3 || true"] = (x => x.A > 3 || true, Scope.EveryRow),
            ["!(A > 0 && off)"] = (x => !(x.A > 0 && Off), Scope.EveryRow),
            ["!(!(!(empty.Contains(A) && A > 0)) && A > 1)"] = (x => !(!(!(Empty.Contains(x.A) && x.A > 0)) && x.A > 1), Scope.EveryRow),
            ["!(A > 0 && (A > 1 && empty.Contains(A)))"] = (x => !(x.A > 0 && (x.A > 1 && Empty.Contains(x.A))), Scope.EveryRow),
            // TASK-1011: C# 14 binds an array's Contains to MemoryExtensions, where a null array is an empty span.
            ["!none.Contains(A) (null array)"] = (x => !NoneArray!.Contains(x.A), Scope.EveryRow),

            // ── no row ──
            ["x => false"] = (x => false, Scope.NoRow),
            ["empty.Contains(A)"] = (x => Empty.Contains(x.A), Scope.NoRow),
            ["Score > nul"] = (x => x.Score > Nul, Scope.NoRow),
            ["empty.Contains(A) && A > 0"] = (x => Empty.Contains(x.A) && x.A > 0, Scope.NoRow),
            ["!(A > 3 || true)"] = (x => !(x.A > 3 || true), Scope.NoRow),
            ["!!empty.Contains(A)"] = (x => !!Empty.Contains(x.A), Scope.NoRow),
            ["none.Contains(A) (null array)"] = (x => NoneArray!.Contains(x.A), Scope.NoRow),
            // TASK-1012: an always-true group under a NOT; the SQL parser dropped the NOT and a read returned every row.
            ["!(A > 3 || on)"] = (x => !(x.A > 3 || On), Scope.NoRow),
            ["A > 0 && !(A > 3 || true)"] = (x => x.A > 0 && !(x.A > 3 || true), Scope.NoRow),

            // ── depends on the row ──
            ["A > 0"] = (x => x.A > 0, Scope.RowDependent),
            ["Score == nul (IS NULL)"] = (x => x.Score == Nul, Scope.RowDependent),
            ["Score != nul (IS NOT NULL)"] = (x => x.Score != Nul, Scope.RowDependent),
            ["some.Contains(A)"] = (x => Some.Contains(x.A), Scope.RowDependent),
            ["!some.Contains(A)"] = (x => !Some.Contains(x.A), Scope.RowDependent),
            ["Flag"] = (x => x.Flag, Scope.RowDependent),
            ["Name == null"] = (x => x.Name == null, Scope.RowDependent),
            ["empty.Contains(A) || A > 4"] = (x => Empty.Contains(x.A) || x.A > 4, Scope.RowDependent),
            ["Score > nul || A > 4"] = (x => x.Score > Nul || x.A > 4, Scope.RowDependent),
            ["!(A > 3 || empty.Contains(A))"] = (x => !(x.A > 3 || Empty.Contains(x.A)), Scope.RowDependent),
            ["!(A > 3 || Score > nul)"] = (x => !(x.A > 3 || x.Score > Nul), Scope.RowDependent),
            ["A > 0 && !empty.Contains(A)"] = (x => x.A > 0 && !Empty.Contains(x.A), Scope.RowDependent),
            ["!(empty.Contains(A) && A > 0) && A > 4"] = (x => !(Empty.Contains(x.A) && x.A > 0) && x.A > 4, Scope.RowDependent),
            ["A > 0 || !(A > 3 || true)"] = (x => x.A > 0 || !(x.A > 3 || true), Scope.RowDependent),
        };

    public static IEnumerable<object[]> Names => Corpus.Keys.Select(k => new object[] { k });

    private static bool SqlEveryRow(Expression<Func<Row, bool>> filter)
        => AbstractConnectorBase.WouldTargetEveryRow(Birko.Data.SQL.DataBase.ParseConditionExpression(filter));

    [Theory]
    [MemberData(nameof(Names))]
    public void The_expression_reduction_agrees_with_the_label(string name)
    {
        var (filter, scope) = Corpus[name];
        PredicateScope.ReducesToAllRows(filter).Should().Be(scope == Scope.EveryRow, $"'{name}' is {scope}");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_sql_condition_reduction_agrees_with_the_label(string name)
    {
        var (filter, scope) = Corpus[name];
        SqlEveryRow(filter).Should().Be(scope == Scope.EveryRow, $"'{name}' is {scope}");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_two_reductions_agree(string name)
    {
        var (filter, _) = Corpus[name];
        SqlEveryRow(filter).Should().Be(PredicateScope.ReducesToAllRows(filter),
            $"'{name}': a form one reduction knows and the other does not is a whole-table write on one store family");
    }

    /// <summary>
    /// The one recorded difference: a null <see cref="List{T}"/>. Its instance <c>Contains</c> throws in C#, so a portable
    /// store evaluating the filter fails and writes nothing, and the expression reduction does not call it every row.
    /// The SQL parser reads a null list as empty, so on the SQL side <c>!none.Contains</c> is every row, which the
    /// destructive guard refuses. Neither side writes every row, which is what the scope rule protects.
    /// </summary>
    [Fact]
    public void A_null_list_throws_in_csharp_and_is_refused_by_sql()
    {
        List<int>? none = null;
        Expression<Func<Row, bool>> filter = x => !none!.Contains(x.A);

        FluentActions.Invoking(() => filter.Compile()(new Row())).Should().Throw<NullReferenceException>();
        PredicateScope.ReducesToAllRows(filter).Should().BeFalse("C# throws, so nothing is written");
        SqlEveryRow(filter).Should().BeTrue("the SQL parser reads a null list as empty; the destructive guard refuses it");
    }
}
