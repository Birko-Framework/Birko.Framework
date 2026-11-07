using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>The row TASK-857's contract reads: a 4-decimal amount, a nullable one, an int and a double.</summary>
[Table("T857MathRows")]
public class MathFilterRow : AbstractModel
{
    public string? Code { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal Amount { get; set; }

    [PrecisionField(18)]
    [ScaleField(4)]
    public decimal? Optional { get; set; }

    public int Hits { get; set; }

    public double Ratio { get; set; }
}

/// <summary>
/// TASK-857 — a SQL filter with <c>Math.Round</c>, <c>Math.Abs</c> or a unary minus over the row matches the rows C# matches,
/// and any other <c>Math.*</c> / <c>decimal.*</c> call or unary operator the parser cannot translate exactly is refused by
/// name before the statement runs.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): the parser dropped the call and compared its argument,
/// so <c>Math.Round(x.Amount, 1, ToEven) == 10.1m</c> matched one row of five, <c>Math.Abs(x.Amount - 10m) &lt; 0.2m</c> none,
/// and <c>Math.Floor</c>, <c>Math.Max</c> and the rest answered just as wrongly; <c>-x.Amount == -7m</c> threw "Condition name
/// cannot be null" by accident.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a fresh, empty store.</para>
/// </summary>
public abstract class MathFilterContract
{
    protected virtual bool Ready() => true;

    /// <summary>A fresh, empty store of <see cref="MathFilterRow"/>.</summary>
    protected abstract IAsyncBulkStore<MathFilterRow> NewStore();

    private static readonly decimal[] Tenths = { 10.1m, 10.2m };

    /// <summary>Midpoints at one and zero decimals in both signs, values either side of them, and a NULL Optional (on 7).</summary>
    private static readonly decimal[] Amounts =
        { 10.05m, 10.15m, 10.25m, -10.05m, -10.15m, 10.10m, 10.14m, 10.149m, 9.5m, 10.5m, 11.5m, -2.5m, 7m, 0m };

