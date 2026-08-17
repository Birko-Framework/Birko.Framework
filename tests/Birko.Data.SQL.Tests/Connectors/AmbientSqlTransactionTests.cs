using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Moq;
using Xunit;

namespace Birko.Data.SQL.Tests.Connectors;

/// <summary>
/// TASK-240 — the ambient transaction scope itself, isolated from any store or connector.
///
/// <para>
/// The end-to-end consequences are proven in the SQLite and PostgreSQL suites. These pin the primitive's
/// two non-obvious properties directly, because both were got wrong on the first attempt and neither is
/// visible from a passing end-to-end test:
/// </para>
/// <list type="number">
/// <item>an <c>async</c> method cannot publish an <see cref="AsyncLocal{T}"/> to its caller, which is why
/// the boundary is published by mutating a cell installed <b>synchronously</b>;</item>
/// <item>a boundary must stop resolving the moment its owner leaves, even for a flow still holding a
/// stale cell — otherwise a later read runs on a disposed connection.</item>
/// </list>
/// </summary>
public class AmbientSqlTransactionTests : IDisposable
{
    // Mocks rather than a real database: the scope only ever CARRIES the connection and transaction —
    // it opens nothing, commits nothing and disposes nothing — so a real one would add a provider
    // dependency to this project without testing anything extra. The end-to-end behaviour is proven
    // against real SQLite and PostgreSQL in their own suites.
    private readonly DbConnection _connection = Mock.Of<DbConnection>();
    private readonly DbTransaction _transaction = Mock.Of<DbTransaction>();

    public void Dispose()
    {
    }

    private IDisposable Enter(string settingsId) =>
        AmbientSqlTransaction.Enter(settingsId, _connection, _transaction);

    // ---------------------------------------------------------------- keyed by settings id

    [Fact]
    public void A_boundary_is_found_only_for_the_database_it_covers()
    {
        using var _ = Enter("db-a");

        AmbientSqlTransaction.Find("db-a").Should().NotBeNull();
        AmbientSqlTransaction.Find("db-b").Should().BeNull(
            "a boundary on one database must not capture writes to another");
    }

    [Fact]
    public void Boundaries_against_different_databases_compose()
    {
        using var outer = Enter("db-a");
        using var inner = Enter("db-b");

        AmbientSqlTransaction.Find("db-a").Should().NotBeNull();
        AmbientSqlTransaction.Find("db-b").Should().NotBeNull();
    }

    [Fact]
    public void A_null_or_empty_settings_id_never_matches()
    {
        using var _ = Enter("db-a");

        AmbientSqlTransaction.Find(null).Should().BeNull();
        AmbientSqlTransaction.Find(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Leaving_the_scope_removes_the_boundary()
    {
        using (var _ = Enter("db-a"))
        {
            AmbientSqlTransaction.Find("db-a").Should().NotBeNull();
        }

        AmbientSqlTransaction.Find("db-a").Should().BeNull();
        AmbientSqlTransaction.Current.Should().BeNull();
    }

    [Fact]
    public void The_innermost_boundary_for_a_database_wins()
    {
        using var outer = Enter("db-a");
        var outerEntry = AmbientSqlTransaction.Find("db-a");

        using (var inner = Enter("db-a"))
        {
            AmbientSqlTransaction.Find("db-a").Should().NotBeSameAs(outerEntry);
        }

        AmbientSqlTransaction.Find("db-a").Should().BeSameAs(outerEntry,
            "leaving the inner scope must restore the outer one, not clear it");
    }

    // ---------------------------------------------------------------- ended entries stop resolving

    [Fact]
    public void An_ended_boundary_is_not_returned_even_from_a_stale_cell()
    {
        // Simulates what a unit of work disposed through DisposeAsync leaves behind: the entry is
        // released, but a flow may still be holding a cell that references it. Resolving it anyway means
        // running a later statement on a connection that has already been disposed.
        var scope = Enter("db-a");
        var entry = AmbientSqlTransaction.Find("db-a")!;

        using var stale = AmbientSqlTransaction.InstallCell();   // seeded with `entry` as its head
        scope.Dispose();

        entry.IsEnded.Should().BeTrue();
        AmbientSqlTransaction.Find("db-a").Should().BeNull(
            "the stale cell still references the entry, so correctness cannot depend on cell restoration");
        AmbientSqlTransaction.Current.Should().BeNull();
    }

    [Fact]
    public void Disposing_a_scope_twice_is_harmless()
    {
        var scope = Enter("db-a");
        scope.Dispose();
        var act = () => scope.Dispose();
        act.Should().NotThrow();
    }

    // ---------------------------------------------------------------- rollback-only

    [Fact]
    public void A_boundary_can_be_marked_rollback_only()
    {
        using var _ = Enter("db-a");
        var entry = AmbientSqlTransaction.Find("db-a")!;

        entry.IsRollbackOnly.Should().BeFalse();
        entry.MarkRollbackOnly();
        entry.IsRollbackOnly.Should().BeTrue(
            "a nested participant's rollback must survive to the owner's commit, or its decision is "
          + "silently discarded");
    }

    // ---------------------------------------------------------------- flow isolation (the trap)

    [Fact]
    public async Task A_boundary_entered_in_one_flow_is_invisible_to_a_sibling_flow()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkedIt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AmbientSqlTransaction.Entry? seenBySibling = null;

        var holder = Task.Run(async () =>
        {
            using var _ = Enter("db-a");
            entered.SetResult();
            await checkedIt.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });

        var sibling = Task.Run(async () =>
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            seenBySibling = AmbientSqlTransaction.Find("db-a");
            checkedIt.SetResult();
        });

        await Task.WhenAll(holder, sibling).WaitAsync(TimeSpan.FromSeconds(60));

        seenBySibling.Should().BeNull(
            "this is the whole reason the boundary is an AsyncLocal and not connector state — a "
          + "process-wide answer would make one request's transaction capture every other request's writes");
    }

