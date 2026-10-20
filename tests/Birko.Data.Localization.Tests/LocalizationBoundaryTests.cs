using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Localization.Boundaries;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Localization.Translations;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-683, store-independent: which writes the wrappers run through their boundary, how a non-atomic delete reports a
/// failed cleanup, and the inner store's "not stored" report. The rollback itself is measured on SQL, in
/// Birko.Data.Localization.SQL.Tests.
/// </summary>
public class LocalizationBoundaryTests
{
    private readonly InMemoryBulkStore<TestLocalizableModel> _entities = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translations = new();
    private readonly InMemoryAsyncBulkStore<TestLocalizableModel> _asyncEntities = new();
    private readonly InMemoryAsyncBulkStore<EntityTranslationModel> _asyncTranslations = new();
    private readonly TestEntityLocalizationContext _context = new() { CurrentCulture = new CultureInfo("sk"), DefaultCulture = new CultureInfo("en") };
    private readonly RecordingBoundary _boundary = new();

    private LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> Bulk(ILocalizedWriteBoundary? boundary = null)
        => new(_entities, _translations, _context, boundary ?? _boundary);

    private AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel> AsyncBulk(ILocalizedWriteBoundary? boundary = null)
        => new(_asyncEntities, _asyncTranslations, _context, boundary ?? _boundary);

    private static TestLocalizableModel Item(string name, string code = "A") => new() { Guid = Guid.NewGuid(), Name = name, Code = code };

    // ------------------------------------------------------------------ which writes enter the boundary

    [Fact]
    public void Every_sync_write_touching_both_stores_enters_the_boundary_once()
    {
        var bulk = Bulk();
        var a = Item("a");
        var b = Item("b");

        bulk.Create(a);
        bulk.Create(new[] { b });
        bulk.Update(a);
        bulk.Update(new[] { a, b });
        bulk.Update(x => x.Code == "A", x => x.Name += "!");
        bulk.Update(x => x.Code == "A", new PropertyUpdate<TestLocalizableModel>().Set(x => x.Name, "set"));
        bulk.Delete(a);
        bulk.Delete(new[] { b });
        bulk.Delete(x => x.Code == "none");

        _boundary.Runs.Should().Equal("sync", "sync", "sync", "sync", "sync", "sync", "sync", "sync", "sync");
    }

    [Fact]
    public async Task Every_async_write_touching_both_stores_enters_the_boundary_once()
    {
        var bulk = AsyncBulk();
        var a = Item("a");
        var b = Item("b");

        await bulk.CreateAsync(a);
        await bulk.CreateAsync(new[] { b });
        await bulk.UpdateAsync(a);
        await bulk.UpdateAsync(new[] { a, b });
        await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");
        await bulk.DeleteAsync(a);
        await bulk.DeleteAsync(new[] { b });
        await bulk.DeleteAsync(x => x.Code == "none");

        _boundary.Runs.Should().HaveCount(8).And.OnlyContain(r => r == "async");
    }

    [Fact]
    public async Task On_the_default_culture_creates_and_updates_write_one_store_and_stay_outside()
    {
        _context.CurrentCulture = new CultureInfo("en");
        var bulk = AsyncBulk();
        var a = Item("a");

        await bulk.CreateAsync(a);
        await bulk.UpdateAsync(a);
        await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");
        _boundary.Runs.Should().BeEmpty();

        await bulk.DeleteAsync(a);
        _boundary.Runs.Should().ContainSingle("a delete removes translations in every culture");
    }

    [Fact]
    public async Task The_single_entity_wrappers_use_the_boundary_too()
    {
        var sync = new LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel>(_entities, _translations, _context, _boundary);
        var async = new AsyncLocalizedStoreWrapper<IAsyncStore<TestLocalizableModel>, TestLocalizableModel>(_asyncEntities, _asyncTranslations, _context, _boundary);
        var a = Item("a");
        var b = Item("b");

        sync.Create(a);
        sync.Update(a);
        sync.Delete(a);
        await async.CreateAsync(b);
        await async.UpdateAsync(b);
        await async.DeleteAsync(b);

        _boundary.Runs.Should().Equal("sync", "sync", "sync", "async", "async", "async");
    }

