using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-826 on SQLite, beyond the cross-provider contract: the formula runs in the exact decimal functions, so a value
/// with more digits than a double holds comes out as C# computes it (SQLite's own <c>/</c> converts to a double); it is
/// one UPDATE however many rows match; and a division by zero fails the statement, leaving every row as it was.
/// </summary>
public sealed class Task826ComputedSetSqLiteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-sqlite-826x-" + Guid.NewGuid().ToString("N"));

    public Task826ComputedSetSqLiteTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TestTempDirectory.Delete(_root);

    [Table("T826Wide")]
    public class Wide : AbstractModel
    {
        public string? Code { get; set; }
        public decimal Gross { get; set; }
        public decimal Net { get; set; }
    }

    private SQLiteStore<Wide> NewStore()
    {
        var store = new SQLiteStore<Wide>();
        store.SetSettings(new SqLiteSettings(_root, $"wide-{Guid.NewGuid():N}.db"));
        return store;
    }

    [Fact]
    public void Twenty_digit_values_divide_exactly_in_one_statement()
    {
        var store = NewStore();
        var grosses = new[] { 98765432109876543.21m, 12345678901234567.89m, 0.15m };
        foreach (var gross in grosses) store.Create(new Wide { Code = "w", Gross = gross });
        var statements = new ConcurrentQueue<string>();
        store.Connector!.OnExecute += text => statements.Enqueue(text);

        store.Update(x => x.Code == "w", new PropertyUpdate<Wide>()
            .Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2, MidpointRounding.ToEven)));

        statements.Where(s => s.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
            .Should().ContainSingle("three matching rows, one statement: native, not read-modify-save");
        foreach (var row in store.Read(x => x.Code == "w", null, null, null))
            row.Net.Should().Be(Math.Round(row.Gross / 1.2m, 2, MidpointRounding.ToEven), $"{row.Gross} / 1.2");
    }

    [Fact]
    public void A_division_by_zero_fails_the_statement_and_changes_nothing()
    {
        var store = NewStore();
        store.Create(new Wide { Code = "z", Gross = 5m, Net = 1m });
        store.Create(new Wide { Code = "z", Gross = 0m, Net = 1m });

        var act = () => store.Update(x => x.Code == "z", new PropertyUpdate<Wide>().Compute(x => x.Net, x => 10m / x.Gross));

        act.Should().Throw<Exception>();
        store.Read(x => x.Code == "z", null, null, null).Select(r => r.Net).Should().AllBeEquivalentTo(1m);
    }
}
