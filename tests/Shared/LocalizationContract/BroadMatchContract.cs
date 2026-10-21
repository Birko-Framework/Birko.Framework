using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Tests.Shared.LocalizationContract;

/// <summary>The entity <see cref="BroadMatchContract"/> localizes.</summary>
public sealed class BroadMatchProduct : AbstractModel, ILocalizable
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
}

/// <summary>
/// TASK-701, per provider: TASK-686's broad match off SQL. A filter on a translated field whose translation sets hold more
/// than <see cref="LocalizedTranslationRows.ReadBatchSize"/> GUIDs runs as chunked queries; this counts, pages and deletes
/// such a match, and probes how many GUIDs one query of the provider accepts, so the budget is a measurement, not SQL
/// Server's number carried over.
/// <para>Observed state: the entities left in the store, and the timings written to the test output.</para>
/// </summary>
public abstract class BroadMatchContract
{
    private const int Entities = 3300;
    private readonly ITestOutputHelper _output;

    protected BroadMatchContract(ITestOutputHelper output) => _output = output;

    /// <summary>False when the backend is not reachable; a live provider reports its own skip.</summary>
    protected virtual bool Ready() => true;

    /// <summary>An entity store and a translation store, both empty, on one fresh database.</summary>
    protected abstract (IAsyncBulkStore<BroadMatchProduct> Products, IAsyncBulkStore<EntityTranslationModel> Translations) NewStores();

    /// <summary>Makes writes visible to queries (a near-real-time index); called while waiting for a count.</summary>
    protected virtual Task SettleAsync() => Task.CompletedTask;

    private async Task WaitForAsync(Func<Task<long>> count, long expected, string what)
    {
        var watch = Stopwatch.StartNew();
        long seen = -1;
        while (watch.Elapsed < TimeSpan.FromSeconds(90))
        {
            await SettleAsync();
            seen = await count();
            if (seen == expected) return;
            await Task.Delay(250);
        }
        throw new TimeoutException($"{what}: {seen} visible after {watch.Elapsed}, expected {expected}.");
    }

    [Fact]
    public async Task Localized_A_broad_match_on_a_translated_field_is_counted_paged_and_deleted_in_chunks()
    {
        if (!Ready()) return;
        var (products, translations) = NewStores();
        var context = new Context();
        // 1 100 translated to match (T), 1 100 untranslated matching on their base value (F) and 1 100 translated away (H):
        // 2 200 GUIDs in T and H, so both sets are past one chunk.
        var items = new List<BroadMatchProduct>();
        var rows = new List<(Guid EntityGuid, IReadOnlyDictionary<string, string?> Values)>();
        for (var i = 0; i < Entities; i++)
        {
            var guid = Guid.NewGuid();
            var kind = i % 3;
            items.Add(new BroadMatchProduct { Guid = guid, Name = kind == 0 ? $"b-{i}" : $"m-{i:D4}", Code = "run" });
            if (kind != 1)
            {
                rows.Add((guid, new Dictionary<string, string?> { ["Name"] = kind == 0 ? $"m-{i:D4}" : $"x-{i}" }));
            }
        }
        await products.CreateAsync(items);
        await LocalizedTranslationRows.SaveManyAsync(translations, nameof(BroadMatchProduct), "sk", rows, CancellationToken.None);
        await WaitForAsync(() => products.CountAsync(x => x.Code == "run"), Entities, "entities");
        await WaitForAsync(() => translations.CountAsync(t => t.Culture == "sk"), rows.Count, "translations");
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<BroadMatchProduct>, BroadMatchProduct>(products, translations, context);
        Expression<Func<BroadMatchProduct, bool>> matching = x => x.Code == "run" && x.Name.StartsWith("m-");

        var watch = Stopwatch.StartNew();
        var count = await bulk.CountAsync(matching);
        var counted = watch.Elapsed;
        watch.Restart();
        var page = (await bulk.ReadAsync(matching, OrderBy<BroadMatchProduct>.By(x => x.Name), 10, 100)).Select(x => x.Name).ToList();
        var paged = watch.Elapsed;

        count.Should().Be(2200);
        page.Should().Equal(Enumerable.Range(0, Entities).Where(i => i % 3 != 2).Select(i => $"m-{i:D4}")
            .OrderBy(n => n, StringComparer.Create(context.CurrentCulture, false)).Skip(100).Take(10));

        watch.Restart();
        await bulk.DeleteAsync(matching);
        var deleted = watch.Elapsed;
        await WaitForAsync(() => products.CountAsync(x => x.Code == "run"), 1100, "entities after the delete");
        _output.WriteLine($"budget {LocalizedTranslationRows.ReadBatchSize}: count {counted.TotalMilliseconds:F0} ms, "
                          + $"page {paged.TotalMilliseconds:F0} ms, delete {deleted.TotalMilliseconds:F0} ms");
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2200)]
    [InlineData(10000)]
    public async Task Localized_One_query_of_this_many_guids_is_accepted(int size)
    {
        if (!Ready()) return;
        var (products, _) = NewStores();
        var stored = Enumerable.Range(0, 50).Select(i => new BroadMatchProduct { Guid = Guid.NewGuid(), Name = $"p{i}", Code = "probe" }).ToList();
        await products.CreateAsync(stored);
        await WaitForAsync(() => products.CountAsync(x => x.Code == "probe"), stored.Count, "probe entities");
        // The shape the localized filter renders: a GUID list against the entity Guid.
        var guids = stored.Select(p => p.Guid).Concat(Enumerable.Range(0, size - stored.Count).Select(_ => (Guid?)Guid.NewGuid())).ToList();

        var watch = Stopwatch.StartNew();
        var found = await products.CountAsync(x => guids.Contains(x.Guid));
        var countTime = watch.Elapsed;
        watch.Restart();
        var read = (await products.ReadAsync(x => guids.Contains(x.Guid), null, null, null)).Count();
        var readTime = watch.Elapsed;

        _output.WriteLine($"{size} GUIDs in one query: count {countTime.TotalMilliseconds:F0} ms, read {readTime.TotalMilliseconds:F0} ms");
        found.Should().Be(stored.Count);
        read.Should().Be(stored.Count);
    }

    private sealed class Context : IEntityLocalizationContext
    {
        public CultureInfo CurrentCulture { get; } = new("sk");
        public CultureInfo DefaultCulture { get; } = new("en");
    }
}
