using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-639: <see cref="DataBase.ResolveViewConditions"/> names a persistent view's WHERE by the view's own columns, and
/// leaves the on-the-fly path, unknown names and the caller's conditions alone.
/// </summary>
public class Task639ViewConditionResolutionTests
{
    [Table("T639RPersons")]
    public class Person : AbstractModel
    {
        public string? Name { get; set; }
        public string? City { get; set; }
    }

    [Table("T639RProducts")]
    public class Product : AbstractModel
    {
        public string? Name { get; set; }
        public Guid OwnerId { get; set; }
    }

    public class OwnedView
    {
        public string? OwnerName { get; set; }
        public string? OwnerCity { get; set; }
        public string? ProductName { get; set; }
    }

    private static Tables.View View() => SqlViewTranslator.Translate(new ViewDefinitionBuilder<OwnedView>()
        .HasName("T639Owned")
        .From<Product>()
        .Join<Product, Person, Guid?>(p => p.OwnerId, o => o.Guid)
        .Select<Person, string>(o => o.Name!, v => v.OwnerName!)
        .Select<Person, string>(o => o.City!, v => v.OwnerCity!)
        .Select<Product, string>(p => p.Name!, v => v.ProductName!)
        .Build());

    private static Condition Eq(string name, object value) => Condition.AndValue(name, value);

    [Fact]
    public void A_qualified_source_name_becomes_the_view_property_on_the_persistent_path()
    {
        var resolved = DataBase.ResolveViewConditions(View(), new[] { Eq("T639RPersons.Name", "a"), Eq("T639RProducts.Name", "b") }, persistent: true)!.ToList();

        resolved.Select(c => c.Name).Should().Equal("OwnerName", "ProductName");
        resolved[0].Values!.Cast<object>().Should().Equal("a");
    }

    [Fact]
    public void The_on_the_fly_path_is_untouched()
    {
        var conditions = new[] { Eq("T639RPersons.Name", "a") };

        DataBase.ResolveViewConditions(View(), conditions, persistent: false).Should().BeSameAs(conditions);
    }

    [Fact]
    public void Groups_and_field_comparisons_are_resolved_and_the_callers_conditions_are_not_modified()
    {
        var inner = Condition.AndField("T639RPersons.City", "T639RProducts.Name");
        var group = Condition.OrSubCondition(new[] { inner, Eq("City", "x") });

        var resolved = DataBase.ResolveViewConditions(View(), new[] { group }, persistent: true)!.Single();

        var subs = resolved.SubConditions!.ToList();
        subs[0].Name.Should().Be("OwnerCity");
        subs[0].Values!.Cast<object>().Should().Equal("ProductName");
        subs[1].Name.Should().Be("OwnerCity", "an unqualified name one field owns resolves too");
        resolved.IsOr.Should().BeTrue();
        inner.Name.Should().Be("T639RPersons.City");
        inner.Values!.Cast<object>().Should().Equal("T639RProducts.Name");
    }

    [Fact]
    public void A_view_property_name_or_an_unknown_name_passes_through()
    {
        var resolved = DataBase.ResolveViewConditions(View(), new[] { Eq("OwnerName", "a"), Eq("Unrelated", 1) }, persistent: true)!;

        resolved.Select(c => c.Name).Should().Equal("OwnerName", "Unrelated");
    }

    [Fact]
    public void An_unqualified_name_two_fields_share_is_refused()
    {
        var act = () => DataBase.ResolveViewConditions(View(), new[] { Eq("Name", "a") }, persistent: true)!.ToList();

        act.Should().Throw<ArgumentException>().WithMessage("*'Name'*more than one*");
    }
}
