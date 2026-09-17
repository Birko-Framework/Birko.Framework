using System;
using System.Collections.Generic;
using Birko.Data.Exceptions;
using Birko.Data.Migrations.ElasticSearch.Context;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// <b>SH-H032 (TASK-314).</b> ElasticSearch reaches the same end state as the SQL backends by a different
/// route: <c>ParseFilter</c> returned <c>BoolQuery { Must = mustClauses }</c>, and an <b>empty</b>
/// <c>must</c> is match-all in Elasticsearch. So <c>{"status":{}}</c> — the object branch with an operator
/// loop that adds nothing — produced a query indistinguishable from <c>match_all</c>, and
/// <c>DeleteByQuery</c> emptied the whole index.
///
/// <para>
/// That is worth stating because the obvious guard does not catch it: the query object is non-null and
/// well-formed, exactly as § TASK-308 records for the <c>$nin: []</c> shape. The discriminator has to be
/// how many terms the translator actually produced.
/// </para>
/// <para>
/// No live cluster is needed. All three operations now resolve and guard the query before touching the
/// client, so the refusal is reachable offline (§ TASK-309: try rendering a sink before accepting it is
/// untestable).
/// </para>
/// </summary>
public class DegradedFilterRefusalTests
{
    /// <summary>
    /// A client pointed at a closed port: reaching it at all would fail loudly rather than pass for the
    /// wrong reason. Retries are off and the timeout is short, because the contract pins below deliberately
    /// DO reach the network — that is how they prove the guard let them through — and NEST's defaults make
    /// each of those take seconds.
    /// </summary>
    private static ElasticSearchDataMigrator Migrator()
        => new(new ElasticClient(new ConnectionSettings(new Uri("http://localhost:59998"))
            .MaximumRetries(0)
            .RequestTimeout(TimeSpan.FromMilliseconds(250))
            .DisableAutomaticProxyDetection()
            .ThrowExceptions(false)));

    [Fact]
    public void A_delete_whose_filter_constrains_nothing_is_refused_before_any_request()
    {
        Action act = () => Migrator().DeleteDocuments("widgets", "{\"status\":{}}");

        var ex = act.Should().Throw<WholeTableWriteException>().Which;
        ex.Operation.Should().Be("delete");
        ex.TableName.Should().Be("widgets");
        ex.Message.Should().Contain("every document in the index");
    }

    [Fact]
    public void An_update_whose_filter_constrains_nothing_is_refused_before_any_request()
    {
        Action act = () => Migrator().UpdateDocuments(
            "widgets", "{\"status\":{}}", new Dictionary<string, object> { ["Name"] = "x" });

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("update");
    }

    /// <summary>Guard the whole verb family (§ TASK-215), so a count agrees with the delete.</summary>
    [Fact]
    public void A_count_whose_filter_constrains_nothing_is_refused_before_any_request()
    {
        Action act = () => Migrator().CountDocuments("widgets", "{\"status\":{}}");

        act.Should().Throw<WholeTableWriteException>()
            .Which.Operation.Should().Be("count");
    }

    // ---------------------------------------------------------------- contract pins
    // Without these, "refuse every filter" passes everything above and looks like a valid fix.

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    public void The_explicit_match_all_door_is_not_refused(string? filterJson)
    {
        Action act = () => Migrator().DeleteDocuments("widgets", filterJson!);

        act.Should().NotThrow<WholeTableWriteException>();
    }

    [Fact]
    public void An_ordinary_filter_is_not_refused()
    {
        Action act = () => Migrator().DeleteDocuments("widgets", "{\"status\":\"archived\"}");

        act.Should().NotThrow<WholeTableWriteException>();
    }

    /// <summary>
    /// An operator filter still produces a term. This is the shape closest to the defect — the object
    /// branch is taken — so it is the one that proves the guard discriminates on the term count rather
    /// than simply refusing every nested object.
    /// </summary>
    [Fact]
    public void An_operator_filter_is_not_refused()
    {
        Action act = () => Migrator().DeleteDocuments("widgets", "{\"age\":{\"$gt\":18}}");

        act.Should().NotThrow<WholeTableWriteException>();
    }
}
