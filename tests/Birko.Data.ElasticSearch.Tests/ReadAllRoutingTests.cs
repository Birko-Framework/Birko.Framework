using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// Regression for CR-H046: the no-arg ReadAsync(ct) was overridden directly, skipping the base
/// lazy-init gate and hard-capping results at 1000 docs with no scrolling. The override is removed,
/// so ReadAsync() now runs the init gate and delegates to the scrolling bulk ReadCoreAsync with no
/// limit/offset. A tracking subclass proves the routing.
/// </summary>
public class ReadAllRoutingTests
{
    private sealed class TrackingStore : AsyncElasticSearchStore<DateModel>
    {
        public int InitCount;
        public int BulkReadCount;
        public int? LastLimit = -1;
        public int? LastOffset = -1;

        protected override Task InitCoreAsync(CancellationToken ct = default)
        {
            InitCount++;
            return Task.CompletedTask;
        }

        protected override Task<IEnumerable<DateModel>> ReadCoreAsync(
            Expression<Func<DateModel, bool>>? filter = null,
            OrderBy<DateModel>? orderBy = null,
            int? limit = null,
            int? offset = null,
            CancellationToken ct = default)
        {
            BulkReadCount++;
            LastLimit = limit;
            LastOffset = offset;
            return Task.FromResult<IEnumerable<DateModel>>(new[] { new DateModel(), new DateModel() });
        }
    }

    [Fact]
    public async Task ReadAsync_NoArg_RunsLazyInitAndScrollingCore_NoCap()
    {
        var store = new TrackingStore();

        var result = await store.ReadAsync(CancellationToken.None);

        store.InitCount.Should().Be(1, "the base wrapper runs EnsureInitializedAsync");
        store.BulkReadCount.Should().Be(1, "it delegates to the scrolling bulk ReadCoreAsync");
        store.LastLimit.Should().BeNull("CR-H046: no 1000-doc cap — unlimited read-all");
        store.LastOffset.Should().BeNull();
        result.Should().HaveCount(2);
    }
}
