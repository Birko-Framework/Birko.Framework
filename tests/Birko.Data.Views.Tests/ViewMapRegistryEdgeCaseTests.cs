using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Views.Tests;

/// <summary>
/// A second mapping for the SAME TView (CustomerView) as <see cref="CustomerViewMapping"/>, but
/// mapping only one field — used to observe the last-wins overwrite behavior of Register.
/// </summary>
public class CustomerViewNameOnlyMapping : IViewMapping<CustomerView>
{
    public void Configure(ViewDefinitionBuilder<CustomerView> builder)
    {
        builder
            .From<Customer>()
            .Select<Customer, string>(c => c.Name, v => v.CustomerName);
    }
}

/// <summary>
/// CR-L243: pin the previously-untested edge cases of <see cref="ViewMapRegistry"/> — duplicate
/// TView registration (silent last-wins overwrite of the keyed definition) and the
/// ReflectionTypeLoadException fallback in the assembly scan (CR-L240). The fallback is verified via
/// the <see cref="ViewMapRegistry.GetLoadableTypes"/> seam so no purpose-built broken assembly is needed.
/// </summary>
public class ViewMapRegistryEdgeCaseTests
{
    // ---- CR-L243: duplicate TView registration is a silent last-wins overwrite ----

    [Fact]
    public void Register_DuplicateTView_LastRegistrationWins()
    {
        var registry = new ViewMapRegistry();

        registry.Register(new CustomerViewMapping());          // 2 fields
        registry.GetDefinition<CustomerView>()!.Fields.Should().HaveCount(2);

        registry.Register(new CustomerViewNameOnlyMapping());  // 1 field — overwrites

        // Last-wins: the second definition replaces the first for the same TView key.
        registry.GetDefinition<CustomerView>()!.Fields.Should().HaveCount(1);
        // And it stays a single entry — not two rows for CustomerView.
        registry.GetAll().Count(x => x.Key == typeof(CustomerView)).Should().Be(1);
    }

    // ---- CR-L240 / CR-L243: ReflectionTypeLoadException fallback ----

    [Fact]
    public void GetLoadableTypes_WhenGetTypesThrowsReflectionTypeLoadException_ReturnsOnlyLoadedTypes()
    {
        var partial = new Type?[] { typeof(Customer), null, typeof(Order) };
        Func<Type[]> throwing = () =>
            throw new ReflectionTypeLoadException(partial, new Exception?[] { null, new Exception("boom"), null });

        var loaded = ViewMapRegistry.GetLoadableTypes(throwing).ToList();

        // The null (unloadable) slot is filtered out; the types that loaded are returned.
        loaded.Should().BeEquivalentTo(new[] { typeof(Customer), typeof(Order) });
    }

    [Fact]
    public void GetLoadableTypes_WhenNoException_ReturnsAllTypes()
    {
        var all = new[] { typeof(Customer), typeof(Order) };
        ViewMapRegistry.GetLoadableTypes(() => all).Should().BeEquivalentTo(all);
    }

    [Fact]
    public void RegisterFromAssembly_StillDiscoversMappings_ThroughLoadableTypesWrap()
    {
        // Regression guard: the CR-L240 wrapper must not change happy-path discovery.
        var registry = new ViewMapRegistry();
        registry.RegisterFromAssembly(typeof(CustomerViewMapping).Assembly);

        registry.HasDefinition<CustomerView>().Should().BeTrue();
        registry.HasDefinition<CustomerOrderSummary>().Should().BeTrue();
        registry.HasDefinition<ProductCategorySummary>().Should().BeTrue();
    }
}
