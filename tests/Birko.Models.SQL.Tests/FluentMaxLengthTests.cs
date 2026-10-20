using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-691 — a fluent <c>HasMaxLength</c> reaches the column type, by the producer the attribute uses. Each test owns
/// its model types: the registration is process-wide, keyed by type. Pure reflection, no database.
/// </summary>
public class FluentMaxLengthTests
{
    public class Bounded
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
        [NamedField, MaxLengthField(10)] public string Name { get; set; } = string.Empty;
        [NamedField] public byte[] Blob { get; set; } = Array.Empty<byte>();
        [NamedField] public string Free { get; set; } = string.Empty;
    }

    private sealed class BoundedMap : IModelMapping<Bounded>
    {
        public void Configure(ModelMap<Bounded> map)
        {
            map.ToTable("test_fluent_bounded");
            map.Property(x => x.Code).HasColumnName("code_col").IsUnique().HasMaxLength(64).HasIndex("IX_test_fluent_bounded_code");
            map.Property(x => x.Name).HasMaxLength(20);
            map.Property(x => x.Blob).HasMaxLength(16);
        }
    }

    [Fact]
    public void A_fluent_length_bounds_a_string_and_a_byte_array_and_wins_over_the_attribute()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BoundedMap());

        registry.ApplyToDatabase();

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Bounded));
        table.GetFieldByPropertyName("Code").Should().BeOfType<CharField>().Which.Lenght.Should().Be(64);
        table.GetFieldByPropertyName("Name").Should().BeOfType<CharField>().Which.Lenght.Should().Be(20, "the fluent length wins over [MaxLengthField(10)]");
        table.GetFieldByPropertyName("Blob").Should().BeOfType<BinaryField>().Which.MaxLength.Should().Be(16);
        table.GetFieldByPropertyName("Free").Should().BeOfType<StringField>("a column without a length stays unbounded");
    }

    [Fact]
    public void The_rename_flags_and_index_survive_the_rebuild_the_length_causes()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BoundedMap());

        registry.ApplyToDatabase();

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Bounded));
        var code = table.GetFieldByPropertyName("Code")!;
        code.Name.Should().Be("code_col");
        code.IsUnique.Should().BeTrue();
        table.Indexes!["IX_test_fluent_bounded_code"].Columns.Select(c => c.ColumnName).Should().Equal("code_col");
    }

    public class Twice
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
    }

    private sealed class TwiceMap : IModelMapping<Twice>
    {
        public void Configure(ModelMap<Twice> map)
        {
            map.ToTable("test_fluent_twice");
            map.Property(x => x.Code).IsRequired().HasMaxLength(32);
        }
    }

    [Fact]
    public void Applying_twice_keeps_the_same_fields_and_their_flags()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new TwiceMap());
        registry.ApplyToDatabase();
        var first = Birko.Data.SQL.DataBase.LoadTable(typeof(Twice)).GetFieldByPropertyName("Code");

        registry.ApplyToDatabase();

        var second = Birko.Data.SQL.DataBase.LoadTable(typeof(Twice)).GetFieldByPropertyName("Code");
        second.Should().BeSameAs(first, "the same length registered again rebuilds nothing");
        second!.IsNotNull.Should().BeTrue();
        second.Should().BeOfType<CharField>().Which.Lenght.Should().Be(32);
    }

    public class Changed
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
    }

    private abstract class ChangedMap : IModelMapping<Changed>
    {
        protected abstract int Length { get; }

        public void Configure(ModelMap<Changed> map)
        {
            map.ToTable("test_fluent_changed");
            map.Property(x => x.Code).HasColumnName("code_col").IsUnique().HasMaxLength(Length);
        }
    }

    private sealed class ChangedMap64 : ChangedMap
    {
        protected override int Length => 64;
    }

    private sealed class ChangedMap32 : ChangedMap
    {
        protected override int Length => 32;
    }

    [Fact]
    public void A_changed_length_rebuilds_the_field_and_the_mapping_is_applied_to_the_new_one()
    {
        var first = new ModelMapRegistry();
        first.Register(new ChangedMap64());
        first.ApplyToDatabase();
        var second = new ModelMapRegistry();
        second.Register(new ChangedMap32());

        second.ApplyToDatabase();

        var code = Birko.Data.SQL.DataBase.LoadTable(typeof(Changed)).GetFieldByPropertyName("Code")!;
        code.Should().BeOfType<CharField>().Which.Lenght.Should().Be(32);
        code.Name.Should().Be("code_col");
        code.IsUnique.Should().BeTrue();
    }

    public class NotText
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public int Count { get; set; }
    }

    private sealed class NotTextMap : IModelMapping<NotText>
    {
        public void Configure(ModelMap<NotText> map)
        {
            map.ToTable("test_fluent_not_text");
            map.Property(x => x.Count).HasMaxLength(5);
        }
    }

    [Fact]
    public void A_length_on_a_column_that_has_none_is_refused()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new NotTextMap());

        var act = registry.ApplyToDatabase;

        act.Should().Throw<InvalidOperationException>().WithMessage("*NotText.Count*only a string or byte[]*");
    }

    [Table("test_fluent_untouched")]
    public class Untouched
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
    }

    private sealed class UntouchedMap : IModelMapping<Untouched>
    {
        public void Configure(ModelMap<Untouched> map)
        {
            map.ToTable("test_fluent_untouched");
            map.Property(x => x.Code).IsRequired();
        }
    }

    [Fact]
    public void A_type_without_a_fluent_length_keeps_its_fields()
    {
        var before = Birko.Data.SQL.DataBase.LoadTable(typeof(Untouched))?.GetFieldByPropertyName("Code");
        var registry = new ModelMapRegistry();
        registry.Register(new UntouchedMap());

        registry.ApplyToDatabase();

        var after = Birko.Data.SQL.DataBase.LoadTable(typeof(Untouched)).GetFieldByPropertyName("Code");
        after.Should().BeOfType<StringField>();
        if (before != null)
        {
            after.Should().BeSameAs(before);
        }
    }

    [Table("test_fluent_inherits")]
    public class InheritsBounded : Bounded
    {
    }

    [Fact]
    public void A_type_derived_from_a_mapped_model_gets_its_length_as_an_inherited_attribute_would()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new BoundedMap());
        registry.ApplyToDatabase();

        var field = Birko.Data.SQL.DataBase.LoadTable(typeof(InheritsBounded)).GetFieldByPropertyName("Name");

        field.Should().BeOfType<CharField>().Which.Lenght.Should().Be(20);
    }

    public class SlugBase
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Slug { get; set; } = string.Empty;
    }

    public class SlugModel : SlugBase
    {
    }

    private sealed class SlugModelMap : IModelMapping<SlugModel>
    {
        public void Configure(ModelMap<SlugModel> map)
        {
            map.ToTable("test_fluent_slug");
            map.Property(x => x.Slug).HasMaxLength(48);
        }
    }

    [Fact]
    public void A_field_built_from_a_lambda_over_a_base_declared_property_gets_the_models_length()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new SlugModelMap());
        registry.ApplyToDatabase();

        var fromTable = Birko.Data.SQL.DataBase.LoadTable(typeof(SlugModel)).GetFieldByPropertyName("Slug");
        var fromLambda = Birko.Data.SQL.DataBase.GetField<SlugModel, string>(x => x.Slug);

        fromTable.Should().BeOfType<CharField>().Which.Lenght.Should().Be(48);
        fromLambda.Should().BeOfType<CharField>().Which.Lenght.Should().Be(48, "filters and updates must see the column the table declares");
    }

    public class HiddenBase
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [IgnoreField] public int Code { get; set; }
    }

    public class Hiding : HiddenBase
    {
        [NamedField] public new string Code { get; set; } = string.Empty;
    }

    private sealed class HidingMap : IModelMapping<Hiding>
    {
        public void Configure(ModelMap<Hiding> map)
        {
            map.ToTable("test_fluent_hiding");
            map.Property(x => x.Code).HasMaxLength(12);
        }
    }

    [Fact]
    public void A_property_hidden_with_new_is_resolved_to_the_most_derived_one()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new HidingMap());

        registry.ApplyToDatabase();

        Birko.Data.SQL.DataBase.LoadTable(typeof(Hiding)).Fields.Values.Where(f => f.Property?.Name == "Code")
            .Should().ContainSingle().Which.Should().BeOfType<CharField>().Which.Lenght.Should().Be(12);
    }

    public class Refusals
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Zero { get; set; } = string.Empty;
        [NamedField] public string Computed => Zero + "!";
        [NamedField] public string Skipped { get; set; } = string.Empty;
    }

    private sealed class ZeroMap : IModelMapping<Refusals>
    {
        public void Configure(ModelMap<Refusals> map) => map.ToTable("test_fluent_refusals").Property(x => x.Zero).HasMaxLength(0);
    }

    private sealed class ComputedMap : IModelMapping<Refusals>
    {
        public void Configure(ModelMap<Refusals> map) => map.ToTable("test_fluent_refusals").Property(x => x.Computed).HasMaxLength(10);
    }

    private sealed class IgnoredMap : IModelMapping<Refusals>
    {
        public void Configure(ModelMap<Refusals> map) => map.ToTable("test_fluent_refusals").Property(x => x.Skipped).IsIgnored().HasMaxLength(10);
    }

    [Theory]
    [InlineData(typeof(ZeroMap), "*must be positive*")]
    [InlineData(typeof(ComputedMap), "*not a column*")]
    [InlineData(typeof(IgnoredMap), "*not a column*")]
    public void A_length_that_cannot_be_honoured_is_refused(Type mapType, string message)
    {
        var registry = new ModelMapRegistry();
        registry.Register((IModelMapping<Refusals>)Activator.CreateInstance(mapType)!);

        var act = registry.ApplyToDatabase;

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void A_length_that_is_not_positive_is_refused_at_registration()
    {
        var act = () => Birko.Data.SQL.DataBase.RegisterMaxLength(typeof(Untouched), "Code", 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
