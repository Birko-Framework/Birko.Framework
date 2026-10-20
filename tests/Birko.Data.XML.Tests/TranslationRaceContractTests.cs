using System;
using System.Threading.Tasks;
using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Tests.Shared.LocalizationContract;
using Birko.Data.XML.Stores;

namespace Birko.Data.XML.Tests;

/// <summary>TASK-698's translation race contract on <see cref="AsyncXmlStore{T}"/>.</summary>
public sealed class XmlTranslationRaceContractTests : TranslationRaceContract, IDisposable
{
    private readonly TempDirs _dirs = new();

    // One instance for both writers: an XML store holds its rows per instance.
    protected override (IAsyncBulkStore<EntityTranslationModel> Ours, IAsyncBulkStore<EntityTranslationModel> Theirs) NewTranslationStores()
    {
        var store = new AsyncXmlStore<EntityTranslationModel>();
        store.SetSettings(_dirs.NewSettings());
        return (store, store);
    }

    public void Dispose() => _dirs.Dispose();
}
