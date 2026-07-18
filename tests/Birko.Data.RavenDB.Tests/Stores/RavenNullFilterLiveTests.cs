using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.RavenDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>
/// Live-backend verification that the RavenDB LINQ provider handles null comparisons
/// (<c>x.Field == null</c> / <c>!= null</c>) with the same intent as the SQL and ElasticSearch parsers —
/// null docs matched by <c>== null</c>, only non-null docs by <c>!= null</c>. RavenDB has no hand-rolled
/// parser; the raw <see cref="Expression"/> is handed to <c>session.Query&lt;T&gt;().Where(filter)</c>.
///
/// Gated on <c>BIRKO_RAVEN_URL</c> (e.g. <c>http://localhost:8080</c>); skipped otherwise. Each run uses a
/// throwaway database dropped on teardown. RavenDB queries are eventually consistent (indexing lag), so the
/// assertions poll until the index has caught up before failing.
/// </summary>
public class RavenNullFilterLiveTests
{
    private const string UrlEnv = "BIRKO_RAVEN_URL";

    public class NullModel : AbstractModel
    {
        public string? Name { get; set; }
        public int? Score { get; set; }
    }

    [Fact]
    public async Task NullComparisons_MatchLinqProviderSemantics()
    {
        // Opt-in live test: set BIRKO_RAVEN_URL (e.g. http://localhost:8080) to run it. Absent → no-op pass
        // so CI without a RavenDB server stays green.
        var url = Environment.GetEnvironmentVariable(UrlEnv);
        if (string.IsNullOrWhiteSpace(url))
            return;

        var dbName = "birko_nulltest_" + Guid.NewGuid().ToString("N");
        var store = new AsyncRavenDBStore<NullModel>(url, dbName);

        try
        {
            var withScore = new[]
            {
                new NullModel { Guid = Guid.NewGuid(), Name = "a", Score = 10 },
                new NullModel { Guid = Guid.NewGuid(), Name = "b", Score = 20 },
            };
            var nullScore = new[]
            {
                new NullModel { Guid = Guid.NewGuid(), Name = "c", Score = null },
                new NullModel { Guid = Guid.NewGuid(), Name = "d", Score = null },
            };
            await store.CreateAsync(withScore.Concat(nullScore).ToList());

            // Poll past indexing lag: wait until both partitions settle to the expected sizes.
            var (isNull, notNull, hasValue) = await PollAsync(store, expectedNull: 2, expectedNotNull: 2);

            isNull.Should().BeEquivalentTo(nullScore.Select(x => x.Guid));
            notNull.Should().BeEquivalentTo(withScore.Select(x => x.Guid));
            hasValue.Should().BeEquivalentTo(withScore.Select(x => x.Guid));
        }
        finally
        {
            try { await store.DestroyAsync(); } catch { /* best-effort */ }
        }
    }

    private static async Task<(List<Guid?> isNull, List<Guid?> notNull, List<Guid?> hasValue)> PollAsync(
        AsyncRavenDBStore<NullModel> store, int expectedNull, int expectedNotNull)
    {
        List<Guid?> isNull = new(), notNull = new(), hasValue = new();
        for (int attempt = 0; attempt < 30; attempt++)
        {
            isNull = (await store.ReadAsync(x => x.Score == null)).Select(x => x.Guid).ToList();
            notNull = (await store.ReadAsync(x => x.Score != null)).Select(x => x.Guid).ToList();
            hasValue = (await store.ReadAsync(x => x.Score.HasValue)).Select(x => x.Guid).ToList();
            if (isNull.Count == expectedNull && notNull.Count == expectedNotNull && hasValue.Count == expectedNotNull)
                break;
            await Task.Delay(500);
        }
        return (isNull, notNull, hasValue);
    }
}
