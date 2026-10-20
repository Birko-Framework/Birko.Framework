using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.RavenDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.RavenDB.Tests.Stores;

/// <summary>TASK-698's translation race contract on <see cref="AsyncRavenDBStore{T}"/>, live.</summary>
public sealed class RavenTranslationRaceContractTests : TranslationRaceContract, IDisposable
{
    private readonly RavenLive _live;
    private AsyncRavenDBStore<EntityTranslationModel>? _theirs;
    public RavenTranslationRaceContractTests(ITestOutputHelper output) => _live = new RavenLive(output);

    protected override bool Ready() => _live.Ready();

    // Two stores over one database: the second is opened on the database the tracked one created.
    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var ours = _live.Track((url, db) =>
        {
            _theirs = new AsyncRavenDBStore<EntityTranslationModel>(url, db);
            return new AsyncRavenDBStore<EntityTranslationModel>(url, db);
        });
        return (ours, _theirs!);
    }

    // A RavenDB query reads an index that catches up with a write asynchronously, as the other live tests here settle.
    protected override Task SettleAsync(IAsyncBulkStore<EntityTranslationModel> store) => Task.Delay(1500);

    public void Dispose()
    {
        _theirs?.Dispose();
        _live.Dispose();
    }
}
