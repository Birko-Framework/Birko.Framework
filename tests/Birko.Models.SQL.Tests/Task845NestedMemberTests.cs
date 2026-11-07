using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-845. Measured before the fix: <c>ModelMap</c> kept only the last member name, so
/// <c>map.Property(x =&gt; x.Sub.Code).HasColumnName("sub_code").HasMaxLength(5).HasIndex(…)</c> on an entity that also has
/// a top-level <c>Code</c> renamed, bounded and indexed the top-level column, silently; <c>HasUnique</c>, <c>HasPrimary</c>
/// and <c>Ignore</c> did the same. A SQL table has no nested member, so every entry point that takes a member expression
/// now refuses one at <c>Configure</c>, by name, before anything is recorded. Each test owns its model types: the
/// registrations are process-wide, keyed by type.
/// </summary>
public class Task845NestedMemberTests
{
    public class Inner
    {
        public string Code { get; set; } = string.Empty;
        public Inner? Deeper { get; set; }
    }

    [Table("T845Outer")]
    public class Outer
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
        [IgnoreField] public Inner Sub { get; set; } = new Inner();
        [IgnoreField] public int? Maybe { get; set; }
    }

    private static Action Configure(Action<ModelMap<Outer>> configure) => () => configure(new ModelMap<Outer>());

    [Fact]
    public void Property_of_a_nested_member_is_refused_by_name()
    {
        var act = Configure(map => map.Property(x => x.Sub.Code));

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("Sub.Code").And.Contain("Outer").And.Contain("TASK-845");
    }

    [Fact]
    public void Every_entry_point_refuses_a_nested_member()
    {
        Configure(map => map.HasUnique(x => x.Sub.Code)).Should().Throw<NotSupportedException>();
        Configure(map => map.HasPrimary(x => x.Sub.Code)).Should().Throw<NotSupportedException>();
        Configure(map => map.Ignore(x => x.Sub.Code)).Should().Throw<NotSupportedException>();
        Configure(map => map.Property(x => x.Sub.Deeper!.Code)).Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("Sub.Deeper.Code");
        Configure(map => map.Property<object>(x => x.Sub.Code)).Should().Throw<NotSupportedException>("a conversion is looked through");
    }

    [Fact]
    public void A_member_of_a_column_value_is_refused_too()
    {
        // x.Code.Length and x.Maybe.Value used to be recorded as properties "Length" and "Value".
        Configure(map => map.Property(x => x.Code.Length)).Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("Code.Length");
        Configure(map => map.Property(x => x.Maybe!.Value)).Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("Maybe.Value");
    }

    [Fact]
    public void A_member_of_a_captured_object_is_refused()
    {
        var other = new Outer();
        Configure(map => map.Property(x => other.Code)).Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("Code");
    }

    // Open generic, so this assembly's RegisterFromAssembly tests skip it (TASK-766); a closed nested-member mapping would
    // make their scan throw, which is the refusal working.
    private sealed class InlineMapping<TModel> : IModelMapping<TModel> where TModel : class
    {
        private readonly Action<ModelMap<TModel>> _configure;
        public InlineMapping(Action<ModelMap<TModel>> configure) => _configure = configure;
        public void Configure(ModelMap<TModel> map) => _configure(map);
    }

    [Fact]
    public void Registration_refuses_and_leaves_the_top_level_column_untouched()
    {
        var registry = new ModelMapRegistry();

        var act = () => registry.Register(new InlineMapping<Outer>(map => map.Property(x => x.Sub.Code).HasColumnName("sub_code").HasMaxLength(5)));

        act.Should().Throw<NotSupportedException>();
        registry.HasMap<Outer>().Should().BeFalse();
        registry.ApplyToDatabase();
        var code = Birko.Data.SQL.DataBase.LoadTable(typeof(Outer)).GetFieldByPropertyName("Code")!;
        code.Name.Should().Be("Code");
        code.Should().BeOfType<StringField>("nothing bounded the top-level column");
    }

    public class Plain
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
        [NamedField] public int Count { get; set; }
    }

    [Fact]
    public void A_single_segment_member_is_recorded_as_before()
    {
        var map = new ModelMap<Plain>();

        map.Property(x => x.Code).HasColumnName("code_col");
        map.Property<object>(x => x.Count);
        map.HasUnique(x => x.Code).HasPrimary(x => x.Guid).Ignore(x => x.Count);

        map.Properties.Select(p => p.Name).Should().Equal("Code", "Count", "Guid");
        map.Properties[0].ColumnName.Should().Be("code_col");
        map.Properties[0].IsUnique.Should().BeTrue();
        map.Properties[1].IsIgnored.Should().BeTrue();
        map.Properties[2].IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void A_non_member_expression_is_still_an_argument_exception()
    {
        Configure(map => map.Property(x => x.Code + "!")).Should().Throw<ArgumentException>();
    }
}
