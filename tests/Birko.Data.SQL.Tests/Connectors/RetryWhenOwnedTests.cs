using Birko.Data.SQL.Connectors;
using FluentAssertions;
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using PasswordSettings = Birko.Configuration.PasswordSettings;

namespace Birko.Data.SQL.Tests.Connectors;

/// <summary>
/// TASK-258 — <c>retryWhenOwned</c> claims to preserve each provider's bulk retry behaviour, and until now
/// nothing asserted it. Measured before writing a line: <b>0 occurrences of the flag anywhere in
/// Framework.Tests</b>, which is what made "preserved" an argument rather than a measurement.
/// </summary>
/// <remarks>
/// <para>
/// <b>THE FINDING THIS FILE EXISTS TO PIN — the flag is INERT as shipped.</b> Chasing the flag's behaviour
/// turned up something the task did not anticipate: <see cref="AbstractConnectorBase.RetryPolicy"/> defaults
/// to <c>RetryPolicy.None</c> (<c>MaxRetries = 0</c>), and <c>ExecuteWithRetry[Async]</c> short-circuits to a
/// bare call when <c>MaxRetries &lt;= 0</c>. Grepped across the whole framework and the Symbio consumer,
/// including every <c>appsettings*.json</c>: <b>nothing sets a RetryPolicy anywhere</b> except
/// <c>RetryTests.cs</c>, which sets one explicitly in order to exercise the loop.
/// </para>
/// <para>
/// So as shipped, <c>retryWhenOwned: true</c> and <c>retryWhenOwned: false</c> produce <b>identical</b>
/// behaviour on every provider — SQLite included. The thing the flag "preserves" is a no-op. That does not
/// make the flag wrong; it makes the claim about it vacuous, and vacuous is worth knowing because the next
/// reader will otherwise assume SQLite is getting the CR-M144 retry it is documented to get.
/// </para>
/// <para>
/// <b>What this file therefore asserts, and what it deliberately does not.</b> The tests below split into two
/// kinds, and the distinction is load-bearing rather than tidy:
/// </para>
/// <list type="bullet">
/// <item>
/// <b>True of the shipped configuration</b> — the participating path never retries and never opens a
/// connection of its own, whatever the flag says. This is TASK-258's third criterion, the one it calls "the
/// assertion worth having most", and it is provider-independent, so it belongs here offline rather than in a
/// live per-provider suite.
/// </item>
/// <item>
/// <b>True only under a policy the product does not currently set</b> — that the flag is correctly
/// <i>wired</i>: with a policy present, <c>true</c> retries and <c>false</c> makes exactly one attempt. These
/// are named <c>UnderAnExplicitPolicy_</c> so nobody reads them as evidence that retries happen in
/// production. They are still worth having: they are what makes the flag's intent hold on the day someone
/// configures a policy, which is precisely when a mis-wiring would start losing or duplicating work.
/// </item>
/// </list>
/// <para>
/// <b>NOT claimed here:</b> that each provider passes the right flag value under real transient errors. That
/// needs induced provider-specific failures (SQLITE_BUSY from a held write lock; a deadlock or serialization
/// failure on the servers) against live servers, and it stays on TASK-258. The call sites were read and do
/// match the claim — PostgreSQL/MySQL/MSSql pass <c>false</c> explicitly at all 18 of their bulk sites,
/// SQLite passes nothing and takes the <c>true</c> default — but reading is not measuring, which is the whole
/// reason this task exists.
/// </para>
/// </remarks>
public class RetryWhenOwnedTests
{
    #region Test infrastructure

    private sealed class TransientException : DbException
    {
        public override bool IsTransient => true;
        public TransientException(string message) : base(message) { }
    }

    private sealed class ProbeSettings : PasswordSettings
    {
        public ProbeSettings(string name)
        {
            Location = "retrywhenowned";
            Name = name;
            Password = "test";
        }
    }

    // -- Minimal ADO stubs. The participating path never touches them; the owned path only opens the
    //    connection and begins a transaction, so nothing here needs to execute SQL. --

    private sealed class FakeTransaction : DbTransaction
    {
        private readonly DbConnection _connection;
        public FakeTransaction(DbConnection connection) => _connection = connection;
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => _connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class FakeConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;
        public override string ConnectionString { get; set; } = "fake";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "0";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => new FakeTransaction(this);
        protected override DbCommand CreateDbCommand() => throw new NotSupportedException();
    }

    /// <summary>
    /// Exposes both bulk halves. <see cref="AbstractAsyncConnector"/> derives from
    /// <see cref="AbstractConnector"/>, so one probe covers the sync and async paths — which are separate
    /// code with separate branches, hence every assertion below is written twice.
    /// </summary>
    private sealed class BulkProbeConnector : AbstractAsyncConnector
    {
        private readonly bool _allowConnections;

