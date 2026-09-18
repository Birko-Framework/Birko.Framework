using System.Collections.Generic;
using Birko.Data.Migrations.ElasticSearch.Context;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// Regression for CR-H058: UpdateDocuments used to interpolate values into the Painless source
/// (unescaped string quotes broke/injected the script; bool/DateTime/decimal rendered via ToString()
/// into invalid literals). It now references values as script params, verified here.
/// </summary>
public class PainlessSourceTests
{
    [Fact]
    public void BuildPainlessSource_ReferencesParams_NotInlineValues()
    {
        var updates = new Dictionary<string, object>
        {
            ["status"] = "O'Brien's \"active\"",   // quotes/backslash would break an inline script
            ["count"] = 42,
            ["enabled"] = true
        };

        var source = ElasticSearchDataMigrator.BuildPainlessSource(updates, out var scriptParams);

        source.Should().Be("ctx._source.status = params.p0; ctx._source.count = params.p1; ctx._source.enabled = params.p2");
        // No raw value is baked into the script text.
        source.Should().NotContain("O'Brien");
        source.Should().NotContain("42");
        source.Should().NotContain("true");

        // The actual (un-mangled) values ride along as params for Nest to serialize.
        scriptParams["p0"].Should().Be("O'Brien's \"active\"");
        scriptParams["p1"].Should().Be(42);
        scriptParams["p2"].Should().Be(true);
    }

    [Fact]
    public void BuildPainlessSource_Empty_ProducesEmptySourceAndNoParams()
    {
        var source = ElasticSearchDataMigrator.BuildPainlessSource(new Dictionary<string, object>(), out var scriptParams);

        source.Should().BeEmpty();
        scriptParams.Should().BeEmpty();
    }
}
