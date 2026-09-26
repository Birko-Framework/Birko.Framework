using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-498 — the painless script a <see cref="PropertyUpdate{T}"/> renders to: <c>=</c> for a Set, <c>+=</c> for an
/// Increment (a Decrement is a negative <c>+=</c>). This project has no Elasticsearch live suite, so the script
/// text is the whole of the coverage here.
/// </summary>
public class BuildUpdateScriptTests
{
    private sealed class Doc : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
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
}