        public BulkProbeConnector(PasswordSettings settings, bool allowConnections)
            : base(settings) => _allowConnections = allowConnections;

        public int ConnectionsOpened;

        /// <summary>
        /// When <c>allowConnections</c> is false this THROWS, which is the point: a participating-path test
        /// that passes has proven the path opened no connection of its own. Counting attempts alone would
        /// not — a second connection is the original TASK-242 defect, and it is invisible to an attempt
        /// counter.
        /// </summary>
        public override DbConnection CreateConnection(PasswordSettings settings)
        {
            if (!_allowConnections)
            {
                throw new InvalidOperationException(
                    "CreateConnection was called — the participating path must reuse the boundary's connection.");
            }
            Interlocked.Increment(ref ConnectionsOpened);
            return new FakeConnection();
        }

        public override string ConvertType(DbType type, SQL.Fields.AbstractField field)
            => throw new NotImplementedException();
        public override string FieldDefinition(SQL.Fields.AbstractField field)
            => throw new NotImplementedException();

        public void ProbeRunBulk(Action<DbConnection, DbTransaction, bool> body, bool retryWhenOwned)
            => RunBulk("probe", body, retryWhenOwned);

        public Task ProbeRunBulkAsync(Func<DbConnection, DbTransaction, bool, Task> body, bool retryWhenOwned)
            => RunBulkAsync("probe", (c, t, owned) => body(c, t, owned), retryWhenOwned: retryWhenOwned);
    }

    private static RetryPolicy FastPolicy(int maxRetries = 3) => new()
    {
        MaxRetries = maxRetries,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        UseExponentialBackoff = false,
    };

    /// <summary>
    /// Opens a boundary for <paramref name="connector"/>'s database. The cell must be installed
    /// SYNCHRONOUSLY — an async method cannot publish an AsyncLocal to its caller, which is the defect
    /// AmbientSqlTransaction's own remarks describe.
    /// </summary>
    private static (IDisposable cell, IDisposable scope, FakeConnection connection) EnterBoundary(PasswordSettings settings)
    {
        var cell = AmbientSqlTransaction.InstallCell();
        var connection = new FakeConnection();
        connection.Open();
        var scope = AmbientSqlTransaction.Enter(settings.GetId(), connection, connection.BeginTransaction());
        return (cell, scope, connection);
    }

    #endregion

    #region The participating path never retries — TASK-258's third criterion

    // Both halves, and `retryWhenOwned: true` is passed DELIBERATELY: the flag's existence invites a future
    // caller to thread it through, and this is the one combination that could double-apply statements inside
    // somebody else's transaction. Asserting it with `false` would prove nothing.

    [Fact]
    public void Sync_ParticipatingPath_MakesExactlyOneAttempt_EvenWithRetryRequestedAndAPolicySet()
    {
        var settings = new ProbeSettings(nameof(Sync_ParticipatingPath_MakesExactlyOneAttempt_EvenWithRetryRequestedAndAPolicySet));
        var connector = new BulkProbeConnector(settings, allowConnections: false) { RetryPolicy = FastPolicy() };
        var (cell, scope, _) = EnterBoundary(settings);

        var attempts = 0;
        try
        {
            Action act = () => connector.ProbeRunBulk((_, _, _) =>
            {
                attempts++;
                throw new TransientException("transient inside somebody else's transaction");
            }, retryWhenOwned: true);

            act.Should().Throw<TransientException>("the failure belongs to the boundary owner, not to a retry loop");
            attempts.Should().Be(1,
                "re-running statements inside a transaction whose earlier statements already succeeded can only "
                + "fail differently — retrying is the boundary owner's decision, never the participant's");
            connector.ConnectionsOpened.Should().Be(0);
        }
        finally { scope.Dispose(); cell.Dispose(); }
    }

    [Fact]
    public async Task Async_ParticipatingPath_MakesExactlyOneAttempt_EvenWithRetryRequestedAndAPolicySet()
    {
        var settings = new ProbeSettings(nameof(Async_ParticipatingPath_MakesExactlyOneAttempt_EvenWithRetryRequestedAndAPolicySet));
        var connector = new BulkProbeConnector(settings, allowConnections: false) { RetryPolicy = FastPolicy() };
        var (cell, scope, _) = EnterBoundary(settings);

        var attempts = 0;
        try
        {
            Func<Task> act = () => connector.ProbeRunBulkAsync((_, _, _) =>
            {
                attempts++;
                throw new TransientException("transient inside somebody else's transaction");
            }, retryWhenOwned: true);

            await act.Should().ThrowAsync<TransientException>();
            attempts.Should().Be(1);
            connector.ConnectionsOpened.Should().Be(0);
        }
        finally { scope.Dispose(); cell.Dispose(); }
    }

