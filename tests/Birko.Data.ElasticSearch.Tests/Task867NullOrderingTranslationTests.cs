using System;
using System.Linq.Expressions;
using Birko.Data.Models;
using FluentAssertions;
using Elasticsearch.Net;
using Nest;
using Xunit;
using Es = Birko.Data.ElasticSearch.ElasticSearch;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-867: an ordering against null is false in C#. The translator refused it with <c>NotSupportedException</c>; it is
/// now folded to that constant before translation, so it renders as match_none / match_all or drops out of an AND / OR.
/// The live behaviour is pinned by <c>NullOrderingScopeContract</c>; this pins the rendering, with no server.
/// </summary>
public class Task867NullOrderingTranslationTests
{
    public class Item : AbstractModel
    {
        public int A { get; set; }
        public int? Score { get; set; }
    }

    private static readonly int? Nul = null;

    [Fact]
    public void A_bare_null_ordering_matches_nothing()
        => Es.ParseFilterQuery<Item>(x => x.Score > Nul).Should().BeOfType<MatchNoneQuery>();

    [Fact]
    public void A_negated_null_ordering_matches_everything()
        => Es.ParseFilterQuery<Item>(x => !(Nul <= x.Score)).Should().BeOfType<MatchAllQuery>();

    [Theory]
    [MemberData(nameof(Reduced))]
    public void A_null_ordering_inside_a_group_drops_out_as_CSharp_has_it(Expression<Func<Item, bool>> filter, Expression<Func<Item, bool>> same)
    {
        var folded = Es.ParseFilterQuery(filter);
        var plain = Es.ParseFilterQuery(same);

        folded.Should().NotBeNull();
        var serializer = new ElasticClient().RequestResponseSerializer;
        serializer.SerializeToString(folded).Should().Be(serializer.SerializeToString(plain));
    }

    public static TheoryData<Expression<Func<Item, bool>>, Expression<Func<Item, bool>>> Reduced => new()
    {
        { x => x.Score > Nul || x.A > 4, x => x.A > 4 },
        { x => !(x.Score > Nul) && x.A > 4, x => x.A > 4 },
        { x => !(x.Score > Nul || x.A > 3), x => !(x.A > 3) },
    };
}
