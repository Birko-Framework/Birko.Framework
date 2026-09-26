using System;
using Birko.Configuration;
using Birko.Data.Stores;
using Birko.Data.ElasticSearch.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-499: StoreLocator.GetStore configures a new store only through ISettingsStore&lt;ISettings&gt;.
/// A store that implements just ISettingsStore&lt;its own Settings&gt; is handed out unconfigured.
/// </summary>
public class StoreLocatorSettingsContractTests
{
    public class ContractModel : Birko.Data.Models.AbstractModel
    {
    }

    [Theory]
    [InlineData(typeof(ElasticSearchStore<ContractModel>))]
    [InlineData(typeof(AsyncElasticSearchStore<ContractModel>))]
    public void Store_CanBeConfiguredByStoreLocator(Type storeType)
    {
        typeof(ISettingsStore<ISettings>).IsAssignableFrom(storeType).Should().BeTrue(
            "StoreLocator.GetStore only calls SetSettings on an ISettingsStore<ISettings>");
    }

    [Fact]
    public void StoreLocator_GetStore_ConfiguresTheStore()
    {
        var settings = new Birko.Data.ElasticSearch.Stores.Settings { Location = "http://localhost:1", Name = "task499" + Guid.NewGuid().ToString("N") };

        var store = StoreLocator.GetStore<ElasticSearchStore<ContractModel>, ISettings>(settings);

        store.Connector.Should().NotBeNull("the locator must pass the settings on to the store it creates");
    }
}
