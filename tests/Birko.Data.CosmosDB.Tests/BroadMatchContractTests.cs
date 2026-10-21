using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.CosmosDB.Stores;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>TASK-701's broad-match contract on <see cref="AsyncCosmosDBStore{T}"/>, live.</summary>
public sealed class CosmosBroadMatchContractTests : BroadMatchContract, IDisposable
{
    private readonly CosmosLive _live;
    public CosmosBroadMatchContractTests(ITestOutputHelper output) : base(output) => _live = new CosmosLive(output);

    protected override bool Ready() => _live.Ready();

    protected override (IAsyncBulkStore<BroadMatchProduct> Products, IAsyncBulkStore<EntityTranslationModel> Translations) NewStores()
    {
        var (conn, db, settings) = _live.Next();
        return (new AsyncCosmosDBStore<BroadMatchProduct>(conn, db, null, settings),
                new AsyncCosmosDBStore<EntityTranslationModel>(conn, db, null, settings));
    }

    public void Dispose() => _live.Dispose();
}
