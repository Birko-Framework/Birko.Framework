using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Localization.Translations;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-686 — a filter on a translated field whose GUID sets outgrow one batch reaches the entity store as disjoint chunked
/// queries over one positive set, never as one unbounded IN list. Every test checks the results exactly and that no
/// inner filter carries more than <see cref="LocalizedTranslationRows.ReadBatchSize"/> GUIDs.
/// </summary>
public class LocalizationChunkedFilterTests
{
    private const int Batch = LocalizedTranslationRows.ReadBatchSize;

    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _entities = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _translations = new();
    private readonly InMemoryBulkStore<TestLocalizableModel> _syncEntities = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _syncTranslations = new();
    private readonly TestEntityLocalizationContext _context = new() { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };

    private AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> Bulk() => new(_entities, _translations, _context);

    private LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> SyncBulk() => new(_syncEntities, _syncTranslations, _context);

    private static EntityTranslationModel Row(Guid guid, string value) => new()
    {
        EntityGuid = guid, EntityType = nameof(TestLocalizableModel), FieldName = nameof(TestLocalizableModel.Name), Culture = "sk", Value = value,
    };

    /// <summary>
    /// <paramref name="translated"/> entities translated to match ("match-…", T), <paramref name="untranslated"/> whose
    /// base value matches (F), and <paramref name="hidden"/> translated so they don't, though their base value does (H).
    /// Returns the GUIDs that should match: T and F.
    /// </summary>
    private async Task<HashSet<Guid>> SeedAsync(int translated, int untranslated, int hidden)
    {
        var expected = new HashSet<Guid>();
        var entities = new List<TestLocalizableModel>();
        var rows = new List<EntityTranslationModel>();
        for (var i = 0; i < translated; i++)
        {
            var guid = Guid.NewGuid();
            entities.Add(new TestLocalizableModel { Guid = guid, Name = $"base-{i}", Code = $"T{i:D5}" });
            rows.Add(Row(guid, $"match-t{i}"));
            expected.Add(guid);
        }
        for (var i = 0; i < untranslated; i++)
        {
            var guid = Guid.NewGuid();
            entities.Add(new TestLocalizableModel { Guid = guid, Name = $"match-f{i}", Code = $"F{i:D5}" });
            expected.Add(guid);
        }
        for (var i = 0; i < hidden; i++)
        {
            var guid = Guid.NewGuid();
            entities.Add(new TestLocalizableModel { Guid = guid, Name = $"match-h{i}", Code = $"H{i:D5}" });
            rows.Add(Row(guid, $"other-{i}"));
        }
        await _entities.CreateAsync(entities);
        await _translations.CreateAsync(rows);
        _syncEntities.Create(entities.Select(e => (TestLocalizableModel)e.CopyTo()).ToList());
        _syncTranslations.Create(rows.Select(r => new EntityTranslationModel
        {
            EntityGuid = r.EntityGuid, EntityType = r.EntityType, FieldName = r.FieldName, Culture = r.Culture, Value = r.Value,
        }).ToList());
        return expected;
    }

    private static readonly Expression<Func<TestLocalizableModel, bool>> Matching = x => x.Name.StartsWith("match");

    /// <summary>The largest GUID list constant any filter the store received carries.</summary>
    private static int LargestInList(IEnumerable<LambdaExpression?> filters)
        => filters.Where(f => f != null).Select(f => new GuidListFinder().Largest(f!)).DefaultIfEmpty(0).Max();

    [Fact]
    public async Task A_broad_match_is_read_in_bounded_chunks_with_every_entity_once()
    {
        var expected = await SeedAsync(translated: 1200, untranslated: 300, hidden: 300);

        var found = (await Bulk().ReadAsync(Matching, null, null, null)).Select(x => x.Guid!.Value).ToList();

        found.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(expected);
        LargestInList(_entities.SeenFilters).Should().BeLessThanOrEqualTo(Batch);
    }

