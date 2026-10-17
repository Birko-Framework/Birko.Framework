using Birko.Data.SQL.Attributes;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-651: the cross-assembly attribute fallback of <c>DataBase.LoadField</c> took every attribute in
/// <c>Birko.Data.SQL.Attributes</c>, so a property carrying only <c>[ViewField]</c> (same namespace, not a field) threw
/// "comes from another assembly". Only an attribute deriving from <c>Field</c> is rebuilt now.
/// </summary>
public class Task651ViewFieldAttributeTests
{
    public class Source : Birko.Data.Models.AbstractModel { public string? Name { get; set; } }

    public class OnlyViewField
    {
        [ViewField(typeof(Source), nameof(Source.Name))]
        public string? Name { get; set; }
    }

    [Fact]
    public void A_property_with_only_a_ViewField_maps_by_convention_and_does_not_throw()
    {
        var act = () => DataBase.LoadField(typeof(OnlyViewField).GetProperty(nameof(OnlyViewField.Name))!);

        act.Should().NotThrow("[ViewField] is not a field attribute from another assembly");
        act().Should().ContainSingle().Which.Name.Should().Be(nameof(OnlyViewField.Name), "an unattributed property maps by convention");
    }
}
