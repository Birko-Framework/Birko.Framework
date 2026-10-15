using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-567: a model that declares no key gets its <c>AbstractModel.Guid</c> as the key. Only then: a declared key, a
/// composite one included, is never touched, and a key declared fluently after the table is loaded replaces the
/// implicit one instead of joining it into a composite. Each test has its own type, because LoadTable caches per type.
/// </summary>
public class ImplicitKeyTests
{
    [Table("ik_keyless")]
    public class KeyLess : AbstractModel
    {
        public string? Name { get; set; }
    }

    [Table("ik_composite_attr")]
    public class CompositeByAttribute : AbstractModel
    {
        [PrimaryField]
        public string Tenant { get; set; } = string.Empty;

        [PrimaryField]
        public int Number { get; set; }
    }

    [Table("ik_fluent_single")]
    public class FluentSingle : AbstractModel
    {
        public string Code { get; set; } = string.Empty;
    }

    [Table("ik_fluent_composite")]
    public class FluentComposite : AbstractModel
    {
        public string Tenant { get; set; } = string.Empty;
        public int Number { get; set; }
    }

    private sealed class FluentSingleMap : IModelMapping<FluentSingle>
    {
        public void Configure(ModelMap<FluentSingle> map) => map.ToTable("ik_fluent_single").HasPrimary(x => x.Code);
    }

    private sealed class FluentCompositeMap : IModelMapping<FluentComposite>
    {
        public void Configure(ModelMap<FluentComposite> map)
            => map.ToTable("ik_fluent_composite").HasPrimary(x => x.Tenant).HasPrimary(x => x.Number);
    }

    private static string[] Keys(Type type)
        => Birko.Data.SQL.DataBase.LoadTable(type).Fields.Values.Where(f => f.IsPrimary)
            .Select(f => f.Property.Name).OrderBy(n => n).ToArray();

    [Fact]
    public void A_model_without_a_declared_key_is_keyed_by_its_guid()
        => Keys(typeof(KeyLess)).Should().Equal("Guid");

    [Fact]
    public void A_composite_key_declared_by_attributes_is_left_exactly_as_declared()
        => Keys(typeof(CompositeByAttribute)).Should().Equal("Number", "Tenant");

    [Fact]
    public void A_fluent_key_replaces_the_implicit_guid_key()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new FluentSingleMap());
        registry.ApplyToDatabase();

        Keys(typeof(FluentSingle)).Should().Equal("Code");
    }

    [Fact]
    public void A_fluent_composite_key_is_exactly_the_declared_columns()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new FluentCompositeMap());
        registry.ApplyToDatabase();

        Keys(typeof(FluentComposite)).Should().Equal("Number", "Tenant");
    }
}
