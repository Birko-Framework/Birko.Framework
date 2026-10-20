using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.CosmosDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>TASK-698's translation race contract on <see cref="AsyncCosmosDBStore{T}"/>, live.</summary>
public sealed class CosmosTranslationRaceContractTests : TranslationRaceContract, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosTranslationRaceContractTests(ITestOutputHelper output) => _live = new CosmosLive(output);

    protected override bool Ready() => _live.Ready();

    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var (conn, db, settings) = _live.Next();
        return (new AsyncCosmosDBStore<EntityTranslationModel>(conn, db, null, settings),
                new AsyncCosmosDBStore<EntityTranslationModel>(conn, db, null, settings));
    }

    public void Dispose() => _live.Dispose();
}
