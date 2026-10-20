using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.MongoDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>TASK-698's translation race contract on <see cref="AsyncMongoDBStore{T}"/>, live.</summary>
public sealed class MongoTranslationRaceContractTests : TranslationRaceContract, IDisposable
{
    private readonly MongoLive _live;
    public MongoTranslationRaceContractTests(ITestOutputHelper output) => _live = new MongoLive(output);

    protected override bool Ready() => _live.Ready();

    // Two stores over one database, as two writers hold them.
    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var settings = _live.NewSettings();
        var ours = new AsyncMongoDBStore<EntityTranslationModel>();
        ours.SetSettings(settings);
        var theirs = new AsyncMongoDBStore<EntityTranslationModel>();
        theirs.SetSettings(settings);
        return (ours, theirs);
    }

    public void Dispose() => _live.Dispose();
}
