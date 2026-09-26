using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-498 — an increment on a localizable field has no translation-row meaning, so the wrappers refuse it,
/// on <b>every</b> culture: before this rule the default culture took the native path and a non-default one the
/// read-modify-write fallback, so the same call would have passed or failed depending on the request's culture.
/// An increment on a field that is not localizable passes through untouched.
/// </summary>
public class LocalizedPropertyUpdateIncrementTests
{
    public class PricedModel : AbstractModel, ILocalizable
    {
        public decimal Price { get; set; }   // localizable: a per-culture price
        public int Stock { get; set; }       // not localizable
        public string Label { get; set; } = string.Empty;   // localizable

        public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Price), nameof(Label) };
    }

    private static TestEntityLocalizationContext Context(string current) => new()
    {
        CurrentCulture = new CultureInfo(current),
        DefaultCulture = new CultureInfo("en")
    };

    private static LocalizedBulkStoreWrapper<IBulkStore<PricedModel>, PricedModel> SyncWrapper(InMemoryBulkStore<PricedModel> store, string culture)
        => new(store, new InMemoryBulkStore<EntityTranslationModel>(), Context(culture));

    private static AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<PricedModel>, PricedModel> AsyncWrapper(InMemoryAsyncBulkStore<PricedModel> store, string culture)
        => new(store, new InMemoryAsyncBulkStore<EntityTranslationModel>(), Context(culture));

    [Theory]
    [InlineData("en")]
    [InlineData("sk")]
    public void Sync_Increment_On_A_Localizable_Field_Is_Refused(string culture)
    {
        var wrapper = SyncWrapper(new InMemoryBulkStore<PricedModel>(), culture);

        var act = () => wrapper.Update(x => x.Stock > 0, new PropertyUpdate<PricedModel>().Increment(x => x.Price, 1m));

        act.Should().Throw<NotSupportedException>().WithMessage("*Price*localizable*");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("sk")]
    public async Task Async_Increment_On_A_Localizable_Field_Is_Refused(string culture)
    {
        var wrapper = AsyncWrapper(new InMemoryAsyncBulkStore<PricedModel>(), culture);

        var act = () => wrapper.UpdateAsync(x => x.Stock > 0, new PropertyUpdate<PricedModel>().Increment(x => x.Price, 1m));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*Price*localizable*");
    }

    /// <summary>
    /// A localizable Set beside a non-localizable increment is replayed as read-modify-save on a non-default
    /// culture, where the increment would silently stop being atomic — so it is refused there, and native on the
    /// default culture.
    /// </summary>
    [Fact]
    public async Task Increment_Beside_A_Localizable_Set_Is_Refused_On_A_Non_Default_Culture()
    {
        var update = () => new PropertyUpdate<PricedModel>().Set(x => x.Label, "x").Increment(x => x.Stock, 1);
        var sync = SyncWrapper(new InMemoryBulkStore<PricedModel>(), "sk");
        var async = AsyncWrapper(new InMemoryAsyncBulkStore<PricedModel>(), "sk");

        sync.Invoking(w => w.Update(x => x.Stock > 0, update())).Should().Throw<NotSupportedException>().WithMessage("*Stock*read-modify-save*");
        await async.Invoking(w => w.UpdateAsync(x => x.Stock > 0, update(), default)).Should().ThrowAsync<NotSupportedException>().WithMessage("*Stock*");
    }

    [Fact]
    public void Increment_Beside_A_Localizable_Set_Applies_On_The_Default_Culture()
    {
        var store = new InMemoryBulkStore<PricedModel>();
        var guid = store.Create(new PricedModel { Stock = 5 });

        SyncWrapper(store, "en").Update(x => x.Stock > 0, new PropertyUpdate<PricedModel>().Set(x => x.Label, "x").Increment(x => x.Stock, 1));

        store.Read(guid)!.Stock.Should().Be(6);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("sk")]
    public void Sync_Increment_On_A_Non_Localizable_Field_Applies(string culture)
    {
        var store = new InMemoryBulkStore<PricedModel>();
        var guid = store.Create(new PricedModel { Stock = 5 });

        SyncWrapper(store, culture).Update(x => x.Stock > 0, new PropertyUpdate<PricedModel>().Increment(x => x.Stock, 2));

        store.Read(guid)!.Stock.Should().Be(7);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("sk")]
    public async Task Async_Increment_On_A_Non_Localizable_Field_Applies(string culture)
    {
        var store = new InMemoryAsyncBulkStore<PricedModel>();
        var guid = await store.CreateAsync(new PricedModel { Stock = 5 });

        await AsyncWrapper(store, culture).UpdateAsync(x => x.Stock > 0, new PropertyUpdate<PricedModel>().Decrement(x => x.Stock, 2));

        (await store.ReadAsync(guid))!.Stock.Should().Be(3);
    }
}