    [Fact]
    public async Task A_broad_match_is_counted_exactly_across_chunks()
    {
        await SeedAsync(translated: 1200, untranslated: 300, hidden: 300);

        (await Bulk().CountAsync(Matching)).Should().Be(1500);
        SyncBulk().Count(Matching).Should().Be(1500);
        LargestInList(_entities.SeenFilters.Concat(_syncEntities.SeenFilters)).Should().BeLessThanOrEqualTo(Batch);
    }

    [Fact]
    public async Task A_large_exclusion_set_still_answers_the_fallback_exactly()
    {
        var expected = await SeedAsync(translated: 10, untranslated: 50, hidden: 700);

        var found = (await Bulk().ReadAsync(Matching, null, null, null)).Select(x => x.Guid!.Value).ToList();

        found.Should().BeEquivalentTo(expected, "translated non-matching entities stay out, untranslated base matches stay in");
        LargestInList(_entities.SeenFilters).Should().BeLessThanOrEqualTo(Batch);
    }

    [Fact]
    public async Task Two_translated_conditions_intersect()
    {
        var expected = await SeedAsync(translated: 1200, untranslated: 0, hidden: 0);
        var described = expected.Take(700).ToList();
        await _translations.CreateAsync(described.Select(g => new EntityTranslationModel
        {
            EntityGuid = g, EntityType = nameof(TestLocalizableModel), FieldName = nameof(TestLocalizableModel.Description), Culture = "sk", Value = "popis",
        }).ToList());

        var found = (await Bulk().ReadAsync(x => x.Name.StartsWith("match") && x.Description == "popis", null, null, null))
            .Select(x => x.Guid!.Value).ToList();

        found.Should().BeEquivalentTo(described);
        LargestInList(_entities.SeenFilters).Should().BeLessThanOrEqualTo(Batch);
    }

    [Fact]
    public async Task When_nothing_can_match_no_query_reaches_the_entity_store_and_a_delete_removes_nothing()
    {
        await SeedAsync(translated: 0, untranslated: 0, hidden: 700);
        var bulk = Bulk();
        var reads = _entities.FilteredReadCalls;

        (await bulk.ReadAsync(x => x.Code.StartsWith("H") && x.Name.StartsWith("match-h"), null, null, null)).Should().BeEmpty();
        await bulk.DeleteAsync(x => x.Code.StartsWith("H") && x.Name.StartsWith("match-h"));

        (_entities.FilteredReadCalls - reads).Should().Be(2, "one candidate read per call, and no query for the empty set (rules 2, 3)");
        (await _entities.ReadAsync(default(CancellationToken))).Should().HaveCount(700);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_chunked_page_is_sorted_and_paged_over_the_whole_match(bool byTranslatedName)
    {
        var expected = await SeedAsync(translated: 600, untranslated: 100, hidden: 100);
        var all = (await Bulk().ReadAsync(Matching, null, null, null)).ToList();
        var orderBy = byTranslatedName ? OrderBy<TestLocalizableModel>.By(x => x.Name) : OrderBy<TestLocalizableModel>.By(x => x.Code);
        var key = byTranslatedName ? (Func<TestLocalizableModel, string>)(x => x.Name) : x => x.Code;

        var page = (await Bulk().ReadAsync(Matching, orderBy, 20, 40)).ToList();

        page.Select(key).Should().Equal(all.OrderBy(key, StringComparer.Create(_context.CurrentCulture, false)).Skip(40).Take(20).Select(key));
        expected.Should().HaveCount(700);
    }

    [Fact]
    public async Task A_chunked_page_without_an_order_returns_the_requested_size()
    {
        await SeedAsync(translated: 600, untranslated: 100, hidden: 100);

        (await Bulk().ReadAsync(Matching, null, 25, 690)).Should().HaveCount(10);
    }

    [Fact]
    public async Task A_single_read_finds_a_match_in_any_chunk()
    {
        await SeedAsync(translated: 1200, untranslated: 0, hidden: 0);

        (await ((IAsyncReadStore<TestLocalizableModel>)Bulk()).ReadAsync(Matching)).Should().NotBeNull();
    }

    [Fact]
    public async Task Find_counts_its_limit_across_chunks_and_sees_a_second_match_in_the_last_one()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000001");
        var last = new Guid("ffffffff-ffff-ffff-ffff-fffffffffffe");
        var rows = new List<EntityTranslationModel>();
        var entities = new List<TestLocalizableModel>();
        for (var i = 0; i < 1200; i++)
        {
            var guid = i == 0 ? first : i == 1 ? last : Guid.NewGuid();
            entities.Add(new TestLocalizableModel { Guid = guid, Name = "base", Code = i < 2 ? "pick" : "skip" });
            rows.Add(Row(guid, "dup"));
        }
        await _entities.CreateAsync(entities);
        await _translations.CreateAsync(rows);
        var api = new EntityTranslations<TestLocalizableModel>(new ScopedToCode(_entities, "pick"), _translations, _context);

        var act = () => api.FindEntityAsync("Name", new CultureInfo("sk"), "dup");

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(first.ToString()).And.Contain(last.ToString());
    }

