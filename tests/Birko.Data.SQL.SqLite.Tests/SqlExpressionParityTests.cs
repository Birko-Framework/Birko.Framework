using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// Behavioural parity of the SQL hand-rolled filter parser (Birko.Data.SQL DataBase.ParseConditionExpression)
/// against a compiled-delegate oracle — the same predicate run in-memory via <c>expr.Compile()</c>, which is
/// the ground-truth C# semantics that the native-LINQ backends (InMemory / JSON / XML / RavenDB / CosmosDB)
/// honour.
///
/// Each filter shape is executed end-to-end against a real on-disk SQLite database through the store; the
/// returned Guid set must equal the oracle's. This is the positive counterpart to
/// Birko.Data.ElasticSearch.Tests.ExpressionDivergenceTests, which pins the ElasticSearch parser's gaps on the
/// SAME matrix. Every case here passes, i.e. the SQL parser matches reference semantics across the board.
/// </summary>
public class SqlExpressionParityTests : IDisposable
{
    private readonly string _root;

    public SqlExpressionParityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sql-parity-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public bool Active { get; set; }
        public int? Score { get; set; }
    }

    private sealed class RowMapping : IModelMapping<Row>
    {
        public void Configure(ModelMap<Row> map)
        {
            map.ToTable("Rows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
            map.Property(x => x.Active);
            map.Property(x => x.Score);
        }
    }

    private AsyncSQLiteStore<Row> NewStore()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "parity.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });
        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "parity.db"));
        return store;
    }

    private static readonly int[] Ids = { 1, 5 };

    /// <summary>Empty by design — TASK-137's empty-`IN` / empty-`NOT IN` cases below.</summary>
    private static readonly int[] NoIds = Array.Empty<int>();

    // TASK-213: sets chosen to make the computed-operand cases match POSITIVELY, not merely return nothing.
    // Seed amounts are 1/5/5/9, so `Amount + 1` is 2/6/6/10 and this hits three rows; scores are
    // 10/null/20/30, so `Score ?? 0` is 10/0/20/30 and this hits two.
    private static readonly int[] PlusOneHits = { 6, 10 };
    private static readonly int[] ScoreHits = { 0, 20 };

    // Each case: a label and the predicate. The oracle is expr.Compile() over the same seed.
    public static IEnumerable<object[]> Cases()
    {
        var cases = new (string label, Expression<Func<Row, bool>> expr)[]
        {
            ("bareBool",      x => x.Active),
            ("constTrue",     x => true),
            ("constFalse",    x => false),
            ("endsWith",      x => x.Name!.EndsWith("a")),
            ("startsWith",    x => x.Name!.StartsWith("a")),
            ("strContains",   x => x.Name!.Contains("et")),
            ("toLowerEq",     x => x.Name!.ToLower() == "beta"),
            ("inPattern",     x => Ids.Contains(x.Amount)),
            ("eqNull",        x => x.Score == null),
            ("notEqNull",     x => x.Score != null),
            ("bitwiseAnd",    x => x.Active & (x.Amount > 1)),
            ("andAlso",       x => x.Active && x.Amount > 1),
            ("orElse",        x => x.Amount == 1 || x.Amount == 9),
            ("notEq",         x => x.Amount != 5),
            ("hasValue",      x => x.Score.HasValue),
            ("greaterThan",   x => x.Amount > 4),
            ("lessOrEqual",   x => x.Amount <= 5),

            // Complex nested boolean grouping — verifies the hand-rolled parser preserves AND/OR
            // precedence and does not flatten (a || b) && (c || d) into a || (b && c) || d etc.
            ("grpOrAnd",      x => (x.Active || x.Amount > 6) && (x.Score == null || x.Name!.StartsWith("a"))),
            ("grpAndOr",      x => (x.Active && x.Amount < 3) || (!x.Active && x.Amount > 6)),
            ("deMorgan",      x => !(x.Active && x.Amount > 4)),
            ("deepNest",      x => x.Active || (x.Amount > 4 && (x.Score != null || x.Name!.EndsWith("a")))),
            ("mixedNot",      x => x.Score != null && !(x.Name == "beta") && (x.Amount <= 2 || x.Amount >= 7)),
            ("orOfAnds",      x => (x.Active && x.Score != null) || (x.Amount == 9) || (x.Name!.StartsWith("b") && !x.Active)),

            // TASK-137 — an empty NOT IN matches every row and is now REDUCED AWAY rather than rendered as
            // `1 = 1`. The reduction rewrites the SQL for every shape below (dropping the term from an AND
            // run, collapsing an OR chain, flipping a negated group to always-false), so the oracle is the
            // check that actually matters: C# `!empty.Contains(x)` is true of every row, and the SQL must
            // agree after being rewritten. `emptyIn` is the asymmetric twin — always-FALSE terms cannot be
            // dropped (`A AND FALSE` is `FALSE`, not `A`) and keep their `1 = 0`.
            ("emptyNotIn",       x => !NoIds.Contains(x.Amount)),
            ("emptyIn",          x => NoIds.Contains(x.Amount)),
            ("emptyNotInAnd",    x => x.Amount > 4 && !NoIds.Contains(x.Amount)),
            ("emptyNotInOr",     x => x.Amount > 4 || !NoIds.Contains(x.Amount)),
            ("emptyNotInNotOr",  x => !(x.Amount > 4 || !NoIds.Contains(x.Amount))),
            ("emptyNotInNotAnd", x => !(x.Amount > 4 && !NoIds.Contains(x.Amount))),
            ("emptyNotInNested", x => x.Amount == 9 || (x.Name!.StartsWith("a") && !NoIds.Contains(x.Amount))),
            // Two always-true terms, so the whole AND group reduces rather than just losing one term.
            // Deliberately over a PLAIN column twice: any COMPUTED operand inside Contains
            // (`x.Amount + 1`, `x.Score ?? 0`) is translated wrongly TODAY, independently of this task —
            // the operand is discarded and replaced by a subcondition, so `SomeIds.Contains(x.Amount + 1)`
            // over a NON-empty set answers 1 where the oracle says 0. Asserting it here would have had to
            // encode that defect to stay green, which blesses it; it is filed as TASK-213 instead.
            ("emptyNotInTwice",  x => !NoIds.Contains(x.Amount) && !NoIds.Contains(x.Amount)),
            ("emptyBothKinds",   x => NoIds.Contains(x.Amount) || !NoIds.Contains(x.Amount)),

            // TASK-213 — a COMPUTED operand inside a set `Contains` used to be recursed into as though it
            // were a nested predicate, which fabricated a subcondition and made the renderer emit a
            // DIFFERENT predicate: `Ids.Contains(x.Amount + 1)` answered 1 row where C# says 0, and the
            // negated form 3 where C# says 4. The operand is now resolved as a value expression, the same
            // way a comparison resolves its column side. The oracle is the whole point here — these shapes
            // are exactly the ones where "looks like plausible SQL" and "means what C# means" diverged.
            ("containsArith",        x => Ids.Contains(x.Amount + 1)),
            ("containsArithNot",     x => !Ids.Contains(x.Amount + 1)),
            ("containsArithMatches", x => PlusOneHits.Contains(x.Amount + 1)),
            ("containsMultiply",     x => Ids.Contains(x.Amount * 1)),
            ("containsCoalesce",     x => Ids.Contains(x.Score ?? 0)),
            ("containsCoalesceNot",  x => !Ids.Contains(x.Score ?? 0)),
            ("containsCoalesceHits", x => ScoreHits.Contains(x.Score ?? 0)),
            ("containsArithInAnd",   x => x.Amount > 4 && Ids.Contains(x.Amount + 1)),
            ("containsArithInOr",    x => x.Amount > 8 || Ids.Contains(x.Amount + 1)),
            // Composes with TASK-137: an empty NOT IN over a computed operand is still always-true, so it
            // must reduce away rather than emit the fabricated predicate the old path produced.
            ("containsArithEmptyNot", x => !NoIds.Contains(x.Amount + 1)),
            ("containsArithEmptyIn",  x => NoIds.Contains(x.Amount + 1)),
        };
        foreach (var c in cases)
            yield return new object[] { c.label, c.expr };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task SqlFilter_MatchesCompiledDelegateOracle(string label, Expression<Func<Row, bool>> expr)
    {
        var store = NewStore();
        var seed = new List<Row>
        {
            new Row { Name = "alpha", Amount = 1, Active = true,  Score = 10 },
            new Row { Name = "beta",  Amount = 5, Active = false, Score = null },
            new Row { Name = "gamma", Amount = 5, Active = true,  Score = 20 },
            new Row { Name = "zeta",  Amount = 9, Active = false, Score = 30 },
        };
        await store.CreateAsync(seed);
        var all = (await store.ReadAsync(x => true)).ToList();

        var oracle = all.Where(expr.Compile()).Select(r => r.Guid).OrderBy(g => g).ToList();
        var sql = (await store.ReadAsync(expr)).Select(r => r.Guid).OrderBy(g => g).ToList();

        sql.Should().Equal(oracle, $"SQL parser must match C# semantics for case '{label}'");
    }
}
