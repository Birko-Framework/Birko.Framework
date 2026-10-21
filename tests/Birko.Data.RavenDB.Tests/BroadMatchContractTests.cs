using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.RavenDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>TASK-701's broad-match contract on <see cref="AsyncRavenDBStore{T}"/>, live.</summary>
public sealed class RavenBroadMatchContractTests : BroadMatchContract, IDisposable
{
    private readonly RavenLive _live;
    public RavenBroadMatchContractTests(ITestOutputHelper output) : base(output) => _live = new RavenLive(output);

    protected override bool Ready() => _live.Ready();

    // Two stores over one database: the second is opened on the database the tracked one created. Queries read an index
    // that catches up later; the contract waits for the counts it expects.
    protected override (IAsyncBulkStore<BroadMatchProduct> Products, IAsyncBulkStore<EntityTranslationModel> Translations) NewStores()
    {
        AsyncRavenDBStore<EntityTranslationModel>? translations = null;
        var products = _live.Track((url, db) =>
        {
            translations = _live.Track((u, _) => new AsyncRavenDBStore<EntityTranslationModel>(u, db));
            return new AsyncRavenDBStore<BroadMatchProduct>(url, db);
        });
        return (products, translations!);
    }

    public void Dispose() => _live.Dispose();
}
