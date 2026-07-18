using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// Live-backend verification that the Cosmos DB LINQ provider handles null comparisons
/// (<c>x.Field == null</c> / <c>!= null</c>) with the same intent as the SQL and ElasticSearch parsers —
/// null docs matched by <c>== null</c>, only non-null docs by <c>!= null</c>. Cosmos has no hand-rolled
/// parser; the raw <see cref="System.Linq.Expressions.Expression"/> is handed to
/// <c>GetItemLinqQueryable().Where(filter)</c>, so this is only assertable against a live account/emulator.
///
/// Gated on <c>BIRKO_COSMOS_CONNECTION</c> (a full Cosmos connection string, e.g. the emulator's well-known
/// one); skipped otherwise. Each run uses a throwaway database dropped on teardown.
/// </summary>
public class CosmosNullFilterLiveTests
{
    private const string ConnEnv = "BIRKO_COSMOS_CONNECTION";

    public class NullModel : AbstractModel
    {
        public string? Name { get; set; }
        public int? Score { get; set; }
    }

    [Fact]
    public async Task NullComparisons_MatchLinqProviderSemantics()
    {
        // Opt-in live test: set BIRKO_COSMOS_CONNECTION (a Cosmos connection string) to run it. Absent →
        // no-op pass so CI without a Cosmos account/emulator stays green.
        var conn = Environment.GetEnvironmentVariable(ConnEnv);
        if (string.IsNullOrWhiteSpace(conn))
            return;

        var dbName = "birko_nulltest_" + Guid.NewGuid().ToString("N");
        var store = new AsyncCosmosDBStore<NullModel>(conn, dbName);

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

            var isNull = (await store.ReadAsync(x => x.Score == null)).Select(x => x.Guid).ToList();
            var notNull = (await store.ReadAsync(x => x.Score != null)).Select(x => x.Guid).ToList();
            var hasValue = (await store.ReadAsync(x => x.Score.HasValue)).Select(x => x.Guid).ToList();

            isNull.Should().BeEquivalentTo(nullScore.Select(x => x.Guid));
            notNull.Should().BeEquivalentTo(withScore.Select(x => x.Guid));
            hasValue.Should().BeEquivalentTo(withScore.Select(x => x.Guid));
        }
        finally
        {
            try { await store.DestroyAsync(); } catch { /* best-effort */ }
            try { if (store.Client != null) await store.Client.GetDatabase(dbName).DeleteAsync(); } catch { /* best-effort */ }
        }
    }
}
