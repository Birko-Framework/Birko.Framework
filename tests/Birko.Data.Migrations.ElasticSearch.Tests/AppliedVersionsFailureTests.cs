using System;
using Birko.Data.Migrations.ElasticSearch;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.Migrations.ElasticSearch.Tests;

/// <summary>
/// <b>SH-H029 (TASK-314).</b> <see cref="ElasticSearchMigrationStore.GetAppliedVersions"/> answered with an
/// <b>empty set</b> when the read <i>failed</i>, which is not the same claim as "nothing has been applied".
/// <c>GetCurrentVersion()</c> then reports 0 and <c>Migrate()</c> re-executes every registered migration —
/// including any destructive <c>Up</c> — against an already-migrated cluster.
///
/// <para>
/// <b>Two gates, and the filed finding named only one.</b> The search's <c>if (!IsValid) return new
/// HashSet&lt;long&gt;()</c> was reported. The <c>Indices.Exists</c> check one line above it has the
/// identical defect and fires <i>first</i>: NEST's <c>ExistsResponse.Exists</c> is
/// <c>HttpStatusCode == 200</c>, so an unreachable or unauthorized cluster answers "the index is not
/// there". Fixing only the reported gate would have left the method returning an empty set for exactly
/// the same reason.
/// </para>
/// <para>
/// <b>Why this is offline-testable.</b> A closed port produces a genuinely invalid response with no
/// status code, which is the failure this is about — so the condition needs no live cluster, only an
/// unreachable one (§ TASK-309).
/// </para>
/// </summary>
public class AppliedVersionsFailureTests
{
    private static ElasticSearchMigrationStore UnreachableStore()
        => new(new ElasticClient(new ConnectionSettings(new Uri("http://localhost:59997"))
            .MaximumRetries(0)
            .RequestTimeout(TimeSpan.FromMilliseconds(250))
            .DisableAutomaticProxyDetection()
            .ThrowExceptions(false)));

    [Fact]
    public void A_failed_read_throws_instead_of_reporting_that_nothing_is_applied()
    {
        Action act = () => UnreachableStore().GetAppliedVersions();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*applied migration versions*");
    }

    /// <summary>
    /// The refusal has to say why an empty set would have been wrong, or a reader hitting it concludes the
    /// cluster is simply new and works around the guard.
    /// </summary>
    [Fact]
    public void The_refusal_explains_what_an_empty_set_would_have_caused()
    {
        Action act = () => UnreachableStore().GetAppliedVersions();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*replay every*");
    }

    /// <summary>
    /// The async overload forwards to the same body, so it must not be the half that still answers
    /// silently — § TASK-245: the twin you patched may not be the one anything calls.
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task The_async_overload_fails_the_same_way()
    {
        Func<System.Threading.Tasks.Task> act = () => UnreachableStore().GetAppliedVersionsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// <b>Contract pin.</b> The read path now agrees with its own write path: <c>RecordMigration</c> has
    /// always thrown on an invalid response, which is what made the silent read a contradiction rather
    /// than a policy.
    /// </summary>
    [Fact]
    public void The_write_path_still_throws_on_a_failed_response()
    {
        Action act = () => UnreachableStore().RecordMigration(new ProbeMigration());

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class ProbeMigration : Data.Migrations.AbstractMigration
    {
        public override long Version => 1;
        public override string Name => "Probe";
        public override void Up(Data.Migrations.Context.IMigrationContext context) { }
    }
}
