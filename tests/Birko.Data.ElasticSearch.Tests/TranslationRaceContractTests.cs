using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.ElasticSearch.Stores;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>TASK-698's translation race contract on <see cref="AsyncElasticSearchStore{T}"/>, live.</summary>
public sealed class ElasticTranslationRaceContractTests : TranslationRaceContract, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticTranslationRaceContractTests(ITestOutputHelper output) => _live = new ElasticLive(output);

    protected override bool Ready() => _live.Ready();

    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var settings = _live.NewSettings();
        var ours = new AsyncElasticSearchStore<EntityTranslationModel>();
        ours.SetSettings(settings);
        var theirs = new AsyncElasticSearchStore<EntityTranslationModel>();
        theirs.SetSettings(settings);
        return (ours, theirs);
    }

    protected override Task SettleAsync(IAsyncBulkStore<EntityTranslationModel> store)
    {
        ElasticLive.Refresh();
        return Task.CompletedTask;
    }

    public void Dispose() => _live.Dispose();
}

/// <summary>
/// TASK-698: the sync store's realtime read, which the translation upsert re-reads through after a refusal. A search
/// sees a write only after a refresh; a realtime GET sees it at once.
/// </summary>
public sealed class ElasticSyncRealtimeReadTests : IDisposable
{
    private readonly ElasticLive _live;
    public ElasticSyncRealtimeReadTests(ITestOutputHelper output) => _live = new ElasticLive(output);

    [Xunit.Fact]
    public void A_realtime_read_sees_a_write_before_the_index_refreshes()
    {
        if (!_live.Ready()) return;
        var store = new ElasticSearchStore<EntityTranslationModel>();
        store.SetSettings(_live.NewSettings());
        var row = new EntityTranslationModel { EntityGuid = Guid.NewGuid(), EntityType = "Product", FieldName = "Name", Culture = "sk", Value = "v" };
        var guid = store.Create(row);

        FluentAssertions.AssertionExtensions.Should(store.ReadRealtime(guid)).NotBeNull("a realtime GET does not wait for the refresh");
        FluentAssertions.AssertionExtensions.Should(store.ReadRealtime(Guid.NewGuid())).BeNull();
    }

    public void Dispose() => _live.Dispose();
}
