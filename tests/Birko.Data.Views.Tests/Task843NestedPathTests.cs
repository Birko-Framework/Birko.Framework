using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Views.Tests;

/// <summary>
/// TASK-843: the builder recorded only the last member name of a path, so <c>Select(r =&gt; r.Sub.X, ...)</c> became
/// source field <c>X</c> and every provider read the top-level <c>X</c> (reproduced on SQLite, Cosmos DB, MongoDB,
/// RavenDB and Elasticsearch). A Select now records the whole path; every other builder call, and every view property,
/// takes a property of its parameter only and refuses a path by name, as does a member that is not a stored one.
/// </summary>
public class Task843NestedPathTests
{
    public class Deeper { public int Level { get; set; } }
    public class Sub { public int X { get; set; } public string Name { get; set; } = ""; public Deeper? Deep { get; set; } }

    public class Row
    {
        public Guid Key { get; set; }
        public int X { get; set; }
        public int? Maybe { get; set; }
        public string Name { get; set; } = "";
        public DateTime When { get; set; }
        public List<int> Items { get; set; } = new();
        public Sub Sub { get; set; } = new();
    }

    public class Flat
    {
        public Guid Key { get; set; }
        public int X { get; set; }
        public int? Level { get; set; }
        public int Count { get; set; }
        public Sub? Child { get; set; }
    }

    [Fact]
    public void A_top_level_select_records_its_one_name_as_before()
    {
        var field = new ViewDefinitionBuilder<Flat>().From<Row>().Select<Row, int>(r => r.X, v => v.X).Build().Fields[0];

        field.SourceProperty.Should().Be("X");
        field.SourcePath.Should().Equal("X");
        field.IsNested.Should().BeFalse();
    }

    [Fact]
    public void A_nested_select_records_the_whole_path()
    {
        var definition = new ViewDefinitionBuilder<Flat>().From<Row>()
            .Select<Row, int>(r => r.Sub.X, v => v.X)
            .Select<Row, int?>(r => r.Sub.Deep!.Level, v => v.Level)
            .Build();

        definition.Fields[0].SourcePath.Should().Equal("Sub", "X");
        definition.Fields[0].SourceProperty.Should().Be("Sub.X");
        definition.Fields[0].IsNested.Should().BeTrue();
        definition.Fields[1].SourcePath.Should().Equal("Sub", "Deep", "Level");
    }

    [Fact]
    public void A_final_nullable_value_is_the_value_itself()
    {
        var definition = new ViewDefinitionBuilder<Flat>().From<Row>()
            .Select<Row, int>(r => r.Maybe!.Value, v => v.X)
            .Select<Row, int?>(r => r.Sub.Deep!.Level, v => v.Level)
            .Build();

        definition.Fields[0].SourcePath.Should().Equal("Maybe");
        definition.Fields[0].IsNested.Should().BeFalse();
    }

    [Fact]
    public void The_one_name_constructor_is_a_one_segment_path()
    {
        var field = new FieldSelector(typeof(Row), "X", "X");
        field.SourcePath.Should().Equal("X");
        field.IsNested.Should().BeFalse();
    }

    public static IEnumerable<object[]> Refused() => new[]
    {
        new object[] { "nested view property", "View property 'Flat.Child.X'" },
        new object[] { "nested group key", "GroupBy key 'Row.Sub.X'" },
        new object[] { "nested join key", "Join key 'Row.Sub.X'" },
        new object[] { "nested aggregate source", "Aggregate source 'Row.Sub.X'" },
        new object[] { "string length", "'Length' of String" },
        new object[] { "collection count", "'Count' of List" },
        new object[] { "struct member", "'Year' of DateTime" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_path_a_builder_call_cannot_record_is_refused_by_name(string shape, string expected)
    {
        Action act = shape switch
        {
            "nested view property" => () => new ViewDefinitionBuilder<Flat>().From<Row>().Select<Row, int>(r => r.X, v => v.Child!.X),
            "nested group key" => () => new ViewDefinitionBuilder<Flat>().From<Row>().GroupBy<Row, int>(r => r.Sub.X),
            "nested join key" => () => new ViewDefinitionBuilder<Flat>().From<Row>().Join<Row, Row, int>(r => r.Sub.X, o => o.X),
            "nested aggregate source" => () => new ViewDefinitionBuilder<Flat>().From<Row>().Sum<Row, int>(r => r.Sub.X, v => v.X),
            "string length" => () => new ViewDefinitionBuilder<Flat>().From<Row>().Select<Row, int>(r => r.Name.Length, v => v.X),
            "collection count" => () => new ViewDefinitionBuilder<Flat>().From<Row>().Select<Row, int>(r => r.Items.Count, v => v.Count),
            "struct member" => () => new ViewDefinitionBuilder<Flat>().From<Row>().Select<Row, int>(r => r.When.Year, v => v.X),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain(expected).And.Contain("TASK-843");
    }

    [Fact]
    public void A_chain_that_does_not_start_at_the_parameter_is_refused()
    {
        var captured = new Sub { X = 3 };
        Action act = () => new ViewDefinitionBuilder<Flat>().From<Row>().Select<Row, int>(r => captured.X, v => v.X);
        act.Should().Throw<ArgumentException>().WithMessage("*member access on its parameter*");
    }
}