    [Fact]
    public async Task A_boundary_entered_in_a_parent_flow_IS_visible_to_a_child_it_starts()
    {
        using var _ = Enter("db-a");

        var seen = await Task.Run(() => AmbientSqlTransaction.Find("db-a"));

        seen.Should().NotBeNull(
            "work the boundary itself starts is inside the boundary — that is what lets a service method "
          + "span several stores without wiring each one by hand");
    }

    [Fact]
    public async Task Installing_a_cell_keeps_a_childs_pushes_out_of_the_parents_view()
    {
        using var parentCell = AmbientSqlTransaction.InstallCell();

        await Task.Run(() =>
        {
            using var childCell = AmbientSqlTransaction.InstallCell();
            using var __ = Enter("db-a");
            AmbientSqlTransaction.Find("db-a").Should().NotBeNull("visible inside the child");
        });

        AmbientSqlTransaction.Find("db-a").Should().BeNull(
            "a fresh cell per unit of work is what stops two concurrent flows forked from a common "
          + "ancestor from sharing one chain");
    }

    // ---------------------------------------------------------------- the async-publication pitfall

    /// <summary>
    /// Pins the runtime fact the whole cell design exists for.
    /// </summary>
    /// <remarks>
    /// If a future runtime changed this, <c>InstallCell</c> would become unnecessary rather than wrong —
    /// but the design comment explaining it would silently stop being true, and this test is what would
    /// say so.
    /// </remarks>
    [Fact]
    public async Task An_async_method_cannot_publish_an_ambient_boundary_to_its_caller()
    {
        await EnterFromAnAsyncMethod();

        AmbientSqlTransaction.Find("db-a").Should().BeNull(
            "AsyncMethodBuilder.Start saves the ExecutionContext and restores it when the state machine "
          + "returns, so an awaited BeginAsync() cannot publish a boundary — which is why SqlUnitOfWork "
          + "installs its cell in a synchronous constructor");
    }

    private async Task EnterFromAnAsyncMethod()
    {
        await Task.Yield();
        Enter("db-a");   // deliberately not disposed: the point is that it is invisible to the caller
    }

    [Fact]
    public void A_synchronous_method_CAN_publish_an_ambient_boundary_to_its_caller()
    {
        EnterFromASyncMethod();
        try
        {
            AmbientSqlTransaction.Find("db-a").Should().NotBeNull(
                "the counterpart of the test above — this asymmetry is the entire reason the cell is "
              + "installed from a constructor");
        }
        finally
        {
            AmbientSqlTransaction.Current?.MarkRollbackOnly();
        }
    }

    private void EnterFromASyncMethod() => Enter("db-a");

    // ---------------------------------------------------------------- argument guards

    [Fact]
    public void Entering_requires_a_settings_id_a_connection_and_a_transaction()
    {
        var noId = () => AmbientSqlTransaction.Enter(string.Empty, _connection, _transaction);
        noId.Should().Throw<ArgumentNullException>();

        var noConnection = () => AmbientSqlTransaction.Enter("db-a", null!, _transaction);
        noConnection.Should().Throw<ArgumentNullException>();

        var noTransaction = () => AmbientSqlTransaction.Enter("db-a", _connection, null!);
        noTransaction.Should().Throw<ArgumentNullException>();
    }
}
