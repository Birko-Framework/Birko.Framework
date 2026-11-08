using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-856 — on SQLite a <c>decimal</c> value fragment (TASK-213: arithmetic, <c>COALESCE</c>, <c>CASE</c> over the row)
/// in a filter is computed and compared as a decimal, as the column is.
/// <para>
/// Before (measured on this seed): <c>Small.Contains(x.Amount + 0m)</c> matched 0 rows where C# matches 3, <c>==</c>
/// matched none, <c>x.Amount + 0m &lt; 6m</c> every row, <c>&gt;</c> none, <c>(x.Optional ?? 0m) &lt; 8m</c> and a
/// <c>CASE</c> compared byte-wise (every row), and <c>x.Amount + 0m == x.Amount</c> missed the two values a double cannot
/// hold. The portable fragment <c>(Amount + 0)</c> is a REAL, which SQLite orders before every TEXT a decimal parameter
/// binds as. PostgreSQL, MySQL and SQL Server answered every shape here correctly, live, and keep the portable form.
/// </para>
/// </summary>
public sealed class DecimalValueFragmentComparisonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t856-{Guid.NewGuid():N}");

    public DecimalValueFragmentComparisonTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TestTempDirectory.Delete(_root);

    [Table("T856Ledger")]
    public class Ledger : AbstractModel
    {
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public decimal? Optional { get; set; }
        public int Hits { get; set; }
    }

    private static readonly decimal Big = 12345678901234567.89m;
    private static readonly decimal[] Small = { 10.10m, 5.25m };
    private static readonly decimal[] Zero = { 0m };

    private static readonly IReadOnlyDictionary<string, Expression<Func<Ledger, bool>>> Shapes =
        new Dictionary<string, Expression<Func<Ledger, bool>>>
        {
            ["Contains(Amount + 0)"] = x => Small.Contains(x.Amount + 0m),
            ["Contains(Amount * 1)"] = x => Small.Contains(x.Amount * 1m),
            ["!Contains(Amount + 0)"] = x => !Small.Contains(x.Amount + 0m),
            ["none || (Contains(Amount + 0) && true)"] = x => x.Name == "none" || (Small.Contains(x.Amount + 0m) && true),
            ["none || (Amount + 0 < 6 && true)"] = x => x.Name == "none" || (x.Amount + 0m < 6m && true),
            ["Amount + 0 == 10.10"] = x => x.Amount + 0m == 10.10m,
            ["Amount * 1 == 10.1"] = x => x.Amount * 1m == 10.1m,
            ["Amount + 0.20 == 10.30"] = x => x.Amount + 0.20m == 10.30m,
            ["Amount + 0 == big"] = x => x.Amount + 0m == Big,
            ["Amount + 0 != 7"] = x => x.Amount + 0m != 7m,
            ["Amount + 0 < 6"] = x => x.Amount + 0m < 6m,
            ["Amount + 0 > 6"] = x => x.Amount + 0m > 6m,
            ["6 < Amount + 0"] = x => 6m < x.Amount + 0m,
            ["Amount / 2 == 3.5"] = x => x.Amount / 2m == 3.5m,
            ["Amount % 3 == 1"] = x => x.Amount % 3m == 1m,
            ["Amount + Hits == 20.1"] = x => x.Amount + x.Hits == 20.1m,
            ["(decimal)(Hits * 2) == 20"] = x => (decimal)(x.Hits * 2) == 20m,
            ["Amount + 0 == Amount"] = x => x.Amount + 0m == x.Amount,
            ["Amount + 0 >= Amount"] = x => x.Amount + 0m >= x.Amount,
            ["Optional + 0 == null"] = x => x.Optional + 0m == null,
            ["(Optional ?? 0) < 8"] = x => (x.Optional ?? 0m) < 8m,
            ["Contains0(Optional ?? 0)"] = x => Zero.Contains(x.Optional ?? 0m),
            ["CASE < 8"] = x => (x.Amount > 6m ? x.Amount : 0m) < 8m,
            ["CASE(Amount + 1 > 8) == 1"] = x => (x.Amount + 1m > 8m ? 1m : 0m) == 1m,
        };

    public static IEnumerable<object[]> ShapeNames => Shapes.Keys.Select(k => new object[] { k });

    private async Task<(AsyncSQLiteStore<Ledger> Store, List<Ledger> Seed)> Seeded()
    {
        var store = new AsyncSQLiteStore<Ledger>();
        store.SetSettings(new SqLiteSettings(_root, "t856.db"));
        var seed = new List<Ledger>();
        // Two values a double cannot hold apart, a trailing zero, a NULL Optional (on 7) and a zero.
        foreach (var amount in new[] { 10.10m, 10.1m, 5.25m, 0.30m, Big, Big - 0.01m, 7m, 10.14m, 0m })
        {
            var row = new Ledger
            {
                Guid = Guid.NewGuid(),
                Name = amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Amount = amount,
                Optional = amount == 7m ? null : amount,
                Hits = amount > 1000m ? 0 : (int)amount,
            };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    [Theory]
    [MemberData(nameof(ShapeNames))]
    public async Task ADecimalValueFragment_MatchesWhatCSharpMatches(string shape)
    {
        var (store, seed) = await Seeded();
        var filter = Shapes[shape];
        var expected = seed.Where(filter.Compile()).Select(x => x.Guid!.Value).OrderBy(x => x).ToList();

        var read = (await store.ReadAsync(filter)).Select(x => x.Guid!.Value).OrderBy(x => x).ToList();
        read.Should().Equal(expected, $"'{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count);
    }

    [Fact]
    public async Task ADecimalFragmentConvertedToAnotherType_IsRefusedByName_BeforeItRuns()
    {
        var (store, _) = await Seeded();

        var act = () => store.ReadAsync(x => (int)(x.Amount * 2m) == 20);

        // TASK-860: refused at parse time on every provider now (a narrowing cast is no SQL anywhere), so the message is the
        // parser's, not SQLite's dialect's.
        (await act.Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*conversion from Decimal to Int32*");
    }

    [Fact]
    public void ThePortableFragment_OtherProvidersRun_IsUnchanged()
    {
        var condition = Birko.Data.SQL.DataBase.ParseConditionExpression((Expression<Func<Ledger, bool>>)(x => Small.Contains(x.Amount + 0m))).Single();

        condition.Name.Should().Be("(T856Ledger.Amount + 0)");
        condition.NameFragment.Should().NotBeNull();
        condition.NameFragment!(Birko.Data.SQL.Conditions.SqlValueFragmentDialect.Portable).Should().Be(condition.Name);
        condition.NameFragment!(SqLiteValueFragmentDialect.Instance)
            .Should().Be("birko_decimal_add(T856Ledger.Amount, '0.0') COLLATE BIRKO_DECIMAL");
    }

    /// <summary>A connector that splits an IN past 10 values: the chunks are copies of the condition.</summary>
    private sealed class ChunkingConnector : SqLiteConnector
    {
        public ChunkingConnector(Birko.Configuration.PasswordSettings settings) : base(settings) { }
        public override int MaxCommandParameters => 40;
        public override int PreferredInListChunkSize => 10;
    }

    private sealed class ChunkingStore : AsyncSQLiteStore<Ledger>
    {
        public override void SetSettings(Birko.Configuration.PasswordSettings settings)
        {
            base.SetSettings(settings);
            Connector = new ChunkingConnector(settings);
        }
    }

    [Fact]
    public async Task AChunkedInList_KeepsTheDecimalComparison()
    {
        var (_, seed) = await Seeded();
        var store = new ChunkingStore();
        store.SetSettings(new SqLiteSettings(_root, "t856.db"));
        // 60 values, so the list is split; the matching ones are spread over several chunks.
        var many = Enumerable.Range(1, 60).Select(i => i + 0.5m).Concat(new[] { 10.10m, 0.30m, Big }).ToArray();
        Expression<Func<Ledger, bool>> filter = x => many.Contains(x.Amount + 0m);

        var expected = seed.Where(filter.Compile()).Select(x => x.Guid!.Value).OrderBy(x => x).ToList();
        expected.Should().HaveCount(4, "10.10 twice, 0.30 and Big");
        var read = (await store.ReadAsync(filter)).Select(x => x.Guid!.Value).OrderBy(x => x).ToList();
        read.Should().Equal(expected);
    }
}
