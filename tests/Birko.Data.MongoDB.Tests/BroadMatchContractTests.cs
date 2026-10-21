using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.MongoDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>TASK-701's broad-match contract on <see cref="AsyncMongoDBStore{T}"/>, live.</summary>
public sealed class MongoBroadMatchContractTests : BroadMatchContract, IDisposable
{
    private readonly MongoLive _live;
    public MongoBroadMatchContractTests(ITestOutputHelper output) : base(output) => _live = new MongoLive(output);

    protected override bool Ready() => _live.Ready();

    protected override (IAsyncBulkStore<BroadMatchProduct> Products, IAsyncBulkStore<EntityTranslationModel> Translations) NewStores()
    {
        var settings = _live.NewSettings();
        var products = new AsyncMongoDBStore<BroadMatchProduct>();
        products.SetSettings(settings);
        var translations = new AsyncMongoDBStore<EntityTranslationModel>();
        translations.SetSettings(settings);
        return (products, translations);
    }

    public void Dispose() => _live.Dispose();
}
