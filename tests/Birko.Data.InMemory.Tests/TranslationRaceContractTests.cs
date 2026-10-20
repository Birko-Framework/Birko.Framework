using Birko.Data.InMemory.Stores;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;

namespace Birko.Data.InMemory.Tests;

/// <summary>TASK-698's translation race contract on <see cref="AsyncInMemoryStore{T}"/>.</summary>
public sealed class InMemoryTranslationRaceContractTests : TranslationRaceContract
{
    // One instance for both writers: in-memory stores do not share data across instances.
    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var store = new AsyncInMemoryStore<EntityTranslationModel>();
        return (store, store);
    }
}