    [Fact]
    public async Task A_native_update_and_a_delete_cover_every_chunk_and_nothing_else()
    {
        var expected = await SeedAsync(translated: 1200, untranslated: 300, hidden: 300);
        var bulk = Bulk();

        await bulk.UpdateAsync(Matching, new PropertyUpdate<TestLocalizableModel>().Set(x => x.Code, "updated"));

        var all = (await _entities.ReadAsync(default(CancellationToken))).ToList();
        all.Where(x => x.Code == "updated").Select(x => x.Guid!.Value).Should().BeEquivalentTo(expected);

        await bulk.DeleteAsync(Matching);

        (await _entities.ReadAsync(default(CancellationToken))).Select(x => x.Code).Should().OnlyContain(c => c.StartsWith("H")).And.HaveCount(300);
        LargestInList(_entities.SeenFilters).Should().BeLessThanOrEqualTo(Batch);
    }

    [Fact]
    public async Task A_chunked_native_update_runs_inside_the_write_boundary()
    {
        await SeedAsync(translated: 1200, untranslated: 0, hidden: 0);
        var boundary = new CountingBoundary();
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(_entities, _translations, _context, boundary);

        await bulk.UpdateAsync(Matching, new PropertyUpdate<TestLocalizableModel>().Set(x => x.Code, "updated"));

        boundary.Runs.Should().Be(1, "every chunk of one native update is one unit");
    }

