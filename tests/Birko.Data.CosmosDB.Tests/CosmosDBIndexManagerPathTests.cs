using System.Collections.ObjectModel;
using System.Linq;
using Birko.Data.CosmosDB.IndexManagement;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// CR-M085: CreateAsync stored single-field indexes as the normalized path "/Name/?", but
/// ExistsAsync/DropAsync/GetInfoAsync compared against the raw name, so an index created by name
/// was never found — and DropAsync unconditionally pushed the raw name into ExcludedPaths even when
/// nothing was removed. These test the extracted normalization/matching helpers directly.
/// </summary>
public class CosmosDBIndexManagerPathTests
{
    [Theory]
    [InlineData("Foo", "/Foo/?")]
    [InlineData("/Foo", "/Foo/?")]
    [InlineData("/Foo/?", "/Foo/?")]
    public void NormalizeIncludedPath_matches_the_form_CreateAsync_persists(string input, string expected)
    {
        CosmosDBIndexManager.NormalizeIncludedPath(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Foo", "/Foo")]
    [InlineData("/Foo", "/Foo")]
    public void NormalizeFieldPath_prefixes_a_slash(string input, string expected)
    {
        CosmosDBIndexManager.NormalizeFieldPath(input).Should().Be(expected);
    }

    [Fact]
    public void PolicyContainsIndex_finds_a_single_field_index_created_by_name()
    {
        var policy = new IndexingPolicy();
        policy.IncludedPaths.Add(new IncludedPath { Path = "/Category/?" });

        CosmosDBIndexManager.PolicyContainsIndex(policy, "Category").Should().BeTrue("normalized match");
        CosmosDBIndexManager.PolicyContainsIndex(policy, "/Category/?").Should().BeTrue("raw match");
        CosmosDBIndexManager.PolicyContainsIndex(policy, "Missing").Should().BeFalse();
    }

    [Fact]
    public void PolicyContainsIndex_finds_a_composite_index_element_by_name()
    {
        var policy = new IndexingPolicy();
        policy.CompositeIndexes.Add(new Collection<CompositePath>
        {
            new CompositePath { Path = "/A" },
            new CompositePath { Path = "/B" },
        });

        CosmosDBIndexManager.PolicyContainsIndex(policy, "A").Should().BeTrue();
        CosmosDBIndexManager.PolicyContainsIndex(policy, "/B").Should().BeTrue();
        CosmosDBIndexManager.PolicyContainsIndex(policy, "C").Should().BeFalse();
    }

    [Fact]
    public void RemoveIncludedIndex_removes_the_normalized_path_and_mirrors_it_to_excluded()
    {
        var policy = new IndexingPolicy();
        policy.IncludedPaths.Add(new IncludedPath { Path = "/Category/?" });

        var removed = CosmosDBIndexManager.RemoveIncludedIndex(policy, "Category");

        removed.Should().Be("/Category/?");
        policy.IncludedPaths.Should().NotContain(p => p.Path == "/Category/?");
        policy.ExcludedPaths.Should().ContainSingle().Which.Path.Should().Be("/Category/?");
    }

    [Fact]
    public void RemoveIncludedIndex_no_match_removes_nothing_and_excludes_nothing()
    {
        // CR-M085 regression: the old code added ExcludedPath { Path = indexName } unconditionally.
        var policy = new IndexingPolicy();
        policy.IncludedPaths.Add(new IncludedPath { Path = "/Category/?" });

        var removed = CosmosDBIndexManager.RemoveIncludedIndex(policy, "DoesNotExist");

        removed.Should().BeNull();
        policy.IncludedPaths.Should().ContainSingle().Which.Path.Should().Be("/Category/?");
        policy.ExcludedPaths.Should().BeEmpty("nothing was removed, so nothing must be excluded");
    }
}
