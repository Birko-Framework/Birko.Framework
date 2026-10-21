using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.ElasticSearch.Stores;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>TASK-701's broad-match contract on <see cref="AsyncElasticSearchStore{T}"/>, live.</summary>
public sealed class ElasticBroadMatchContractTests : BroadMatchContract, IDisposable
{
    private readonly ElasticLive _live;
    public ElasticBroadMatchContractTests(ITestOutputHelper output) : base(output) => _live = new ElasticLive(output);

    protected override bool Ready() => _live.Ready();

    protected override (IAsyncBulkStore<BroadMatchProduct> Products, IAsyncBulkStore<EntityTranslationModel> Translations) NewStores()
    {
        var settings = _live.NewSettings();
        var products = new AsyncElasticSearchStore<BroadMatchProduct>();
        products.SetSettings(settings);
        var translations = new AsyncElasticSearchStore<EntityTranslationModel>();
        translations.SetSettings(settings);
        return (products, translations);
    }

    protected override Task SettleAsync()
    {
        ElasticLive.Refresh();
        return Task.CompletedTask;
    }

    public void Dispose() => _live.Dispose();
}
