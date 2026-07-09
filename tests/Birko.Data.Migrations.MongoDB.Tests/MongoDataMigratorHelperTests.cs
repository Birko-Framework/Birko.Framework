using System;
using Birko.Data.Migrations.MongoDB.Context;
using FluentAssertions;
using MongoDB.Bson;
using Xunit;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// CR-M112: CopyData ignored transformJson (hard-coded to a single $merge stage). BuildCopyPipeline
/// now prepends the caller's transform stage(s) before $merge. Also covers ParseFilter's empty/JSON
/// handling (CR-M113 — the data migrator's pure logic had no coverage). Live copy/CRUD stays an
/// integration-tier concern.
/// </summary>
public class MongoDataMigratorHelperTests
{
    [Fact]
    public void BuildCopyPipeline_no_transform_is_just_a_merge()
    {
        foreach (var t in new[] { (string?)null, "", "   ", "{}" })
        {
            var pipeline = MongoDataMigrator.BuildCopyPipeline("target", t);

            pipeline.Should().ContainSingle();
            pipeline[0].Contains("$merge").Should().BeTrue();
            pipeline[0]["$merge"]["into"].AsString.Should().Be("target");
        }
    }

    [Fact]
    public void BuildCopyPipeline_single_stage_is_prepended_before_merge()
    {
        var pipeline = MongoDataMigrator.BuildCopyPipeline("target", "{\"$addFields\": {\"x\": 1}}");

        pipeline.Should().HaveCount(2);
        pipeline[0].Contains("$addFields").Should().BeTrue("the transform runs before the merge");
        pipeline[1].Contains("$merge").Should().BeTrue();
    }

    [Fact]
    public void BuildCopyPipeline_array_of_stages_is_prepended_in_order()
    {
        var pipeline = MongoDataMigrator.BuildCopyPipeline(
            "target", "[{\"$match\": {\"a\": 1}}, {\"$addFields\": {\"x\": 1}}]");

        pipeline.Should().HaveCount(3);
        pipeline[0].Contains("$match").Should().BeTrue();
        pipeline[1].Contains("$addFields").Should().BeTrue();
        pipeline[2].Contains("$merge").Should().BeTrue();
    }

    [Fact]
    public void ParseFilter_empty_or_object_is_not_null_and_does_not_throw()
    {
        foreach (var f in new[] { (string?)null, "", "{}" })
        {
            Action act = () => MongoDataMigrator.ParseFilter(f);
            act.Should().NotThrow();
            MongoDataMigrator.ParseFilter(f).Should().NotBeNull();
        }
    }

    [Fact]
    public void ParseFilter_valid_json_parses_invalid_throws()
    {
        MongoDataMigrator.ParseFilter("{\"status\":\"active\"}").Should().NotBeNull();
        Action act = () => MongoDataMigrator.ParseFilter("not json");
        act.Should().Throw<Exception>();
    }
}
