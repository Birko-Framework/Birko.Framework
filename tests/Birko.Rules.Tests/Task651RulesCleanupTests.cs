using System;
using System.Collections.Generic;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>TASK-651: three small rule-model contracts the regen found broken.</summary>
public class Task651RulesCleanupTests
{
    [Fact]
    public void Assigning_RuleSet_Rules_copies_the_list_as_documented()
    {
        var mine = new RuleList();
        var set = new RuleSet("s") { Rules = mine };

        mine.Add(new Rule("A", ComparisonOperator.Equal, 1));

        set.Rules.Should().BeEmpty("the set holds a copy, so the caller's later edits do not reach it");
    }

    [Fact]
    public void A_RuleGroup_refuses_a_null_rule_list()
    {
        var group = RuleGroup.And();

        var act = () => group.Rules = null!;

        act.Should().Throw<ArgumentNullException>();
        group.Rules.Should().NotBeNull();
    }

    [Fact]
    public void A_field_that_matches_two_keys_ignoring_case_is_refused()
    {
        var context = new DictionaryRuleContext(new Dictionary<string, object?> { ["Name"] = 1, ["name"] = 2 });

        context.TryGetValue("name", out var exact).Should().BeTrue();
        exact.Should().Be(2, "an exact key still wins");
        context.Invoking(c => c.TryGetValue("NAME", out _)).Should().Throw<InvalidOperationException>().WithMessage("*'Name'*'name'*");
    }
}
