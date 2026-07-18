using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// Live-backend verification that the MongoDB driver's expression translator handles null comparisons
/// (<c>x.Field == null</c> / <c>!= null</c>) the way the SQL and ElasticSearch parsers do — i.e. null docs
/// are matched by <c>== null</c> and only non-null docs by <c>!= null</c>. MongoDB has no hand-rolled parser;
/// the raw <see cref="System.Linq.Expressions.Expression"/> is forwarded to the driver, so this can only be
/// asserted against a running server.
///
/// Gated on the <c>BIRKO_MONGO_HOST</c> environment variable (e.g. <c>localhost</c>); skipped otherwise so CI
/// without a Mongo instance stays green. Each run uses a throwaway database that is dropped on teardown.
/// </summary>
public class MongoNullFilterLiveTests
{
    private const string HostEnv = "BIRKO_MONGO_HOST";

    public class NullModel : AbstractModel
    {
        public string? Name { get; set; }
        public int? Score { get; set; }
    }

    [Fact]
    public async Task NullComparisons_MatchDriverSemantics()
    {
        // Opt-in live test: set BIRKO_MONGO_HOST (e.g. localhost) to run it. Absent → no-op pass so CI
        // without a Mongo instance stays green.
        var host = Environment.GetEnvironmentVariable(HostEnv);
        if (string.IsNullOrWhiteSpace(host))
            return;

        var dbName = "birko_nulltest_" + Guid.NewGuid().ToString("N");
        var store = new AsyncMongoDBStore<NullModel>();
        store.SetSettings(new Settings(host, dbName));

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
            try { store.Client?.Database.Client.DropDatabase(dbName); } catch { /* best-effort */ }
        }
    }
}