    public static readonly IReadOnlyDictionary<string, Expression<Func<MathFilterRow, bool>>> Translated =
        new Dictionary<string, Expression<Func<MathFilterRow, bool>>>
        {
            ["Round(Amount, 1, ToEven) == 10.1"] = x => Math.Round(x.Amount, 1, MidpointRounding.ToEven) == 10.1m,
            ["Round(Amount, 1, AwayFromZero) == 10.1"] = x => Math.Round(x.Amount, 1, MidpointRounding.AwayFromZero) == 10.1m,
            ["Round(Amount, 1, ToEven) == -10.2"] = x => Math.Round(x.Amount, 1, MidpointRounding.ToEven) == -10.2m,
            ["Round(Amount, 1, AwayFromZero) == -10.2"] = x => Math.Round(x.Amount, 1, MidpointRounding.AwayFromZero) == -10.2m,
            ["Round(Amount, 0, ToEven) == 10"] = x => Math.Round(x.Amount, 0, MidpointRounding.ToEven) == 10m,
            ["Round(Amount, 0, AwayFromZero) == 11"] = x => Math.Round(x.Amount, 0, MidpointRounding.AwayFromZero) == 11m,
            ["Round(Amount, ToEven) == -2"] = x => Math.Round(x.Amount, MidpointRounding.ToEven) == -2m,
            ["Round(Amount, AwayFromZero) == 12"] = x => Math.Round(x.Amount, MidpointRounding.AwayFromZero) == 12m,
            ["decimal.Round(Amount, 1, ToEven) == 10.2"] = x => decimal.Round(x.Amount, 1, MidpointRounding.ToEven) == 10.2m,
            ["Round(Amount, 1, ToEven) < 10.1"] = x => Math.Round(x.Amount, 1, MidpointRounding.ToEven) < 10.1m,
            ["10.1 <= Round(Amount, 1, ToEven)"] = x => 10.1m <= Math.Round(x.Amount, 1, MidpointRounding.ToEven),
            ["Round(Amount, 1, ToEven) != 10.1"] = x => Math.Round(x.Amount, 1, MidpointRounding.ToEven) != 10.1m,
            ["Round(Amount * 2, 1, AwayFromZero) == 20.3"] = x => Math.Round(x.Amount * 2m, 1, MidpointRounding.AwayFromZero) == 20.3m,
            ["Round(Optional ?? 0, 1, ToEven) == 10.2"] = x => Math.Round(x.Optional ?? 0m, 1, MidpointRounding.ToEven) == 10.2m,

            ["Round(Amount, 1, ToEven) == Amount"] = x => Math.Round(x.Amount, 1, MidpointRounding.ToEven) == x.Amount,
            ["Tenths.Contains(Round(Amount, 1, ToEven))"] = x => Tenths.Contains(Math.Round(x.Amount, 1, MidpointRounding.ToEven)),
            ["CASE(Round(Amount, 1, ToEven) > 10.1) == 1"] = x => (Math.Round(x.Amount, 1, MidpointRounding.ToEven) > 10.1m ? 1m : 0m) == 1m,
            ["Abs(Amount - 10) < 0.2"] = x => Math.Abs(x.Amount - 10m) < 0.2m,
            ["Abs(Amount) == 10.05"] = x => Math.Abs(x.Amount) == 10.05m,
            ["Abs(Amount) > 10.1"] = x => Math.Abs(x.Amount) > 10.1m,
            ["Abs(Hits) == 2"] = x => Math.Abs(x.Hits) == 2,
            ["Abs(Ratio) == 0.625"] = x => Math.Abs(x.Ratio) == 0.625,
            ["Optional == null || Abs(Optional.Value) == 10.05"] = x => x.Optional == null || Math.Abs(x.Optional.Value) == 10.05m,
            ["Abs(Amount) == Amount"] = x => Math.Abs(x.Amount) == x.Amount,
            ["-Amount == -7"] = x => -x.Amount == -7m,
            ["-Amount < 0"] = x => -x.Amount < 0m,
            ["-(Amount - 10) > 0"] = x => -(x.Amount - 10m) > 0m,
            ["-Amount == Amount"] = x => -x.Amount == x.Amount,
            ["-Hits == -2"] = x => -x.Hits == -2,
            ["-Ratio < 0"] = x => -x.Ratio < 0,
            ["-Optional == null"] = x => -x.Optional == null,
            ["+Amount == 7"] = x => +x.Amount == 7m,
            ["decimal.Negate(Amount) == -7"] = x => decimal.Negate(x.Amount) == -7m,
            ["-Abs(Amount) == -10.05"] = x => -Math.Abs(x.Amount) == -10.05m,
            ["Abs(-Amount) == 10.15"] = x => Math.Abs(-x.Amount) == 10.15m,
            ["-Optional == Optional (nullable)"] = x => -x.Optional == x.Optional,
            ["Round(Amount, 1, ToEven) != Optional (nullable)"] = x => Math.Round(x.Amount, 1, MidpointRounding.ToEven) != x.Optional,
            ["Round(-Amount, 1, ToEven) == 10.2"] = x => Math.Round(-x.Amount, 1, MidpointRounding.ToEven) == 10.2m,
        };

    public static IEnumerable<object[]> TranslatedNames => Translated.Keys.Select(k => new object[] { k });