    // The other half of "participating": it reuses the boundary's connection rather than opening one. This is
    // the TASK-242 defect itself, and an attempt counter cannot see it.

    [Fact]
    public void Sync_ParticipatingPath_ReusesTheBoundarysConnectionAndReportsNotOwned()
    {
        var settings = new ProbeSettings(nameof(Sync_ParticipatingPath_ReusesTheBoundarysConnectionAndReportsNotOwned));
        var connector = new BulkProbeConnector(settings, allowConnections: false);
        var (cell, scope, boundaryConnection) = EnterBoundary(settings);

        try
        {
            DbConnection? seen = null;
            var owned = true;
            connector.ProbeRunBulk((c, _, o) => { seen = c; owned = o; }, retryWhenOwned: true);

            seen.Should().BeSameAs(boundaryConnection, "a second connection is exactly the escape TASK-242 removed");
            owned.Should().BeFalse("the body must not commit, roll back or dispose what it does not own");
        }
        finally { scope.Dispose(); cell.Dispose(); }
    }

    [Fact]
    public async Task Async_ParticipatingPath_ReusesTheBoundarysConnectionAndReportsNotOwned()
    {
        var settings = new ProbeSettings(nameof(Async_ParticipatingPath_ReusesTheBoundarysConnectionAndReportsNotOwned));
        var connector = new BulkProbeConnector(settings, allowConnections: false);
        var (cell, scope, boundaryConnection) = EnterBoundary(settings);

        try
        {
            DbConnection? seen = null;
            var owned = true;
            await connector.ProbeRunBulkAsync((c, _, o) => { seen = c; owned = o; return Task.CompletedTask; },
                retryWhenOwned: true);

            seen.Should().BeSameAs(boundaryConnection);
            owned.Should().BeFalse();
        }
        finally { scope.Dispose(); cell.Dispose(); }
    }

    #endregion

    #region The flag is inert as shipped — the finding, pinned so it cannot rot silently

    // These two are the ones that will fail the day somebody configures a default RetryPolicy, and that is
    // the intended alarm: at that moment SQLite's bulk paths silently START retrying, and TASK-258's first
    // criterion becomes a real question rather than a vacuous one.

    [Fact]
    public void WithTheSHIPPEDDefaults_RetryWhenOwnedTrue_DoesNotRetry_BecauseNoRetryPolicyIsEverConfigured()
    {
        var settings = new ProbeSettings(nameof(WithTheSHIPPEDDefaults_RetryWhenOwnedTrue_DoesNotRetry_BecauseNoRetryPolicyIsEverConfigured));
        var connector = new BulkProbeConnector(settings, allowConnections: true);

        connector.RetryPolicy.MaxRetries.Should().Be(0,
            "RetryPolicy defaults to None, and nothing in the framework or the Symbio consumer — including "
            + "every appsettings*.json — assigns one. If this ever fails, the flag has stopped being inert "
            + "and TASK-258's per-provider criteria need answering for real.");

        var attempts = 0;
        Action act = () => connector.ProbeRunBulk((_, _, _) =>
        {
            attempts++;
            throw new TransientException("transient on the owned path");
        }, retryWhenOwned: true);

        act.Should().Throw<TransientException>();
        attempts.Should().Be(1,
            "with MaxRetries = 0 ExecuteWithRetry short-circuits, so `true` and `false` are the same "
            + "behaviour — this is what makes 'preserves each provider's retry policy' vacuous today");
    }

    [Fact]
    public async Task WithTheSHIPPEDDefaults_TheFlagMakesNoDifferenceEitherWay_AsyncHalf()
    {
        var settings = new ProbeSettings(nameof(WithTheSHIPPEDDefaults_TheFlagMakesNoDifferenceEitherWay_AsyncHalf));

        // Asserted as an EQUIVALENCE rather than as two separate counts: the claim is that the flag is inert,
        // and two independent "== 1" assertions would also pass if one path were broken in some other way.
        var counts = new int[2];
        for (var i = 0; i < 2; i++)
        {
            var connector = new BulkProbeConnector(settings, allowConnections: true);
            var index = i;
            Func<Task> act = () => connector.ProbeRunBulkAsync((_, _, _) =>
            {
                counts[index]++;
                throw new TransientException("transient on the owned path");
            }, retryWhenOwned: i == 0);
            await act.Should().ThrowAsync<TransientException>();
        }

        counts[0].Should().Be(counts[1], "under the shipped default policy the flag changes nothing");
        counts[0].Should().Be(1);
    }

