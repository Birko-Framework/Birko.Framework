using System;
using Birko.Data.Patterns.Specification;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-L158: the compiled-expression path (ToExpression().Compile()) must not throw when run in-memory
/// over null string members or with non-convertible rule values — such leaves degrade to "no match"
/// (Expression.Constant(false)) instead of NullReferenceException / InvalidCastException.
/// </summary>
public class RuleSpecificationExpressionTests
{
    private class Widget
    {
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public int? Optional { get; set; }
        public Status State { get; set; }
    }

    private enum Status { Draft, Active }

    private static Func<Widget, bool> Compile(Rule rule)
        => new RuleSpecification<Widget>(rule).ToExpression().Compile();

    [Fact]
    public void Compiled_Contains_over_null_string_does_not_throw_and_is_false()
    {
        var predicate = Compile(new Rule(nameof(Widget.Name), ComparisonOperator.Contains, "abc"));

        FluentActions.Invoking(() => predicate(new Widget { Name = null })).Should().NotThrow();
        predicate(new Widget { Name = null }).Should().BeFalse();
        predicate(new Widget { Name = "xx-ABC-xx" }).Should().BeTrue(); // case-insensitive
    }

    [Fact]
    public void Compiled_NotContains_over_null_string_is_true()
    {
        // A null field "does not contain" the needle.
        var predicate = Compile(new Rule(nameof(Widget.Name), ComparisonOperator.NotContains, "abc"));

        predicate(new Widget { Name = null }).Should().BeTrue();
        predicate(new Widget { Name = "has abc" }).Should().BeFalse();
    }

    [Fact]
    public void Compiled_comparison_with_non_convertible_value_is_unsatisfiable_not_a_throw()
    {
        var predicate = Compile(new Rule(nameof(Widget.Quantity), ComparisonOperator.Equal, "not-a-number"));

        FluentActions.Invoking(() => predicate(new Widget { Quantity = 5 })).Should().NotThrow();
        predicate(new Widget { Quantity = 5 }).Should().BeFalse();
    }

    [Fact]
    public void Compiled_comparison_null_against_non_nullable_value_type_is_false()
    {
        var predicate = Compile(new Rule(nameof(Widget.Quantity), ComparisonOperator.Equal, null));

        FluentActions.Invoking(() => predicate(new Widget { Quantity = 0 })).Should().NotThrow();
        predicate(new Widget { Quantity = 0 }).Should().BeFalse();
    }

    [Fact]
    public void Compiled_comparison_with_convertible_value_still_matches()
    {
        var predicate = Compile(new Rule(nameof(Widget.Quantity), ComparisonOperator.GreaterThan, "3"));

        predicate(new Widget { Quantity = 5 }).Should().BeTrue();
        predicate(new Widget { Quantity = 2 }).Should().BeFalse();
    }

    [Fact]
    public void Compiled_between_with_valid_bounds_works()
    {
        var predicate = new RuleSpecification<Widget>(
                new Rule(nameof(Widget.Quantity), ComparisonOperator.Between, "2") { UpperValue = "6" })
            .ToExpression().Compile();

        predicate(new Widget { Quantity = 4 }).Should().BeTrue();
        predicate(new Widget { Quantity = 1 }).Should().BeFalse();
        predicate(new Widget { Quantity = 7 }).Should().BeFalse();
    }

    [Fact]
    public void Compiled_comparison_value_already_of_member_type_matches()
    {
        // The enum value arrives already typed — TryConvertConstant must accept it directly
        // (Convert.ChangeType cannot target an enum).
        var predicate = Compile(new Rule(nameof(Widget.State), ComparisonOperator.Equal, Status.Active));

        predicate(new Widget { State = Status.Active }).Should().BeTrue();
        predicate(new Widget { State = Status.Draft }).Should().BeFalse();
    }
}
