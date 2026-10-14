using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// A model whose string members are mapped by NEST attributes rather than dynamically: AutoMap makes a
/// <c>[Keyword]</c> member a plain <c>keyword</c> field with NO <c>.keyword</c> sub-field, and stores it under the
/// attribute's <c>Name</c>.
/// </summary>
public class MappedNamesModel : AbstractModel
{
    [Keyword(Name = "code")]
    public string Code { get; set; } = string.Empty;

    [Keyword(Name = "ownerRef")]
    public string Owner { get; set; } = string.Empty;

    public string? Name { get; set; }

    [Text(Name = "notes", Index = false)]
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// TASK-613 — the filter translator names the field the document is actually stored under. It appended
/// <c>.keyword</c> to every string member, so an equality on a <c>[Keyword]</c> member queried a field that does
/// not exist and matched nothing, without an error (the workflow, background-job and sync ES models).
/// </summary>
public class MappedFieldNameTests
{
    private static QueryBase? Parse(Expression<Func<MappedNamesModel, bool>> expr)
        => Birko.Data.ElasticSearch.ElasticSearch.ParseFilterQuery(expr);

    [Fact]
    public void A_keyword_mapped_member_is_queried_as_itself_not_its_keyword_subfield()
        => Parse(x => x.Code == "A").Should().BeOfType<TermQuery>().Which.Field.Name.Should().Be("code");

    [Fact]
    public void A_mapped_member_is_queried_under_the_name_its_attribute_declares()
        => Parse(x => x.Owner == "o").Should().BeOfType<TermQuery>().Which.Field.Name.Should().Be("ownerRef");

    [Fact]
    public void A_dynamically_mapped_string_still_uses_its_keyword_subfield()
        => Parse(x => x.Name == "n").Should().BeOfType<TermQuery>().Which.Field.Name.Should().Be("name.keyword");

    [Fact]
    public void A_substring_match_on_a_keyword_mapped_member_targets_the_keyword_field()
        => Parse(x => x.Code.Contains("A")).Should().BeOfType<WildcardQuery>().Which.Field.Name.Should().Be("code");

    /// <summary>
    /// A <c>[Text]</c> member has no exact-match form: the analysed field matches tokens, and there is no
    /// <c>.keyword</c> sub-field to fall back on. Refused rather than sent to a field that does not exist (rule 41/42).
    /// </summary>
    [Fact]
    public void An_exact_match_on_a_text_mapped_member_is_refused()
        => ((Action)(() => Parse(x => x.Notes == "n"))).Should().Throw<NotSupportedException>().WithMessage("*Notes*");
}

/// <summary>TASK-613, live: the same filters find the rows a store wrote.</summary>
public sealed class MappedFieldNameLiveTests : IDisposable
{
    private readonly ElasticLive _live;
    public MappedFieldNameLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    [Fact]
    public void Filters_on_keyword_mapped_and_dynamic_members_find_the_stored_rows()
    {
        if (!_live.Ready()) return;
        var store = new ElasticSearchStore<MappedNamesModel>();
        store.SetSettings(_live.NewSettings());

        store.Create(new MappedNamesModel { Code = "A-1", Owner = "o1", Name = "first" });
        store.Create(new MappedNamesModel { Code = "B-2", Owner = "o2", Name = "second" });
        ElasticLive.Refresh();

        store.Read(x => x.Code == "A-1").Select(x => x.Name).Should().Equal("first");
        store.Read(x => x.Owner == "o2").Select(x => x.Name).Should().Equal("second");
        store.Read(x => x.Name == "second").Select(x => x.Code).Should().Equal("B-2");
        store.Read(x => x.Code.Contains("-2")).Select(x => x.Name).Should().Equal("second");
    }
}