    #endregion

    #region The flag IS wired correctly — true only under a policy the product does not set

    // Named `UnderAnExplicitPolicy_` on purpose: these must not be read as evidence that production retries.
    // They assert the wiring, so that the intent holds on the day a policy is configured.

    [Fact]
    public void UnderAnExplicitPolicy_OwnedPathWithRetryWhenOwnedTrue_Retries()
    {
        var settings = new ProbeSettings(nameof(UnderAnExplicitPolicy_OwnedPathWithRetryWhenOwnedTrue_Retries));
        var connector = new BulkProbeConnector(settings, allowConnections: true) { RetryPolicy = FastPolicy(3) };

        var attempts = 0;
        Action act = () => connector.ProbeRunBulk((_, _, _) =>
        {
            attempts++;
            throw new TransientException("transient");
        }, retryWhenOwned: true);

        act.Should().Throw<TransientException>();
        attempts.Should().Be(4, "the initial attempt plus MaxRetries = 3");
        connector.ConnectionsOpened.Should().Be(4,
            "each attempt must get a FRESH connection — retrying on the connection that just failed is what "
            + "makes a retry unsafe, and CR-M144's reasoning depends on the whole unit rolling back first");
    }

    [Fact]
    public void UnderAnExplicitPolicy_OwnedPathWithRetryWhenOwnedFalse_MakesExactlyOneAttempt()
    {
        var settings = new ProbeSettings(nameof(UnderAnExplicitPolicy_OwnedPathWithRetryWhenOwnedFalse_MakesExactlyOneAttempt));
        var connector = new BulkProbeConnector(settings, allowConnections: true) { RetryPolicy = FastPolicy(3) };

        var attempts = 0;
        Action act = () => connector.ProbeRunBulk((_, _, _) =>
        {
            attempts++;
            throw new TransientException("transient");
        }, retryWhenOwned: false);

        act.Should().Throw<TransientException>();
        attempts.Should().Be(1,
            "PostgreSQL, MySQL and MSSql pass false at all 18 of their bulk sites so no consumer silently "
            + "gains a retry it never had");
    }

    [Fact]
    public async Task UnderAnExplicitPolicy_AsyncOwnedPath_HonoursTheFlagBothWays()
    {
        var settings = new ProbeSettings(nameof(UnderAnExplicitPolicy_AsyncOwnedPath_HonoursTheFlagBothWays));

        var retried = 0;
        var connectorTrue = new BulkProbeConnector(settings, allowConnections: true) { RetryPolicy = FastPolicy(2) };
        Func<Task> withRetry = () => connectorTrue.ProbeRunBulkAsync((_, _, _) =>
        {
            retried++;
            throw new TransientException("transient");
        }, retryWhenOwned: true);
        await withRetry.Should().ThrowAsync<TransientException>();
        retried.Should().Be(3, "initial attempt plus MaxRetries = 2");

        var once = 0;
        var connectorFalse = new BulkProbeConnector(settings, allowConnections: true) { RetryPolicy = FastPolicy(2) };
        Func<Task> noRetry = () => connectorFalse.ProbeRunBulkAsync((_, _, _) =>
        {
            once++;
            throw new TransientException("transient");
        }, retryWhenOwned: false);
        await noRetry.Should().ThrowAsync<TransientException>();
        once.Should().Be(1);

        // The discrimination, asserted rather than left implied: a build where the flag is ignored gives these
        // two the same count, and each individual assertion above would still be checking a plausible number.
        retried.Should().NotBe(once);
    }

    [Fact]
    public void UnderAnExplicitPolicy_ANonTransientFailure_IsNotRetried_EvenWhenRetryIsRequested()
    {
        var settings = new ProbeSettings(nameof(UnderAnExplicitPolicy_ANonTransientFailure_IsNotRetried_EvenWhenRetryIsRequested));
        var connector = new BulkProbeConnector(settings, allowConnections: true) { RetryPolicy = FastPolicy(3) };

        var attempts = 0;
        Action act = () => connector.ProbeRunBulk((_, _, _) =>
        {
            attempts++;
            throw new InvalidOperationException("a bug, not a lock");
        }, retryWhenOwned: true);

        act.Should().Throw<InvalidOperationException>();
        attempts.Should().Be(1,
            "retrying a deterministic failure burns the policy's budget and delays the real error — the "
            + "control for the retry tests above, so they cannot pass by retrying everything");
    }

    #endregion
}
