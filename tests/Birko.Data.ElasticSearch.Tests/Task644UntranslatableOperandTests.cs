using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using FluentAssertions;
using Nest;
using Xunit;
using Es = Birko.Data.ElasticSearch.ElasticSearch;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-644: a field compared with an operand that cannot be translated was read as a comparison with null, so
/// <c>x.Text == x.Text.Trim()</c> ran as "Text is missing". Only a parameter-free operand that evaluates to null is a null
/// comparison; anything else is refused (rules 42, 44).
/// </summary>
public class Task644UntranslatableOperandTests
{
    public class Owner
    {
        public string? Name { get; set; }
    }

    public class Item : AbstractModel
    {
        public string? Text { get; set; }
        public Owner? Owner { get; set; }
    }

    [Theory]
    [MemberData(nameof(Untranslatable))]
    public void A_field_compared_with_an_untranslatable_operand_is_refused(Expression<Func<Item, bool>> filter)
    {
        var act = () => Es.ParseFilterQuery(filter);

        act.Should().Throw<NotSupportedException>();
    }

    public static TheoryData<Expression<Func<Item, bool>>> Untranslatable => new()
    {
        x => x.Text == x.Text!.Trim(),
        x => x.Text != x.Text!.Trim(),
        x => x.Text!.Trim() == x.Text,
        x => x.Owner!.Name == x.Text,
        x => x.Text == x.Owner!.Name,
    };

    [Fact]
    public void A_comparison_with_null_is_still_an_existence_test()
    {
        string? none = null;

        Es.ParseFilterQuery<Item>(x => x.Text == null).Should().BeOfType<BoolQuery>().Which.MustNot.Should().ContainSingle();
        Es.ParseFilterQuery<Item>(x => null == x.Text).Should().BeOfType<BoolQuery>();
        Es.ParseFilterQuery<Item>(x => x.Text == none).Should().BeOfType<BoolQuery>("a closure that holds null is a null comparison");
        Es.ParseFilterQuery<Item>(x => x.Text != null).Should().BeOfType<ExistsQuery>();
    }
}
