using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-218 — end-to-end proof that an array-typed <c>IN</c> filter reaches the server and matches the
/// right rows. On .NET 9+ <c>arr.Contains(x.Col)</c> binds to <c>MemoryExtensions.Contains</c>, which the
/// driver's LINQ translator rejected with <c>NotSupportedException: Specified method is not supported</c>.
///
/// The unit half is <c>Birko.Data.Core.Tests.SpanContainsTests</c>, which pins the rewrite itself and is
/// non-gated. This adds what a render check cannot: that the rewritten filter selects the intended
/// documents rather than merely translating. Both directions are asserted — a rewrite that matched
/// everything would satisfy "no longer throws" while being just as wrong.
///
/// Gated on <c>BIRKO_MONGO_HOST</c>; no-op pass when absent so CI stays green.
/// </summary>
public class MongoArrayContainsLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";

    public class Doc : AbstractModel { public int Amount { get; set; } public string? Name { get; set; } }

    [Fact]
    public async Task An_array_backed_IN_filter_selects_the_right_documents()
    {
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host)) return;

        var settings = new Settings(host, "birko_task218_" + Guid.NewGuid().ToString("N"));
        var store = new AsyncMongoDBStore<Doc>();
        store.SetSettings(settings);

        try
        {
            await store.CreateAsync(new[]
            {
                new Doc { Amount = 1, Name = "a" },
                new Doc { Amount = 5, Name = "b" },
                new Doc { Amount = 9, Name = "c" },
            });

            var wanted = new[] { 1, 5 };

            var hits = (await store.ReadAsync(x => wanted.Contains(x.Amount))).ToList();
            hits.Select(d => d.Name).Should().BeEquivalentTo(new[] { "a", "b" });

            var misses = (await store.ReadAsync(x => !wanted.Contains(x.Amount))).ToList();
            misses.Select(d => d.Name).Should().BeEquivalentTo(new[] { "c" },
                "the negated form must exclude exactly the same set — a rewrite that widened would return all three");

            (await store.CountAsync(x => wanted.Contains(x.Amount), CancellationToken.None)).Should().Be(2);

            // An empty array is still an empty set, not "everything" — the shape TASK-212's guard is about.
            var none = Array.Empty<int>();
            (await store.CountAsync(x => none.Contains(x.Amount), CancellationToken.None)).Should().Be(0);
        }
        finally
        {
            await new Birko.Data.MongoDB.MongoDBClient(settings).Database.Client.DropDatabaseAsync(settings.Name);
        }
    }
}
