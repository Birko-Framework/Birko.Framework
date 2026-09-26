using System;
using Birko.Configuration;
using Birko.Data.Stores;
using Birko.Data.MongoDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-499: StoreLocator.GetStore configures a new store only through ISettingsStore&lt;ISettings&gt;.
/// A store that implements just ISettingsStore&lt;its own Settings&gt; is handed out unconfigured.
/// </summary>
public class StoreLocatorSettingsContractTests
{
    public class ContractModel : Birko.Data.MongoDB.Models.MongoDBModel
    {
    }

    [Theory]
    [InlineData(typeof(MongoDBStore<ContractModel>))]
    [InlineData(typeof(AsyncMongoDBStore<ContractModel>))]
    public void Store_CanBeConfiguredByStoreLocator(Type storeType)
    {
        typeof(ISettingsStore<ISettings>).IsAssignableFrom(storeType).Should().BeTrue(
            "StoreLocator.GetStore only calls SetSettings on an ISettingsStore<ISettings>");
    }
}
