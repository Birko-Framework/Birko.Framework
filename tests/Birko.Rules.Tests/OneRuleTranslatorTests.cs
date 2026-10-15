using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Patterns.Specification;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// TASK-144: one rule-to-LINQ translator. <c>RuleSpecification.ToExpression</c> now calls <see cref="RuleExpressionConverter"/>,
/// which took the specification's safety fixes (it had none of them) and kept its own nested fields (which the
/// specification lacked). SH-M297 brings the in-memory evaluator onto the same answer for a field a typed entity cannot
/// reach. Every assertion runs the compiled predicate over real objects.
/// </summary>
public class OneRuleTranslatorTests
{
    public enum Kind { A = 0, B = 1 }

    public class Address
    {
        public string? City { get; set; }
    }

    public class Item
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int Qty { get; set; }
        public Kind Kind { get; set; }
        public Address? Address { get; set; }
    }

    private static readonly Guid FirstId = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Item[] Items =
    {
        new() { Id = FirstId, Name = "Widget", Qty = 10, Kind = Kind.A, Address = new Address { City = "Brno" } },
        new() { Id = Guid.NewGuid(), Name = "Gadget", Qty = 11, Kind = Kind.B, Address = null },
        new() { Id = Guid.NewGuid(), Name = null, Qty = 3, Kind = Kind.B, Address = new Address { City = null } },
    };

    private static List<Item> Converter(IRule rule) => Items.Where(RuleExpressionConverter.ToExpression<Item>(rule).Compile()).ToList();

    private static List<Item> Specification(IRule rule) => Items.Where(new RuleSpecification<Item>(rule).ToExpression().Compile()).ToList();

    private static List<Item> InMemory(IRule rule) => Items.Where(new RuleSpecification<Item>(rule).IsSatisfiedBy).ToList();

    // ---------------- the converter took the specification's fixes ----------------

    [Fact]
    public void A_null_needle_matches_nothing_rather_than_every_row()
    {
        Converter(new Rule("Name", ComparisonOperator.Contains, null)).Should().BeEmpty();
        Converter(new Rule("Name", ComparisonOperator.NotContains, null)).Should().BeEmpty("a degraded leaf is never negated");
    }

    [Fact]
    public void An_interior_wildcard_matches_nothing_rather_than_the_literal()
    {
        Converter(new Rule("Name", ComparisonOperator.Like, "W%t")).Should().BeEmpty();
        Converter(new Rule("Name", ComparisonOperator.Like, "W%t") { IsNegated = true }).Should().BeEmpty();
    }

    [Fact]
    public void A_guid_given_as_text_is_converted_not_thrown()
    {
        Converter(new Rule("Id", ComparisonOperator.Equal, FirstId.ToString())).Should().ContainSingle().Which.Id.Should().Be(FirstId);
    }

    [Fact]
    public void Ordering_strings_has_no_store_form_and_matches_nothing_rather_than_throwing()
    {
        var act = () => Converter(new Rule("Name", ComparisonOperator.GreaterThan, "A"));

        act.Should().NotThrow();
        Converter(new Rule("Name", ComparisonOperator.GreaterThan, "A")).Should().BeEmpty();
    }

    [Fact]
    public void A_fractional_value_against_an_int_is_not_rounded()
    {
        Converter(new Rule("Qty", ComparisonOperator.Equal, 10.7)).Should().BeEmpty("10.7 is not 11");
    }

    [Fact]
    public void A_string_operator_on_a_number_matches_nothing_negated_or_not()
    {
        Converter(new Rule("Qty", ComparisonOperator.NotContains, "1")).Should().BeEmpty();
    }

    [Fact]
    public void A_scalar_In_is_one_candidate_as_the_evaluator_reads_it()
    {
        var rule = new Rule("Kind", ComparisonOperator.In, Kind.A);

        Converter(rule).Should().ContainSingle().Which.Name.Should().Be("Widget");
        InMemory(rule).Should().ContainSingle().Which.Name.Should().Be("Widget");
    }

    // ---------------- the specification took the converter's nested fields ----------------

    [Fact]
    public void A_nested_field_is_translated_with_null_guards()
    {
        Specification(new Rule("Address.City", ComparisonOperator.Equal, "Brno")).Should().ContainSingle().Which.Name.Should().Be("Widget");
        Specification(new Rule("Address.City", ComparisonOperator.Equal, "Brno") { IsNegated = true })
            .Should().ContainSingle("a null Address never matches, negated or not; a null City is a value, which is not Brno");
    }

    // ---------------- SH-M297: a field a typed entity cannot reach ----------------

    [Fact]
    public void A_misspelled_field_matches_nothing_in_memory_as_at_the_store()
    {
        var rule = new Rule("Nmae", ComparisonOperator.IsNull, null);

        InMemory(rule).Should().BeEmpty();
        Converter(rule).Should().BeEmpty();
        InMemory(new Rule("Nmae", ComparisonOperator.IsNull, null) { IsNegated = true }).Should().BeEmpty();
    }

    [Fact]
    public void A_null_intermediate_matches_nothing_in_memory_as_at_the_store()
    {
        var rule = new Rule("Address.City", ComparisonOperator.IsNull, null);

        InMemory(rule).Select(i => i.Qty).Should().Equal(3);
        Converter(rule).Select(i => i.Qty).Should().Equal(3);
    }

    [Fact]
    public void A_dictionary_context_still_reads_a_missing_key_as_null()
    {
        var context = new DictionaryRuleContext(new Dictionary<string, object?> { ["Other"] = 1 });

        new RuleEvaluator().Evaluate(new Rule("Missing", ComparisonOperator.IsNull, null), context).IsMatch.Should().BeTrue();
    }

    // ---------------- one translator, by construction ----------------

    public static TheoryData<IRule> Rules => new()
    {
        new Rule("Name", ComparisonOperator.Equal, "Widget"),
        new Rule("Qty", ComparisonOperator.Between, 2) { UpperValue = 10 },
        new Rule("Name", ComparisonOperator.Like, "%dg%"),
        new Rule("Kind", ComparisonOperator.NotIn, new[] { "B" }),
        new Rule("Address.City", ComparisonOperator.StartsWith, "Br"),
        new RuleGroup(LogicOperator.Or, new List<IRule>
        {
            new Rule("Qty", ComparisonOperator.LessThan, 5),
            new Rule("Name", ComparisonOperator.Contains, "W") { IsNegated = true },
        }) { IsNegated = true },
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Both_entry_points_produce_the_same_expression(IRule rule)
    {
        new RuleSpecification<Item>(rule).ToExpression().ToString()
            .Should().Be(RuleExpressionConverter.ToExpression<Item>(rule).ToString());
    }
}
