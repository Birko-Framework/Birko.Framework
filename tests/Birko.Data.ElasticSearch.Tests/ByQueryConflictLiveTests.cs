using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Exceptions;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-502, live. A filtered update or delete runs as <c>update_by_query</c> / <c>delete_by_query</c> with
/// <c>conflicts: abort</c>. Measured: three concurrent increments over 3,000 documents each answered 409 after applying
/// ~330, and the store returned normally, so most rows silently missed the update. The invariant below is exact: every
/// call that returns normally applied to every document, and every call that throws says how many it applied.
/// </summary>
public sealed class ByQueryConflictLiveTests : IDisposable
{
    private const int Documents = 3000;
    private const int Writers = 3;

    private readonly ElasticLive _live;
    public ByQueryConflictLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    private static IEnumerable<SortModel> Seed() => Enumerable.Range(0, Documents).Select(_ => new SortModel { Rank = 0, Name = "n" });

    private static long Applied(Exception? e) => e is StoreConcurrencyException c ? c.Applied : 0;

    private static void AssertNoSilentPartialWrite(IReadOnlyList<Exception?> outcomes, long total)
    {
        outcomes.Where(e => e != null).Should().AllBeOfType<StoreConcurrencyException>();
        var completed = outcomes.Count(e => e == null);
        total.Should().Be((long)completed * Documents + outcomes.Sum(Applied),
            "a call that returns normally applied to every document, and one that throws reports what it applied");
    }

    [Fact]
    public async Task Concurrent_async_increments_never_report_a_partial_write_as_success()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<SortModel>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(Seed());
        ElasticLive.Refresh();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, Writers).Select(async _ =>
        {
            try { await store.UpdateAsync(x => x.Rank >= 0, new PropertyUpdate<SortModel>().Increment(x => x.Rank, 1)); return (Exception?)null; }
            catch (Exception e) { return e; }
        }));
        ElasticLive.Refresh();

        var total = (await store.ReadAsync(x => x.Rank >= 0)).Sum(x => (long)x.Rank);
        AssertNoSilentPartialWrite(outcomes, total);
    }

    [Fact]
    public void Concurrent_sync_increments_never_report_a_partial_write_as_success()
    {
        if (!_live.Ready()) return;
        var store = new ElasticSearchStore<SortModel>();
        store.SetSettings(_live.NewSettings());
        store.Create(Seed());
        ElasticLive.Refresh();

        var outcomes = Enumerable.Range(0, Writers).AsParallel().WithDegreeOfParallelism(Writers).Select(_ =>
        {
            try { store.Update(x => x.Rank >= 0, new PropertyUpdate<SortModel>().Increment(x => x.Rank, 1)); return (Exception?)null; }
            catch (Exception e) { return e; }
        }).ToList();
        ElasticLive.Refresh();

        var total = store.Read(x => x.Rank >= 0).Sum(x => (long)x.Rank);
        AssertNoSilentPartialWrite(outcomes, total);
    }

    /// <summary>A delete racing an update: whichever loses must say so rather than leave rows it reported gone.</summary>
    [Fact]
    public async Task A_delete_racing_an_update_reports_what_it_could_not_delete()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<SortModel>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(Seed());
        ElasticLive.Refresh();

        var update = store.UpdateAsync(x => x.Rank >= 0, new PropertyUpdate<SortModel>().Increment(x => x.Rank, 1));
        Exception? deleteError = null;
        try { await store.DeleteAsync(x => x.Rank >= 0); }
        catch (Exception e) { deleteError = e; }
        try { await update; } catch (StoreConcurrencyException) { }
        ElasticLive.Refresh();

        var left = (await store.ReadAsync(x => x.Rank >= 0)).Count();
        if (deleteError == null)
        {
            left.Should().Be(0, "a delete that returns normally deleted every match");
        }
        else
        {
            deleteError.Should().BeOfType<StoreConcurrencyException>();
            left.Should().BeGreaterThan(0);
        }
    }
}