    [Fact]
    public async Task The_explicit_api_runs_each_write_through_its_boundary()
    {
        var api = new EntityTranslations<TestLocalizableModel>(_asyncEntities, _asyncTranslations, _context, _boundary);
        var id = Guid.NewGuid();

        await api.SetAsync(id, new CultureInfo("sk"), new Dictionary<string, string?> { ["Name"] = "x" });
        await api.RemoveCultureAsync(id, new CultureInfo("sk"));
        await api.RemoveEntitiesAsync(new[] { id });
        await api.RemoveEntitiesAsync(Array.Empty<Guid>());

        _boundary.Runs.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_retried_collection_write_sees_the_same_items_again()
    {
        var retrying = new RecordingBoundary { RunTwice = true };
        var bulk = AsyncBulk(retrying);
        var once = Enumerable.Range(0, 2).Select(i => Item($"n{i}"));

        await bulk.CreateAsync(once);

        (await _asyncEntities.ReadAsync(default(CancellationToken))).Should().HaveCount(2, "the enumerable was materialized before the boundary");
    }

    // ------------------------------------------------------------------ atomic boundary: no best-effort save

    [Fact]
    public async Task Inside_an_atomic_boundary_a_failed_filter_update_saves_no_translations()
    {
        var bulk = AsyncBulk(new RecordingBoundary { Atomic = true });
        await bulk.CreateAsync(new[] { Item("sk-a"), Item("sk-b") });
        var updates = 0;
        _asyncEntities.BeforeUpdate = _ =>
        {
            if (++updates == 2) throw new InvalidOperationException("inner write failed");
        };

        var act = async () => await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _asyncTranslations.ReadAsync(default(CancellationToken))).Should().NotContain(t => t.Value.EndsWith('!'),
            "the transaction rolls the first entity back, so saving its translation would only be undone");
    }

    // ------------------------------------------------------------------ best-effort delete: the cleanup report

    [Fact]
    public void A_best_effort_delete_whose_cleanup_fails_reports_the_entities_left_with_rows()
    {
        var bulk = Bulk(BestEffortWriteBoundary.Instance);
        var a = Item("sk-a");
        var b = Item("sk-b");
        bulk.Create(new[] { a, b });
        var failing = new InvalidOperationException("translation store down");
        _translations.BeforeDelete = () => throw failing;

        var act = () => bulk.Delete(x => x.Code == "A");

        var report = act.Should().Throw<TranslationCleanupException>().Which;
        report.RemainingEntityGuids.Should().BeEquivalentTo(new[] { a.Guid!.Value, b.Guid!.Value });
        report.InnerException.Should().BeSameAs(failing);
        report.Message.Should().Contain("RemoveEntitiesAsync");
        _entities.Read().Should().BeEmpty("best effort: the entity delete stands");
    }

