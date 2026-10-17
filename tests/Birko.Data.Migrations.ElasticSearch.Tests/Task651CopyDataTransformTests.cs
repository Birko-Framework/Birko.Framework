using System;
using Birko.Data.Migrations.ElasticSearch.Context;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>TASK-651: "{}" is no transform on ElasticSearch too, as MigrationFilter.RefuseTransform reads it everywhere else.</summary>
public class Task651CopyDataTransformTests
{
    private static ElasticSearchDataMigrator Unreachable()
        => new(new ElasticClient(new ConnectionSettings(new Uri("http://127.0.0.1:1")).RequestTimeout(TimeSpan.FromSeconds(2))));

    [Fact]
    public void An_empty_transform_is_a_plain_copy_not_a_refusal()
    {
        var act = () => Unreachable().CopyData("source", "target", "{}");

        // No server: the copy itself fails, but not with the transform refusal.
        act.Should().Throw<Exception>().Which.Should().NotBeOfType<NotSupportedException>();
    }

    [Fact]
    public void A_real_transform_is_still_refused()
        => Unreachable().Invoking(m => m.CopyData("source", "target", "{\"x\":1}")).Should().Throw<NotSupportedException>();
}
