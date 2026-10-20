using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.JSON.Stores;

namespace Birko.Data.JSON.Tests;

/// <summary>TASK-698's translation race contract on <see cref="AsyncJsonStore{T}"/>.</summary>
public sealed class JsonTranslationRaceContractTests : TranslationRaceContract, IDisposable
{
    private readonly TempDirs _dirs = new();

    // One instance for both writers: a JSON store holds its rows per instance.
    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var store = new AsyncJsonStore<EntityTranslationModel>();
        store.SetSettings(_dirs.NewSettings("birko-json-698-"));
        return (store, store);
    }

    public void Dispose() => _dirs.Dispose();
}
