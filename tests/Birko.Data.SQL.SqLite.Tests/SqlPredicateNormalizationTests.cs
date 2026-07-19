using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// Verifies the expression-normalizer follow-up (STORY-047): ternary (<c>?:</c>), null-coalescing
/// (<c>??</c>) and column arithmetic are translated correctly in Where / Delete / Update predicates,
/// and in value-position (Update SET) expressions. Predicate cases are compared against a
/// compiled-delegate oracle — the same predicate run in-memory via <c>expr.Compile()</c>, which is
/// the ground-truth C# semantics the native-LINQ backends honour — exactly like
/// <see cref="SqlExpressionParityTests"/>.
/// </summary>
public class SqlPredicateNormalizationTests : IDisposable
{
    private readonly string _root;

    public SqlPredicateNormalizationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sql-norm-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class NRow : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public int Bonus { get; set; }
        public int Total { get; set; }
        public bool Active { get; set; }
        public bool? Flag { get; set; }
        public int? Score { get; set; }
    }

    private sealed class NRowMapping : IModelMapping<NRow>
    {
        public void Configure(ModelMap<NRow> map)
        {
            map.ToTable("NRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
            map.Property(x => x.Bonus);
            map.Property(x => x.Total);
            map.Property(x => x.Active);
            map.Property(x => x.Flag);
            map.Property(x => x.Score);
        }
    }

    private AsyncSQLiteStore<NRow> NewStore()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new NRowMapping());
        registry.ApplyToDatabase();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "norm.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(NRow) });
        var store = new AsyncSQLiteStore<NRow>();
        store.SetSettings(new SqLiteSettings(_root, "norm.db"));
        return store;
    }

    private static List<NRow> Seed() => new()
    {
        //                        Amount Bonus Total Active Flag   Score
        new NRow { Name = "alpha", Amount = 1, Bonus = 0, Total = 1,  Active = true,  Flag = true,  Score = 10 },
        new NRow { Name = "beta",  Amount = 5, Bonus = 2, Total = 7,  Active = false, Flag = null,  Score = null },
        new NRow { Name = "gamma", Amount = 5, Bonus = 5, Total = 10, Active = true,  Flag = false, Score = 20 },
        new NRow { Name = "zeta",  Amount = 9, Bonus = 3, Total = 99, Active = false, Flag = true,  Score = 30 },
    };

    public static IEnumerable<object[]> PredicateCases()
    {
        var always = true; // captured closure → must funcletize to a constant, collapsing the ternary
        var cases = new (string label, Expression<Func<NRow, bool>> expr)[]
        {
            // Ternary (?:) in predicate position.
            ("ternaryBranch",   x => x.Amount > 4 ? x.Active : x.Score == null),
            ("ternaryConstFold", x => always ? x.Active : x.Amount > 100),
            ("ternaryNested",   x => (x.Amount > 4 ? x.Active : x.Score == null) && x.Name!.EndsWith("a")),

            // Null-coalescing (??).
            ("coalesceBool",    x => x.Flag ?? false),
            ("coalesceInArith", x => x.Amount + (x.Score ?? 0) > 6),
            ("coalesceOperand", x => (x.Score ?? 0) > 5),
            ("coalesceStr",     x => (x.Name ?? "z") == "beta"),

            // Value-position ternary as a comparison operand (CASE in WHERE).
            ("caseInWhere",     x => (x.Active ? x.Amount : x.Bonus) > 4),
            ("caseNullBranch",  x => (x.Score == null ? x.Bonus : x.Amount) >= 5),
            ("caseVsColumn",    x => (x.Active ? x.Amount : x.Bonus) == x.Total),
            ("caseStrBranch",   x => (x.Amount > 4 ? x.Name : "n/a") == "gamma"),

            // Column arithmetic in predicate.
            ("arithAddConst",   x => x.Amount + 1 == 6),
            ("arithMulConst",   x => x.Amount * 2 >= 10),
            ("arithSubCols",    x => x.Amount - x.Bonus > 0),
            ("arithModulo",     x => x.Bonus % 2 == 0),
            ("arithValueLeft",  x => 10 <= x.Amount * 2),
            ("arithColVsArith", x => x.Total == x.Amount + x.Bonus),
            ("arithNotEqual",   x => x.Amount + x.Bonus != 10),
            ("arithNegatedGrp", x => !(x.Amount * 2 > 10 && x.Active)),
        };
        foreach (var c in cases)
            yield return new object[] { c.label, c.expr };
    }

    [Theory]
    [MemberData(nameof(PredicateCases))]
    public async Task WherePredicate_MatchesCompiledDelegateOracle(string label, Expression<Func<NRow, bool>> expr)
    {
        var store = NewStore();
        await store.CreateAsync(Seed());
        var all = (await store.ReadAsync(x => true)).ToList();

        var oracle = all.Where(expr.Compile()).Select(r => r.Guid).OrderBy(g => g).ToList();
        var sql = (await store.ReadAsync(expr)).Select(r => r.Guid).OrderBy(g => g).ToList();

        sql.Should().Equal(oracle, $"SQL parser must match C# semantics for case '{label}'");
    }

    [Fact]
    public async Task DeletePredicate_WithColumnArithmetic_DeletesTheSameRowsAsOracle()
    {
        var store = NewStore();
        await store.CreateAsync(Seed());
        Expression<Func<NRow, bool>> predicate = x => x.Amount * 2 > 10; // Amount > 5 → only "zeta"

        var all = (await store.ReadAsync(x => true)).ToList();
        var survivorsOracle = all.Where(r => !predicate.Compile()(r)).Select(r => r.Guid).OrderBy(g => g).ToList();

        await store.DeleteAsync(predicate);

        var survivors = (await store.ReadAsync(x => true)).Select(r => r.Guid).OrderBy(g => g).ToList();
        survivors.Should().Equal(survivorsOracle);
        survivors.Should().HaveCount(3); // alpha, beta, gamma remain
    }

    [Fact]
    public async Task UpdatePredicate_WithColumnArithmetic_UpdatesTheSameRowsAsOracle()
    {
        var store = NewStore();
        await store.CreateAsync(Seed());
        Expression<Func<NRow, bool>> predicate = x => x.Amount + x.Bonus >= 10; // gamma(10), zeta(12)

        var before = (await store.ReadAsync(x => true)).ToList();
        var expectedHits = before.Where(predicate.Compile()).Select(r => r.Guid).OrderBy(g => g).ToList();

        await store.UpdateAsync(predicate, new PropertyUpdate<NRow>().Set(x => x.Name, "HIT"));

        var hits = (await store.ReadAsync(x => x.Name == "HIT")).Select(r => r.Guid).OrderBy(g => g).ToList();
        hits.Should().Equal(expectedHits);
        hits.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("coalesce", "COALESCE")]
    [InlineData("ternary", "CASE WHEN")]
    [InlineData("nullTest", "IS NULL")]
    public void ValuePosition_RendersExpectedSqlConstruct(string kind, string expectedFragment)
    {
        // ParseExpression is the value-position parser (Update SET right-hand side).
        var registry = new ModelMapRegistry();
        registry.Register(new NRowMapping());
        registry.ApplyToDatabase();

        var parameters = new Dictionary<string, object>();
        Expression<Func<NRow, int>> expr = kind switch
        {
            "coalesce" => (x => x.Score ?? 0),
            "ternary"  => (x => x.Amount > 4 ? x.Bonus : x.Amount),
            "nullTest" => (x => x.Score == null ? 1 : 0),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var sql = DataBase.ParseExpression(expr, parameters);

        sql.Should().Contain(expectedFragment);
    }
}
