using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-498 — the painless script a <see cref="PropertyUpdate{T}"/> renders to: <c>=</c> for a Set, <c>+=</c> for an
/// Increment (a Decrement is a negative <c>+=</c>). The live round trip is in <c>NullSetLiveTests</c>.
/// </summary>
public class BuildUpdateScriptTests
{
    private sealed class Doc : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
        public int? Score { get; set; }

        [Nest.Keyword(Name = "ownerRef")]
        public string? Owner { get; set; }

#pragma warning disable CS0649 // only ever selected, never assigned
        public string? Field;
#pragma warning restore CS0649
    }

    [Fact]
    public void Set_Assigns_And_Increment_Adds()
    {
        var (script, parameters) = ElasticSearchStoreHelper.BuildUpdateScript(
            new PropertyUpdate<Doc>().Set(x => x.Name, "a").Increment(x => x.Hits, 1));

        script.Should().Be("ctx._source.name = params.p_Name; ctx._source.hits += params.p_Hits");
        parameters["p_Name"].Should().Be("a");
        parameters["p_Hits"].Should().Be(1);
    }

    [Fact]
    public void Decrement_Adds_The_Negative_Delta()
    {
        var (script, parameters) = ElasticSearchStoreHelper.BuildUpdateScript(new PropertyUpdate<Doc>().Decrement(x => x.Hits, 2));

        script.Should().Be("ctx._source.hits += params.p_Hits");
        parameters["p_Hits"].Should().Be(-2);
    }

    /// <summary>
    /// TASK-501. A null Set was bound as <c>value ?? ""</c>, so a string stored <c>""</c> and a nullable number stored a
    /// string. It is now assigned as a literal null, with no parameter.
    /// </summary>
    [Fact]
    public void A_null_Set_assigns_null_never_an_empty_string()
    {
        var (script, parameters) = ElasticSearchStoreHelper.BuildUpdateScript(
            new PropertyUpdate<Doc>().Set(x => x.Name, null).Set(x => x.Score, null));

        script.Should().Be("ctx._source.name = null; ctx._source.score = null");
        parameters.Should().BeEmpty();
    }

    /// <summary>The field is the one the document is stored under (the TASK-613 producer), not plain camelCase.</summary>
    [Fact]
    public void A_mapped_member_is_written_under_its_declared_name()
        => ElasticSearchStoreHelper.BuildUpdateScript(new PropertyUpdate<Doc>().Set(x => x.Owner, "o")).Script
            .Should().Be("ctx._source.ownerRef = params.p_Owner");

    /// <summary>
    /// TASK-501. A selector that is not a property is refused like <c>ApplyTo</c> and the MongoDB translator do. It threw
    /// <c>InvalidCastException</c> for a non-member, and wrote a public field as if it were a property.
    /// </summary>
    [Fact]
    public void A_selector_that_is_not_a_property_is_refused()
    {
        ((System.Action)(() => ElasticSearchStoreHelper.BuildUpdateScript(new PropertyUpdate<Doc>().Set(x => x.Name + "!", "a"))))
            .Should().Throw<System.ArgumentException>();
        ((System.Action)(() => ElasticSearchStoreHelper.BuildUpdateScript(new PropertyUpdate<Doc>().Set(x => x.Field, "a"))))
            .Should().Throw<System.ArgumentException>();
    }
}