    /// <summary>Shapes with no exact SQL form on every provider, each with the name its refusal must carry.</summary>
    public static readonly IReadOnlyDictionary<string, (Expression<Func<MathFilterRow, bool>> Filter, string Named)> Refused =
        new Dictionary<string, (Expression<Func<MathFilterRow, bool>>, string)>
        {
            ["Round(Amount) (no mode)"] = (x => Math.Round(x.Amount) == 10m, "Round"),
            ["Round(Amount, 1) (no mode)"] = (x => Math.Round(x.Amount, 1) == 10.1m, "Round"),
            ["decimal.Round(Amount, 1) (no mode)"] = (x => decimal.Round(x.Amount, 1) == 10.1m, "Round"),
            ["Round(Amount, 1, ToZero)"] = (x => Math.Round(x.Amount, 1, MidpointRounding.ToZero) == 10.1m, "ToZero"),
            ["Round(Ratio, 1, ToEven) (double)"] = (x => Math.Round(x.Ratio, 1, MidpointRounding.ToEven) == 2.5, "Round"),
            ["Round(Amount, Hits, ToEven) (row digits)"] = (x => Math.Round(x.Amount, x.Hits, MidpointRounding.ToEven) == 10m, "Round"),
            ["Floor(Amount)"] = (x => Math.Floor(x.Amount) == 10m, "Floor"),
            ["Ceiling(Amount)"] = (x => Math.Ceiling(x.Amount) == 11m, "Ceiling"),
            ["Truncate(Amount)"] = (x => Math.Truncate(x.Amount) == 10m, "Truncate"),
            ["decimal.Floor(Amount)"] = (x => decimal.Floor(x.Amount) == 10m, "Floor"),
            ["Min(Amount, 10)"] = (x => Math.Min(x.Amount, 10m) == 10m, "Min"),
            ["Max(Amount, 10)"] = (x => Math.Max(x.Amount, 10m) == 10m, "Max"),
            ["Sign(Amount)"] = (x => Math.Sign(x.Amount) == 1, "Sign"),
            ["Pow(Ratio, 2)"] = (x => Math.Pow(x.Ratio, 2) == 6.25, "Pow"),
            ["Sqrt(Ratio)"] = (x => Math.Sqrt(x.Ratio) == 1.5, "Sqrt"),
            ["~Hits"] = (x => ~x.Hits == -3, "complement"),
            ["Tenths.Contains(Floor(Amount))"] = (x => Tenths.Contains(Math.Floor(x.Amount)), "Floor"),
        };

    public static IEnumerable<object[]> RefusedNames => Refused.Keys.Select(k => new object[] { k });

    private async Task<(IAsyncBulkStore<MathFilterRow> Store, List<MathFilterRow> Seed)> Seeded()
    {
        var store = NewStore();
        var seed = new List<MathFilterRow>();
        for (var i = 0; i < Amounts.Length; i++)
        {
            var amount = Amounts[i];
            var row = new MathFilterRow
            {
                Guid = Guid.NewGuid(),
                Code = "r" + i,
                Amount = amount,
                Optional = amount == 7m ? null : amount,
                Hits = i % 2 == 0 ? i - 4 : 4 - i,
                Ratio = (double)(amount / 4m),
            };
            await store.CreateAsync(row);
            seed.Add(row);
        }
        return (store, seed);
    }

    [Theory]
    [MemberData(nameof(TranslatedNames))]
    public async Task Contract_A_math_filter_matches_what_CSharp_matches(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var filter = Translated[shape];
        var expected = seed.Where(filter.Compile()).Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var read = (await store.ReadAsync(filter, null, null, null)).Select(x => x.Code).OrderBy(x => x, StringComparer.Ordinal).ToList();
        read.Should().Equal(expected, $"'{shape}' must match the rows C# matches");
        (await store.CountAsync(filter)).Should().Be(expected.Count, $"'{shape}' must count the rows C# matches");
    }

    [Theory]
    [MemberData(nameof(RefusedNames))]
    public async Task Contract_An_untranslatable_math_filter_is_refused_by_name_before_it_runs(string shape)
    {
        if (!Ready()) return;
        var (store, seed) = await Seeded();
        var (filter, named) = Refused[shape];

        var read = () => store.ReadAsync(filter, null, null, null);
        (await read.Should().ThrowAsync<NotSupportedException>($"'{shape}' has no exact SQL form"))
            .Which.Message.Should().Contain(named);

        var delete = () => store.DeleteAsync(filter);
        await delete.Should().ThrowAsync<NotSupportedException>();
        (await store.CountAsync(x => x.Code != null)).Should().Be(seed.Count, "the refused delete never ran");
    }
}
