using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// Live-backend parity of the RavenDB LINQ provider against a compiled-delegate oracle, across the filter
/// shapes catalogued in STORY-047 (strings, case-insensitivity, nested Any, IN, dates, enum/Guid/decimal
/// equality, null, negation, bare/const bool, and complex nested boolean grouping). RavenDB has no hand-rolled
/// parser — the raw <see cref="Expression"/> goes to <c>session.Query&lt;T&gt;().Where(filter)</c>.
///
/// "Correct" = C# semantics: the oracle is <c>expr.Compile()</c> over the docs AS READ BACK. RavenDB is
/// eventually consistent (auto-index lag), so each shape query is retried until its result count reaches the
/// oracle's or a timeout elapses, before comparing. Shapes the provider rejects are caught and reported.
///
/// Gated on <c>BIRKO_RAVEN_URL</c> (e.g. <c>http://localhost:8080</c>); no-op pass when absent so CI stays green.
/// </summary>
public class RavenFilterMatrixLiveTests
{
    private const string UrlEnv = "BIRKO_RAVEN_URL";
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
        };
    }

    [Fact]
    public async Task FilterShapes_MatchCompiledDelegateOracle()
    {
        var url = Environment.GetEnvironmentVariable(UrlEnv);
        if (string.IsNullOrWhiteSpace(url))
            return; // opt-in live test — set BIRKO_RAVEN_URL (e.g. http://localhost:8080) to run it

        var dbName = "birko_matrixtest_" + Guid.NewGuid().ToString("N");
        var store = new AsyncRavenDBStore<FilterModel>(url, dbName);

        try
        {
            var seed = BuildSeed();
            await store.CreateAsync(seed);

            // Wait for the collection to be indexed before reading the ground-truth set.
            List<FilterModel> all = new();
            for (int i = 0; i < 40; i++)
            {
                all = (await store.ReadAsync(x => true)).ToList();
                if (all.Count == seed.Count) break;
                await Task.Delay(500);
            }
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
                    // Retry past auto-index lag until the count settles to the oracle's (or timeout).
                    List<Guid?> actual = new();
                    for (int i = 0; i < 20; i++)
                    {
                        actual = (await store.ReadAsync(expr)).Select(d => d.Guid).OrderBy(g => g).ToList();
                        if (actual.Count == oracle.Count) break;
                        await Task.Delay(250);
                    }
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

            diverged.Should().Be(0, "RavenDB filter translation should match C# semantics:\n" + report);
        }
        finally
        {
            try { await store.DestroyAsync(); } catch { /* best-effort */ }
        }
    }
}
