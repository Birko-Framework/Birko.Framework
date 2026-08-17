using System;
using System.Threading.Tasks;
using Birko.BackgroundJobs;
using Birko.BackgroundJobs.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.BackgroundJobs.SQL.Tests;

/// <summary>
/// Pins the half of the <see cref="IJobLockProvider"/> contract that SQL implements differently from
/// Redis, settled by TASK-232.
/// </summary>
/// <remarks>
/// The interface was introduced to make the two providers substitutable, and they were not: SQL read the
/// single <c>timeout</c> as "how long to wait for the lock" while Redis used it as the key's expiry. These
/// tests fix SQL's half of the answer in place — session-scoped, no lease — so a future change cannot
/// quietly re-converge them by widening SQL instead of narrowing the ambiguity.
/// </remarks>
public class SqlJobLockProviderContractTests
{
    private static SqLiteSettings Settings() =>
        new SqLiteSettings(System.IO.Path.GetTempPath(), "birko_task232_lock.db");

    [Fact]
    public void SQL_declares_itself_session_scoped_not_lease_based()
    {
        using var p = new SqlJobLockProvider<SqLiteConnector>(Settings());

        ((IJobLockProvider)p).IsLeaseBased.Should().BeFalse(
            "a SQL advisory lock lives on a dedicated connection, so the server releases it when the " +
            "holder dies — that is a session lock, and it is the stronger of the two guarantees");
    }

    [Fact]
    public async Task A_lease_duration_is_refused_rather_than_silently_ignored()
    {
        using var p = new SqlJobLockProvider<SqLiteConnector>(Settings());

        var act = () => p.TryAcquireAsync("x", TimeSpan.Zero, TimeSpan.FromMinutes(5));

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("leaseDuration",
            "accepting a bound this provider cannot enforce would be the same class of lie as the " +
            "original overloaded timeout — a caller would believe the lock self-releases, and it does not");
    }

    [Fact]
    public async Task A_null_lease_is_the_session_request_and_is_accepted()
    {
        using var p = new SqlJobLockProvider<SqLiteConnector>(Settings());

        // SQLite has no cross-connection advisory lock, so this returns false rather than acquiring —
        // deliberately (CR-M027: it used to return true for a lock it never took). The assertion here is
        // only that a null lease is not rejected; the guard above must not fire on the normal path.
        var act = () => p.TryAcquireAsync("x", TimeSpan.Zero);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task An_unsupported_dialect_reports_failure_instead_of_a_lock_it_never_took()
    {
        using var p = new SqlJobLockProvider<SqLiteConnector>(Settings());

        (await p.TryAcquireAsync("x", TimeSpan.Zero)).Should().BeFalse(
            "SQLite offers no portable cross-connection advisory lock, so callers must be able to fall " +
            "back deliberately");
    }

    [Fact]
    public async Task Releasing_a_lock_that_was_never_acquired_is_not_an_error()
    {
        using var p = new SqlJobLockProvider<SqLiteConnector>(Settings());

        var act = () => p.ReleaseAsync("never-held");

        await act.Should().NotThrowAsync();
    }
}
