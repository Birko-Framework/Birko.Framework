using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// TASK-634, measured: a document stored before its model gained a non-nullable property reads back with the property's
/// default, so RavenDB needs no repair plan when a model grows (no column schema), unlike SQL (TASK-633).
/// </summary>
/// <remarks>Gated on <c>BIRKO_RAVEN_URL</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</remarks>
public class MissingFieldReadLiveTests
{
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_RAVEN_URL");
    private readonly ITestOutputHelper _output;
    public MissingFieldReadLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Url)) return true;
        const string message = "SKIPPED: no live RavenDB. Set BIRKO_RAVEN_URL (e.g. http://localhost:8080); "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    // Two models over one collection and one document id: both resolve from typeof(T).Name, "Item" for both.
    public static class Before
    {
        public class Item : AbstractModel
        {
            public string? Name { get; set; }
        }
    }

    public static class After
    {
        public enum Stage { Draft = 0, Live = 1 }

        public class Item : AbstractModel
        {
            public string? Name { get; set; }
            public int Count { get; set; }
            public Guid Reference { get; set; }
            public bool Flag { get; set; }
            public decimal Amount { get; set; }
            public DateTime Seen { get; set; }
            public Stage State { get; set; }
        }
    }

    [Fact]
    public async Task A_document_stored_before_the_model_grew_reads_back_with_defaults()
    {
        if (!RequireServer()) return;
        var db = "birko-task634-" + Guid.NewGuid().ToString("N");
        using var before = new AsyncRavenDBStore<Before.Item>(Url!, db);
        using var after = new AsyncRavenDBStore<After.Item>(Url!, db);

        try
        {
            var guid = await before.CreateAsync(new Before.Item { Name = "old" });

            var read = await after.ReadAsync(guid, CancellationToken.None);

            read.Should().NotBeNull("the two models resolve one document id");
            read.Should().BeEquivalentTo(new After.Item { Guid = guid, Name = "old" });
        }
        finally
        {
            try { await before.DestroyAsync(); } catch { }
        }
    }
}
