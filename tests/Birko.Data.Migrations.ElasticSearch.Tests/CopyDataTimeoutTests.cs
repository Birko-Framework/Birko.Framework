using System;
using System.Text;
using Birko.Data.Migrations.ElasticSearch.Context;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// SH-H038 (TASK-319) — <c>CopyData</c>'s server-side reindex checked only <c>IsValid</c>, which a reindex that ran
/// out of time passes: HTTP 200, <c>timed_out: true</c>, no failures listed, part of the source copied. The step
/// completed, so the migration would be recorded as applied over a partial copy.
/// </summary>
public class CopyDataTimeoutTests
{
    private static ElasticSearchDataMigrator Migrator(string reindexBody)
    {
        // Every request after the product check gets this body; the reindex is the first one CopyData sends.
        var connection = new InMemoryConnection(Encoding.UTF8.GetBytes(reindexBody));
        var settings = new ConnectionSettings(new SingleNodeConnectionPool(new Uri("http://localhost:9200")), connection);
        return new ElasticSearchDataMigrator(new ElasticClient(settings));
    }

    [Fact]
    public void A_timed_out_copy_fails_the_step()
    {
        var migrator = Migrator(@"{""took"":60000,""timed_out"":true,""total"":3,""created"":1,""updated"":0,""failures"":[]}");

        var act = () => migrator.CopyData("source", "target", null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*timed out after accounting for 1 of 3*");
    }

    [Fact]
    public void A_cancelled_copy_fails_the_step()
    {
        var migrator = Migrator(@"{""took"":900,""timed_out"":false,""total"":3,""created"":1,""updated"":0,""canceled"":""by user request"",""failures"":[]}");

        var act = () => migrator.CopyData("source", "target", null);

        act.Should().Throw<InvalidOperationException>().WithMessage("*stopped after accounting for 1 of 3*");
    }

    [Fact]
    public void A_complete_copy_passes()
    {
        var migrator = Migrator(@"{""took"":12,""timed_out"":false,""total"":3,""created"":3,""updated"":0,""failures"":[]}");

        var act = () => migrator.CopyData("source", "target", null);

        act.Should().NotThrow();
    }
}
