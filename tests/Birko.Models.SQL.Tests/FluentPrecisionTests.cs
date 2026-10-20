using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-692 — a fluent <c>HasPrecision</c> / <c>HasScale</c> reaches a decimal column, by the producer the attributes use,
/// and is refused on any other type. Both used to be accepted and dropped: a decimal got the provider default, and a
/// string's <c>HasPrecision(n)</c> left it unbounded. Each test owns its model types: the registration is process-wide,
/// keyed by type. Pure reflection, no database.
/// </summary>
public class FluentPrecisionTests
{
    public class Priced
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public decimal Weight { get; set; }
        [NamedField] public decimal? Waist { get; set; }
        [NamedField, PrecisionField(10), ScaleField(4)] public decimal Rate { get; set; }
        [NamedField, PrecisionField(12), ScaleField(3)] public decimal Kept { get; set; }
        [NamedField] public decimal Free { get; set; }
    }

    private sealed class PricedMap : IModelMapping<Priced>
    {
        public void Configure(ModelMap<Priced> map)
        {
            map.ToTable("test_fluent_priced");
            map.Property(x => x.Weight).HasPrecision(6).HasScale(2);
            map.Property(x => x.Waist).HasPrecision(5).HasScale(1);
            map.Property(x => x.Rate).HasPrecision(8).HasScale(2);
            map.Property(x => x.Kept).HasScale(1);
        }
    }

    private static DecimalField Field<T>(string property) where T : class
        => Birko.Data.SQL.DataBase.LoadTable(typeof(T)).GetFieldByPropertyName(property).Should().BeAssignableTo<DecimalField>().Subject;

    [Fact]
    public void A_fluent_precision_and_scale_reach_a_decimal_and_a_nullable_decimal()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PricedMap());

        registry.ApplyToDatabase();

        Field<Priced>("Weight").Precision.Should().Be(6);
        Field<Priced>("Weight").Scale.Should().Be(2);
        Field<Priced>("Waist").Should().BeOfType<NullableDecimalField>().Which.Precision.Should().Be(5);
        Field<Priced>("Waist").Scale.Should().Be(1);
        Field<Priced>("Free").Precision.Should().BeNull("a column without one keeps the provider default (TASK-512)");
    }

    [Fact]
    public void The_fluent_values_win_over_the_attributes_and_a_scale_alone_keeps_the_attribute_precision()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PricedMap());

        registry.ApplyToDatabase();

        Field<Priced>("Rate").Precision.Should().Be(8, "the fluent precision wins over [PrecisionField(10)]");
        Field<Priced>("Rate").Scale.Should().Be(2);
        Field<Priced>("Kept").Precision.Should().Be(12, "HasScale alone leaves the attribute's precision");
        Field<Priced>("Kept").Scale.Should().Be(1);
    }

    [Fact]
    public void The_column_type_renders_the_fluent_precision_on_mysql_and_sql_server()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PricedMap());
        registry.ApplyToDatabase();
        var weight = Field<Priced>("Weight");

        new Birko.Data.SQL.Connectors.MySQLConnector(new Birko.Data.SQL.MySQL.Stores.MySqlSettings("h", "d", "u", "p"))
            .ConvertType(weight.Type, weight).Should().Be("DECIMAL(6,2)");
        new Birko.Data.SQL.Connectors.MSSqlConnector(new Birko.Data.SQL.MSSql.Stores.MSSqlSettings("h", "d", "u", "p", 1433))
            .ConvertType(weight.Type, weight).Should().Be("DECIMAL(6,2)");
    }

    public class Wrong
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Name { get; set; } = string.Empty;
        [NamedField] public double Ratio { get; set; }
        [NamedField] public decimal Amount { get; set; }
        [NamedField, IgnoreField] public decimal Ignored { get; set; }
    }

    private sealed class WrongMap : IModelMapping<Wrong>
    {
        private readonly Action<ModelMap<Wrong>> _configure;
        // Parameterless too: RegisterFromAssembly (another test) instantiates every mapping in this assembly.
        public WrongMap() : this(_ => { }) { }
        public WrongMap(Action<ModelMap<Wrong>> configure) => _configure = configure;
        public void Configure(ModelMap<Wrong> map)
        {
            map.ToTable("test_fluent_wrong");
            _configure(map);
        }
    }

    private static Action Applying(Action<ModelMap<Wrong>> configure) => () =>
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WrongMap(configure));
        registry.ApplyToDatabase();
    };

    [Fact]
    public void A_precision_on_a_string_is_refused_and_names_HasMaxLength()
        => Applying(m => m.Property(x => x.Name).HasPrecision(256)).Should().Throw<InvalidOperationException>()
            .WithMessage("*HasPrecision(256)*Wrong.Name*string*HasMaxLength*");

    [Fact]
    public void A_scale_on_a_string_is_refused()
        => Applying(m => m.Property(x => x.Name).HasScale(2)).Should().Throw<InvalidOperationException>().WithMessage("*HasScale(2)*string*");

    [Fact]
    public void A_precision_on_another_type_is_refused()
        => Applying(m => m.Property(x => x.Ratio).HasPrecision(10)).Should().Throw<InvalidOperationException>().WithMessage("*Double*decimal*");

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(5, 6)]
    [InlineData(null, -1)]
    public void An_impossible_precision_or_scale_is_refused(int? precision, int? scale)
        => Applying(m =>
        {
            var property = m.Property(x => x.Amount);
            if (precision is int p) property.HasPrecision(p);
            if (scale is int s) property.HasScale(s);
        }).Should().Throw<InvalidOperationException>();

    [Fact]
    public void A_precision_on_a_property_that_is_no_column_is_refused()
        => Applying(m => m.Property(x => x.Ignored).HasPrecision(10)).Should().Throw<InvalidOperationException>().WithMessage("*not a column*");

    public class BasePriced
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public decimal Amount { get; set; }
    }

    [Table("test_fluent_derived_priced")]
    public class DerivedPriced : BasePriced { }

    private sealed class BasePricedMap : IModelMapping<BasePriced>
    {
        public void Configure(ModelMap<BasePriced> map)
        {
            map.ToTable("test_fluent_base_priced");
            map.Property(x => x.Amount).HasPrecision(9).HasScale(3);
        }
    }

    [Fact]
    public void A_registration_reaches_a_derived_type_as_an_inherited_attribute_would()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BasePricedMap());

        registry.ApplyToDatabase();

        Field<DerivedPriced>("Amount").Precision.Should().Be(9);
        Field<DerivedPriced>("Amount").Scale.Should().Be(3);
    }

    public class Again
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public decimal Amount { get; set; }
    }

    private sealed class AgainMap : IModelMapping<Again>
    {
        public void Configure(ModelMap<Again> map)
        {
            map.ToTable("test_fluent_again");
            map.Property(x => x.Amount).IsRequired().HasPrecision(7).HasScale(2);
        }
    }

    [Fact]
    public void Applying_the_same_precision_twice_keeps_the_loaded_field_and_its_flags()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new AgainMap());
        registry.ApplyToDatabase();
        var first = Field<Again>("Amount");

        registry.ApplyToDatabase();

        Field<Again>("Amount").Should().BeSameAs(first, "the same value changes nothing, so the cached field stays");
        first.IsNotNull.Should().BeTrue();
    }
}
