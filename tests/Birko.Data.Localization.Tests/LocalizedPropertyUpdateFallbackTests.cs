using System.Globalization;
using System.Linq;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// CR-L135: a filter-based PropertyUpdate targeting a localizable field on a non-default culture must be
/// persisted as a translation, not written to the base column. The wrapper now detects that case and falls
/// back to the read-modify-write path (which saves translations); other cases keep the fast native path.
/// </summary>
public class LocalizedPropertyUpdateFallbackTests
{
    private readonly InMemoryBulkStore<TestLocalizableModel> _entityStore = new();
    private readonly InMemoryBulkStore<EntityTranslationModel> _translationStore = new();
    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("en"),
        DefaultCulture = new CultureInfo("en")
    };
    private readonly LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel> _wrapper;

    public LocalizedPropertyUpdateFallbackTests()
    {
        _wrapper = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            _entityStore, _translationStore, _context);
    }

    [Fact]
    public void PropertyUpdate_on_localizable_field_in_non_default_culture_writes_a_translation()
    {
        var guid = _wrapper.Create(new TestLocalizableModel { Name = "Widget", Code = "W001" });
        _context.CurrentCulture = new CultureInfo("sk");

        _wrapper.Update(x => x.Code == "W001", new PropertyUpdate<TestLocalizableModel>().Set(x => x.Name, "Komponent"));

        // The fix's essential effect: a Slovak translation row for Name now exists (before the fix the
        // native PropertyUpdate wrote only the base column and NO translation). The base column is also
        // updated — that matches the Action<T> overload's read-modify-write behavior, which this now uses.
        var translation = _translationStore.Read(null, null, null, null)
            .SingleOrDefault(t => t.EntityGuid == guid && t.FieldName == "Name" && t.Culture == "sk");
        translation.Should().NotBeNull("the localizable-field update must be persisted as a translation");
        translation!.Value.Should().Be("Komponent");
    }

    [Fact]
    public void PropertyUpdate_on_localizable_field_in_default_culture_uses_native_base_column()
    {
        var guid = _wrapper.Create(new TestLocalizableModel { Name = "Widget", Code = "W001" });
        // Stay on the default culture "en".

        _wrapper.Update(x => x.Code == "W001", new PropertyUpdate<TestLocalizableModel>().Set(x => x.Name, "Sprocket"));

        _entityStore.Read(guid)!.Name.Should().Be("Sprocket", "default culture writes the base column");
        _translationStore.Read(null, null, null, null).Should().BeEmpty("no translation row for the default culture");
    }

    [Fact]
    public void PropertyUpdate_on_non_localizable_field_in_non_default_culture_uses_native_base_column()
    {
        var guid = _wrapper.Create(new TestLocalizableModel { Name = "Widget", Code = "W001" });
        _context.CurrentCulture = new CultureInfo("sk");

        // Code is not localizable → native PropertyUpdate on the base column, no translation.
        _wrapper.Update(x => x.Code == "W001", new PropertyUpdate<TestLocalizableModel>().Set(x => x.Code, "W002"));

        _entityStore.Read(guid)!.Code.Should().Be("W002");
        _translationStore.Read(null, null, null, null).Should().BeEmpty();
    }
}