    private sealed class CountingBoundary : Birko.Data.Localization.Boundaries.ILocalizedWriteBoundary
    {
        public int Runs { get; private set; }
        public bool IsAtomic => true;
        public void Run(Action write) { Runs++; write(); }
        public Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct) { Runs++; return write(ct); }
    }

    [Fact]
    public async Task The_sync_wrapper_reads_and_deletes_in_chunks_too()
    {
        var expected = await SeedAsync(translated: 1200, untranslated: 300, hidden: 300);
        var bulk = SyncBulk();

        bulk.Read(Matching, null, null, null).Select(x => x.Guid!.Value).Should().BeEquivalentTo(expected);
        bulk.Delete(Matching);

        _syncEntities.Read().Should().HaveCount(300);
        LargestInList(_syncEntities.SeenFilters).Should().BeLessThanOrEqualTo(Batch);
    }

    [Fact]
    public async Task A_filter_update_with_an_action_covers_every_chunk()
    {
        var expected = await SeedAsync(translated: 1200, untranslated: 300, hidden: 300);

        await Bulk().UpdateAsync(Matching, x => x.Code = "acted");

        (await _entities.ReadAsync(default(CancellationToken))).Where(x => x.Code == "acted").Select(x => x.Guid!.Value)
            .Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task A_collection_update_reads_its_base_values_in_bounded_chunks()
    {
        await SeedAsync(translated: 0, untranslated: 1200, hidden: 0);
        var items = (await _entities.ReadAsync(default(CancellationToken))).Select(x => new TestLocalizableModel { Guid = x.Guid, Name = "sk", Code = x.Code }).ToList();
        _entities.SeenFilters.Clear();

        await Bulk().UpdateAsync(items);

        LargestInList(_entities.SeenFilters).Should().BeLessThanOrEqualTo(Batch);
        (await _entities.ReadAsync(default(CancellationToken))).Should().OnlyContain(x => x.Name.StartsWith("match"), "the base column keeps its value");
    }

    [Fact]
    public async Task Below_the_budget_the_filter_stays_one_query_with_its_exclusion()
    {
        await SeedAsync(translated: 10, untranslated: 10, hidden: 10);
        var reads = _entities.FilteredReadCalls;

        (await Bulk().ReadAsync(Matching, null, null, null)).Should().HaveCount(20);

        (_entities.FilteredReadCalls - reads).Should().Be(2, "one candidate read and one query");
        _entities.SeenFilters.Last()!.ToString().Should().Contain("Not(", "the small NOT IN is kept as it was");
    }

    [Fact]
    public async Task An_entity_store_that_cannot_list_keeps_its_one_query_below_the_provider_limit()
    {
        await SeedAsync(translated: 0, untranslated: 0, hidden: 0);
        _syncTranslations.Create(Enumerable.Range(0, 700).Select(i => Row(Guid.NewGuid(), $"match-{i}")).ToList());
        var plain = new LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel>(new PlainStore(_syncEntities), _syncTranslations, _context);

        plain.Count(Matching).Should().Be(0, "700 translations of orphan GUIDs match no stored entity, and nothing throws");
    }

    [Fact]
    public async Task An_entity_store_that_cannot_list_is_refused_past_the_provider_limit()
    {
        await SeedAsync(translated: 0, untranslated: 0, hidden: 0);
        _syncTranslations.Create(Enumerable.Range(0, LocalizedFilterResolution.NonListingQueryLimit + 1).Select(i => Row(Guid.NewGuid(), $"match-{i}")).ToList());
        var plain = new LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel>(new PlainStore(_syncEntities), _syncTranslations, _context);

        var act = () => plain.Count(Matching);

        act.Should().Throw<NotSupportedException>().WithMessage("*more than 2000*bulk store*");
    }

    /// <summary>Finds the largest <c>List&lt;Guid&gt;</c> constant in an expression.</summary>
    private sealed class GuidListFinder : ExpressionVisitor
    {
        private int _largest;

        public int Largest(Expression expression)
        {
            Visit(expression);
            return _largest;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IReadOnlyCollection<Guid> guids)
            {
                _largest = Math.Max(_largest, guids.Count);
            }
            return base.VisitConstant(node);
        }
    }

    /// <summary>The entity store under a tenant-like decorator: every query is narrowed to one code.</summary>
    private sealed class ScopedToCode : IAsyncBulkStore<TestLocalizableModel>
    {
        private readonly IAsyncBulkStore<TestLocalizableModel> _inner;
        private readonly string _code;

        public ScopedToCode(IAsyncBulkStore<TestLocalizableModel> inner, string code)
        {
            _inner = inner;
            _code = code;
        }

        private Expression<Func<TestLocalizableModel, bool>> Scope(Expression<Func<TestLocalizableModel, bool>>? filter)
        {
            Expression<Func<TestLocalizableModel, bool>> scope = x => x.Code == _code;
            if (filter == null) return scope;
            var body = Expression.AndAlso(filter.Body, Expression.Invoke(scope, filter.Parameters[0]));
            return Expression.Lambda<Func<TestLocalizableModel, bool>>(body, filter.Parameters[0]);
        }

        public Task<IEnumerable<TestLocalizableModel>> ReadAsync(Expression<Func<TestLocalizableModel, bool>>? filter = null, OrderBy<TestLocalizableModel>? orderBy = null, int? limit = null, int? offset = null, CancellationToken ct = default)
            => _inner.ReadAsync(Scope(filter), orderBy, limit, offset, ct);
        public Task<TestLocalizableModel?> ReadAsync(Guid guid, CancellationToken ct = default) => _inner.ReadAsync(guid, ct);
        public Task<TestLocalizableModel?> ReadAsync(Expression<Func<TestLocalizableModel, bool>>? filter = null, CancellationToken ct = default) => ((IAsyncReadStore<TestLocalizableModel>)_inner).ReadAsync(Scope(filter), ct);
        public Task<IEnumerable<TestLocalizableModel>> ReadAsync(CancellationToken ct = default) => _inner.ReadAsync(Scope(null), null, null, null, ct);
        public Task<long> CountAsync(Expression<Func<TestLocalizableModel, bool>>? filter = null, CancellationToken ct = default) => _inner.CountAsync(Scope(filter), ct);
        public Task<Guid> CreateAsync(TestLocalizableModel data, StoreDataDelegate<TestLocalizableModel>? processDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, processDelegate, ct);
        public Task CreateAsync(IEnumerable<TestLocalizableModel> data, StoreDataDelegate<TestLocalizableModel>? storeDelegate = null, CancellationToken ct = default) => _inner.CreateAsync(data, storeDelegate, ct);
        public Task UpdateAsync(TestLocalizableModel data, StoreDataDelegate<TestLocalizableModel>? processDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, processDelegate, ct);
        public Task UpdateAsync(IEnumerable<TestLocalizableModel> data, StoreDataDelegate<TestLocalizableModel>? storeDelegate = null, CancellationToken ct = default) => _inner.UpdateAsync(data, storeDelegate, ct);
        public Task DeleteAsync(TestLocalizableModel data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task UpdateAsync(Expression<Func<TestLocalizableModel, bool>> filter, Action<TestLocalizableModel> updateAction, CancellationToken ct = default) => _inner.UpdateAsync(Scope(filter), updateAction, ct);
        public Task UpdateAsync(Expression<Func<TestLocalizableModel, bool>> filter, PropertyUpdate<TestLocalizableModel> updates, CancellationToken ct = default) => _inner.UpdateAsync(Scope(filter), updates, ct);
        public Task DeleteAsync(IEnumerable<TestLocalizableModel> data, CancellationToken ct = default) => _inner.DeleteAsync(data, ct);
        public Task DeleteAsync(Expression<Func<TestLocalizableModel, bool>> filter, CancellationToken ct = default) => _inner.DeleteAsync(Scope(filter), ct);
        public Task<Guid> SaveAsync(TestLocalizableModel data, StoreDataDelegate<TestLocalizableModel>? processDelegate = null, CancellationToken ct = default) => _inner.SaveAsync(data, processDelegate, ct);
        public Task InitAsync(CancellationToken ct = default) => _inner.InitAsync(ct);
        public Task DestroyAsync(CancellationToken ct = default) => _inner.DestroyAsync(ct);
        public TestLocalizableModel CreateInstance() => _inner.CreateInstance();
    }

    /// <summary>An entity store with no list read: only <see cref="IStore{T}"/>.</summary>
    private sealed class PlainStore : IStore<TestLocalizableModel>
    {
        private readonly InMemoryBulkStore<TestLocalizableModel> _inner;

        public PlainStore(InMemoryBulkStore<TestLocalizableModel> inner) => _inner = inner;

        public TestLocalizableModel? Read(Guid guid) => _inner.Read(guid);
        public TestLocalizableModel? Read(Expression<Func<TestLocalizableModel, bool>>? filter = null) => ((IReadStore<TestLocalizableModel>)_inner).Read(filter);
        public long Count(Expression<Func<TestLocalizableModel, bool>>? filter = null) => _inner.Count(filter);
        public Guid Create(TestLocalizableModel data, StoreDataDelegate<TestLocalizableModel>? storeDelegate = null) => _inner.Create(data, storeDelegate);
        public void Update(TestLocalizableModel data, StoreDataDelegate<TestLocalizableModel>? storeDelegate = null) => _inner.Update(data, storeDelegate);
        public void Delete(TestLocalizableModel data) => _inner.Delete(data);
        public Guid Save(TestLocalizableModel data, StoreDataDelegate<TestLocalizableModel>? storeDelegate = null) => _inner.Save(data, storeDelegate);
        public void Init() => _inner.Init();
        public void Destroy() => _inner.Destroy();
        public TestLocalizableModel CreateInstance() => _inner.CreateInstance();
    }
}
