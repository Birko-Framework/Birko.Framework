using System;
using System.Collections.Generic;
using Birko.Data.InMemory.Stores;
using Birko.Localization.Data;

namespace Birko.Localization.Data.Tests;

/// <summary>
/// In-memory async bulk store for TranslationModel, used in tests.
/// Backed by Birko.Data.InMemory's <see cref="AsyncInMemoryStore{T}"/>.
/// </summary>
internal class TestTranslationStore : AsyncInMemoryStore<TranslationModel>
{
    public void Seed(IEnumerable<TranslationModel> items)
    {
        foreach (var item in items)
        {
            item.Guid ??= Guid.NewGuid();
            _items[item.Guid.Value] = item;
        }
    }
}
