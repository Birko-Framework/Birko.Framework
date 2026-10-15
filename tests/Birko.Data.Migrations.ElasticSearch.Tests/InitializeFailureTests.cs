using System;
using Birko.Data.Migrations.ElasticSearch;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// SH-M202 (TASK-152): Initialize read a failed existence check as "the index is absent" and did not check the create it
/// led to, so an unreachable or unauthorized cluster initialised "successfully". No server: the probe is refused.
/// </summary>
public class InitializeFailureTests
{
    [Fact]
    public void An_unreachable_cluster_fails_initialisation()
    {
        var store = new ElasticSearchMigrationStore(new ElasticClient(new ConnectionSettings(new Uri("http://localhost:59991"))
            .RequestTimeout(TimeSpan.FromSeconds(2))));

        store.Invoking(s => s.Initialize()).Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("existence check failed");
    }
}
