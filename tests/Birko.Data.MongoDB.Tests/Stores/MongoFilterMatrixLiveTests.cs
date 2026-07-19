using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// Live-backend parity of the MongoDB driver's expression translator against a compiled-delegate oracle,
/// across the filter shapes catalogued in STORY-047 (strings, case-insensitivity, nested Any, IN, dates,
/// enum/Guid/decimal equality, null, negation, bare/const bool). MongoDB has no hand-rolled parser — the
/// raw <see cref="Expression"/> is forwarded to the driver — so parity is only assertable against a server.
///
/// "Correct" = C# semantics: the oracle is <c>expr.Compile()</c> run over the docs AS READ BACK, so any
/// serialization round-trip (DateTime/decimal/enum) is neutralised and only genuine translation divergences
/// surface. Any shape the translator rejects is caught and reported (not silently swallowed).
///
/// Gated on <c>BIRKO_MONGO_HOST</c> (e.g. <c>localhost</c>); no-op pass when absent so CI stays green.
/// </summary>
public class MongoFilterMatrixLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public enum Status { New, Active, Closed }
    public class Line { public int Qty { get; set; } }
    public class Addr { public string? City { get; set; } }

    public class FilterModel : AbstractModel
    {
        public string? Name { get; set; }
        public int? Score { get; set; }
        public int Amount { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedAt { get; set; }
        public Status Status { get; set; }
        public decimal Price { get; set; }
        public List<Line> Lines { get; set; } = new();
        public Addr? Address { get; set; }
    }

    internal static List<FilterModel> BuildSeed() => new()
    {
        new() { Guid = Guid.NewGuid(), Name = "alpha", Score = 10,   Amount = 1, Active = true,  CreatedAt = Base.AddDays(0), Status = Status.New,    Price = 9.99m,  Lines = new() { new() { Qty = 2 } },                    Address = new() { City = "Praha" } },
        new() { Guid = Guid.NewGuid(), Name = "beta",  Score = null, Amount = 5, Active = false, CreatedAt = Base.AddDays(1), Status = Status.Active, Price = 19.50m, Lines = new() { new() { Qty = 7 } },                    Address = new() { City = "Brno" } },
        new() { Guid = Guid.NewGuid(), Name = "gamma", Score = 20,   Amount = 5, Active = true,  CreatedAt = Base.AddDays(2), Status = Status.Active, Price = 5.00m,  Lines = new() { new() { Qty = 1 }, new() { Qty = 9 } },  Address = new() { City = "Praha" } },
        new() { Guid = Guid.NewGuid(), Name = "zeta",  Score = null, Amount = 9, Active = false, CreatedAt = Base.AddDays(3), Status = Status.Closed, Price = 100m,   Lines = new(),                                          Address = null },
        new() { Guid = Guid.NewGuid(), Name = "Beta",  Score = 30,   Amount = 2, Active = true,  CreatedAt = Base.AddDays(4), Status = Status.New,    Price = 0.50m,  Lines = new() { new() { Qty = 6 } },                    Address = new() { City = "Ostrava" } },
        new() { Guid = Guid.NewGuid(), Name = "delta", Score = 40,   Amount = 7, Active = false, CreatedAt = Base.AddDays(5), Status = Status.Active, Price = 50m,    Lines = new() { new() { Qty = 3 } },                    Address = new() { City = "Brno" } },
    };

    /// <summary>Builds the shape matrix. Runtime-derived constants (Guid) are captured from the read-back set.</summary>
    internal static (string label, Expression<Func<FilterModel, bool>> expr)[] Shapes(Guid guidTarget)
    {
        var amounts = new[] { 1, 5 };
        var d1 = Base.AddDays(1);
        var d2 = Base.AddDays(2);
        var d4 = Base.AddDays(4);
        return new (string, Expression<Func<FilterModel, bool>>)[]
        {
            ("bareBool",     x => x.Active),
            ("constTrue",    x => true),
            ("negation",     x => !x.Active),
            ("rangeAmount",  x => x.Amount > 4 && x.Amount <= 7),
            ("inClosure",    x => amounts.Contains(x.Amount)),
            ("enumEq",       x => x.Status == Status.Active),
            ("guidEq",       x => x.Guid == guidTarget),
            ("decimalCmp",   x => x.Price >= 50m),
            ("startsWith",   x => x.Name!.StartsWith("a")),
            ("endsWith",     x => x.Name!.EndsWith("ta")),
            ("contains",     x => x.Name!.Contains("et")),
            ("toLowerEq",    x => x.Name!.ToLower() == "beta"),
            ("dateRange",    x => x.CreatedAt >= d1 && x.CreatedAt < d4),
            ("dateDotDate",  x => x.CreatedAt.Date == d2),
            ("nestedAny",    x => x.Lines.Any(l => l.Qty > 5)),
            ("nestedMember", x => x.Address != null && x.Address.City == "Brno"),
            ("eqNull",       x => x.Score == null),
            ("notEqNull",    x => x.Score != null),

            // Complex nested boolean grouping — verifies AND/OR precedence is preserved, not flattened.
            ("grpOrAnd",     x => (x.Active || x.Amount > 6) && (x.Status == Status.Active || x.Score == null)),
            ("grpAndOr",     x => (x.Active && x.Amount < 3) || (!x.Active && x.Amount > 6)),
            ("deMorgan",     x => !(x.Active && x.Amount > 4)),
            ("deepNest",     x => x.Active || (x.Amount > 4 && (x.Status == Status.Active || x.Name!.StartsWith("z")))),
            ("mixedNot",     x => x.Score != null && !(x.Status == Status.Closed) && (x.Amount <= 2 || x.Amount >= 7)),

            // STORY-047 follow-up: ternary / null-coalescing / column arithmetic. The document backends have
            // no hand-rolled parser — the driver's LINQ translator handles these natively — so these shapes
            // confirm the driver agrees with the compiled-delegate oracle. A THROW/DIVERGE here is a
            // recordable per-backend finding (e.g. arithmetic-in-filter unsupported by the driver).
            ("ternary",      x => (x.Amount > 4 ? x.Active : x.Score == null)),
            ("coalesceCmp",  x => (x.Score ?? 0) > 15),
            ("arithAdd",     x => x.Amount + (x.Score ?? 0) > 10),
            ("arithMul",     x => x.Amount * 2 >= 10),
        };
    }

    [Fact]
    public async Task FilterShapes_MatchCompiledDelegateOracle()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host))
            return; // opt-in live test — set BIRKO_MONGO_HOST (e.g. localhost) to run it

        var dbName = "birko_matrixtest_" + Guid.NewGuid().ToString("N");
        var store = new AsyncMongoDBStore<FilterModel>();
        store.SetSettings(new Settings(host, dbName));

        try
        {
            var seed = BuildSeed();
            await store.CreateAsync(seed);
            var all = (await store.ReadAsync(x => true)).ToList();
            all.Should().HaveCount(seed.Count);

            var guidTarget = all.First(d => d.Name == "gamma").Guid!.Value;

            var report = new StringBuilder();
            int diverged = 0;
            foreach (var (label, expr) in Shapes(guidTarget))
            {
                var oracle = all.Where(expr.Compile()).Select(d => d.Guid).OrderBy(g => g).ToList();
                string line;
                try
                {
                    var actual = (await store.ReadAsync(expr)).Select(d => d.Guid).OrderBy(g => g).ToList();
                    var ok = oracle.SequenceEqual(actual);
                    if (!ok) diverged++;
                    line = ok ? "OK" : $"DIVERGE oracle={oracle.Count} actual={actual.Count}";
                }
                catch (Exception e)
                {
                    diverged++;
                    line = "THROW " + e.GetType().Name + ": " + e.Message.Split('\n')[0];
                }
                report.AppendLine($"{label,-14} -> {line}");
            }

            diverged.Should().Be(0, "MongoDB filter translation should match C# semantics:\n" + report);
        }
        finally
        {
            try { await store.DestroyAsync(); } catch { /* best-effort */ }
            try { store.Client?.Database.Client.DropDatabase(dbName); } catch { /* best-effort */ }
        }
    }
}