    [Fact]
    public async Task On_an_atomic_boundary_a_failed_cleanup_propagates_as_it_is()
    {
        var bulk = AsyncBulk(new RecordingBoundary { Atomic = true });
        var a = Item("sk-a");
        await bulk.CreateAsync(a);
        var failing = new InvalidOperationException("translation store down");
        _asyncTranslations.BeforeDelete = () => throw failing;

        var act = async () => await bulk.DeleteAsync(a);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failing);
    }

    [Fact]
    public void A_delete_of_entities_without_a_guid_reports_nothing()
    {
        var bulk = Bulk(BestEffortWriteBoundary.Instance);
        _translations.BeforeDelete = () => throw new InvalidOperationException("not reached");

        bulk.Delete(new TestLocalizableModel { Name = "unsaved" });
    }

    // ------------------------------------------------------------------ TASK-555: items the inner store did not store

    [Fact]
    public async Task A_collection_update_saves_the_translations_of_the_items_the_inner_store_stored()
    {
        var bulk = AsyncBulk();
        var stored = Item("sk-a");
        var gone = Item("sk-b");
        await bulk.CreateAsync(new[] { stored, gone });
        _asyncEntities.MissingOnUpdate = x => x.Guid == gone.Guid;

        var act = async () => await bulk.UpdateAsync(new[]
        {
            new TestLocalizableModel { Guid = stored.Guid, Name = "sk-a!" }, new TestLocalizableModel { Guid = gone.Guid, Name = "sk-b!" },
        });

        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Guids.Should().Equal(gone.Guid!.Value);
        (await _asyncTranslations.ReadAsync(default(CancellationToken))).Where(t => t.FieldName == "Name").Select(t => t.Value)
            .Should().BeEquivalentTo("sk-a!", "sk-b");
    }

    [Fact]
    public void The_sync_collection_update_does_the_same()
    {
        var bulk = Bulk();
        var stored = Item("sk-a");
        var gone = Item("sk-b");
        bulk.Create(new[] { stored, gone });
        _entities.MissingOnUpdate = x => x.Guid == gone.Guid;

        var act = () => bulk.Update(new[]
        {
            new TestLocalizableModel { Guid = stored.Guid, Name = "sk-a!" }, new TestLocalizableModel { Guid = gone.Guid, Name = "sk-b!" },
        });

        act.Should().Throw<EntityNotFoundException>();
        _translations.Read().Where(t => t.FieldName == "Name").Select(t => t.Value).Should().BeEquivalentTo("sk-a!", "sk-b");
    }

    [Fact]
    public async Task A_filter_update_skips_a_row_deleted_since_its_read_and_finishes()
    {
        var bulk = AsyncBulk();
        var gone = Item("sk-gone");
        var kept = Item("sk-kept");
        await bulk.CreateAsync(new[] { gone, kept });
        _asyncEntities.BeforeUpdate = x =>
        {
            if (x.Guid == gone.Guid) throw new EntityNotFoundException(typeof(TestLocalizableModel), x.Guid);
        };

        await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");

        (await _asyncTranslations.ReadAsync(default(CancellationToken))).Where(t => t.FieldName == "Name").Select(t => t.Value)
            .Should().BeEquivalentTo("sk-gone", "sk-kept!");
    }

    [Fact]
    public async Task On_the_default_culture_an_async_filter_update_skips_a_deleted_row_too()
    {
        _context.CurrentCulture = new CultureInfo("en");
        var bulk = AsyncBulk();
        var gone = Item("gone");
        var kept = Item("kept");
        await bulk.CreateAsync(new[] { gone, kept });
        _asyncEntities.BeforeUpdate = x =>
        {
            if (x.Guid == gone.Guid) throw new EntityNotFoundException(typeof(TestLocalizableModel), x.Guid);
        };

        await bulk.UpdateAsync(x => x.Code == "A", x => x.Name += "!");

        (await _asyncEntities.ReadAsync(kept.Guid!.Value))!.Name.Should().Be("kept!");
    }

    [Fact]
    public async Task When_saving_the_stored_items_translations_fails_too_the_not_stored_report_still_wins()
    {
        _context.CurrentCulture = new CultureInfo("en");
        var bulk = AsyncBulk(BestEffortWriteBoundary.Instance);
        var stored = Item("a");
        var gone = Item("b");
        await bulk.CreateAsync(new[] { stored, gone });
        _context.CurrentCulture = new CultureInfo("sk");
        _asyncEntities.MissingOnUpdate = x => x.Guid == gone.Guid;
        _asyncTranslations.BeforeCreate = _ => throw new InvalidOperationException("translation store down");

        var update = new[]
        {
            new TestLocalizableModel { Guid = stored.Guid, Name = "sk-a" }, new TestLocalizableModel { Guid = gone.Guid, Name = "sk-b" },
        };

        var bestEffort = async () => await bulk.UpdateAsync(update);
        var atomic = async () => await AsyncBulk(new RecordingBoundary { Atomic = true }).UpdateAsync(update);

        (await bestEffort.Should().ThrowAsync<EntityNotFoundException>()).Which.Guids.Should().Equal(gone.Guid!.Value);
        await atomic.Should().ThrowAsync<InvalidOperationException>("on an atomic boundary the failed save rolls everything back")
            .WithMessage("translation store down");
    }

    [Fact]
    public async Task A_best_effort_cleanup_is_not_cancelled_once_the_entities_are_deleted()
    {
        var bulk = AsyncBulk(BestEffortWriteBoundary.Instance);
        await bulk.CreateAsync(new[] { Item("sk-a"), Item("sk-b") });
        using var cancel = new CancellationTokenSource();
        _asyncEntities.BeforeDelete = cancel.Cancel;

        await bulk.DeleteAsync(x => x.Code == "A", cancel.Token);

        (await _asyncEntities.ReadAsync(default(CancellationToken))).Should().BeEmpty();
        (await _asyncTranslations.ReadAsync(default(CancellationToken))).Should().BeEmpty("the cleanup ran to the end");
    }

    /// <summary>Records each write it runs; optionally claims atomicity, or runs the write twice as a retry would.</summary>
    private sealed class RecordingBoundary : ILocalizedWriteBoundary
    {
        public List<string> Runs { get; } = new();
        public bool Atomic { get; init; }
        public bool RunTwice { get; init; }

        public bool IsAtomic => Atomic;

        public void Run(Action write)
        {
            Runs.Add("sync");
            write();
            if (RunTwice) write();
        }

        public async Task RunAsync(Func<CancellationToken, Task> write, CancellationToken ct)
        {
            Runs.Add("async");
            await write(ct);
            if (RunTwice) await write(ct);
        }
    }
}
