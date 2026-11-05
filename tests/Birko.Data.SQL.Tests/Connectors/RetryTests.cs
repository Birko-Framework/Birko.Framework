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

public class RetryTests
{
    #region Test Infrastructure

    /// <summary>Transient, but not guaranteed unapplied: a command timeout, a lost connection.</summary>
    private class TransientException : DbException
    {
        public override bool IsTransient => true;
        public TransientException(string message) : base(message) { }
    }

    /// <summary>Transient AND guaranteed unapplied: a deadlock victim, SQLite busy.</summary>
    private class NotAppliedException : DbException
    {
        public override bool IsTransient => true;
        public NotAppliedException(string message) : base(message) { }
    }

    private class NonTransientException : DbException
    {
        public override bool IsTransient => false;
        public NonTransientException(string message) : base(message) { }
    }

    private class TestSettings : PasswordSettings
    {
        public TestSettings()
        {
            Location = "test";
            Name = "test";
            Password = "test";
        }
    }

    /// <summary>
    /// Minimal concrete connector for testing base retry behavior. Says which test exception is "not applied", as
    /// a provider does for its deadlock code.
    /// </summary>
    private class TestConnectorBase : AbstractConnectorBase
    {
        public TestConnectorBase() : base(new TestSettings()) { }

        public override DbConnection CreateConnection(PasswordSettings settings)
            => throw new NotImplementedException();
        public override string ConvertType(DbType type, SQL.Fields.AbstractField field)
            => throw new NotImplementedException();
        public override string FieldDefinition(SQL.Fields.AbstractField field)
            => throw new NotImplementedException();

        public override bool IsNotAppliedException(Exception ex) => ex is NotAppliedException;

        /// <summary>Runs <paramref name="action"/> as one attempt; <paramref name="sent"/> marks the statement as sent first.</summary>
        public void TestExecuteWithRetry(Action action, bool sent = true)
            => ExecuteWithRetry(attempt => { if (sent) attempt.MarkSent(); action(); });
        public Task TestExecuteWithRetryAsync(Func<Task> action, CancellationToken ct = default, bool sent = true)
            => ExecuteWithRetryAsync(attempt => { if (sent) attempt.MarkSent(); return action(); }, ct);
    }

    private static RetryPolicy Fast(int maxRetries) =>
        new() { MaxRetries = maxRetries, BaseDelay = TimeSpan.FromMilliseconds(1), UseExponentialBackoff = false };

    #endregion

    #region IsTransientException / IsNotAppliedException

    [Fact]
    public void IsTransientException_TimeoutException_ReturnsTrue()
    {
        var connector = new TestConnectorBase();
        connector.IsTransientException(new TimeoutException()).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_TransientDbException_ReturnsTrue()
    {
        var connector = new TestConnectorBase();
        connector.IsTransientException(new TransientException("test")).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_NonTransientDbException_ReturnsFalse()
    {
        var connector = new TestConnectorBase();
        connector.IsTransientException(new NonTransientException("test")).Should().BeFalse();
    }

    [Fact]
    public void IsTransientException_GenericException_ReturnsFalse()
    {
        var connector = new TestConnectorBase();
        connector.IsTransientException(new InvalidOperationException("test")).Should().BeFalse();
    }

    [Fact]
    public void The_base_connector_calls_nothing_unapplied()
    {
        var connector = new MinimalConnector();
        connector.IsNotAppliedException(new TransientException("a timeout")).Should().BeFalse(
            "a provider that does not say which of its errors applied nothing gets no retry once a statement was sent");
        connector.IsNotAppliedException(new TimeoutException()).Should().BeFalse();
    }

    private sealed class MinimalConnector : AbstractConnectorBase
    {
        public MinimalConnector() : base(new TestSettings()) { }
        public override DbConnection CreateConnection(PasswordSettings settings) => throw new NotImplementedException();
        public override string ConvertType(DbType type, SQL.Fields.AbstractField field) => throw new NotImplementedException();
        public override string FieldDefinition(SQL.Fields.AbstractField field) => throw new NotImplementedException();
    }

    #endregion

    #region RetryPolicy property — TASK-305

    [Fact]
    public void RetryPolicy_defaults_to_two_short_retries()
    {
        var policy = new TestConnectorBase().RetryPolicy;
        policy.MaxRetries.Should().Be(2, "TASK-305: a small default retry, decided by the user (it was None)");
        policy.BaseDelay.Should().Be(TimeSpan.FromMilliseconds(50));
        policy.MaxDelay.Should().Be(TimeSpan.FromMilliseconds(500));
        policy.UseExponentialBackoff.Should().BeTrue();
        policy.AddJitter.Should().BeTrue("callers that deadlocked together should not retry in lockstep");
    }

    [Fact]
    public void Each_connector_gets_its_own_default_policy()
    {
        var a = new TestConnectorBase();
        var b = new TestConnectorBase();
        a.RetryPolicy.Should().NotBeSameAs(b.RetryPolicy);
        a.RetryPolicy.MaxRetries = 7;
        b.RetryPolicy.MaxRetries.Should().Be(2, "a policy is mutable; one shared instance would leak a change (rule 36)");
    }

    [Fact]
    public void RetryPolicy_CanBeSet()
    {
        var connector = new TestConnectorBase();
        connector.RetryPolicy = RetryPolicy.Default;
        connector.RetryPolicy.MaxRetries.Should().Be(3);
    }

    #endregion

    #region ExecuteWithRetry (sync)

    [Fact]
    public void ExecuteWithRetry_Success_ExecutesOnce()
    {
        var connector = new TestConnectorBase();
        int callCount = 0;

        connector.TestExecuteWithRetry(() => callCount++);

        callCount.Should().Be(1);
    }

    [Fact]
    public void ExecuteWithRetry_PolicyNone_ThrowsOnFirstFailure()
    {
        var connector = new TestConnectorBase { RetryPolicy = RetryPolicy.None };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetry(() => { callCount++; throw new NotAppliedException("fail"); });

        act.Should().Throw<NotAppliedException>();
        callCount.Should().Be(1, "RetryPolicy.None still turns retrying off");
    }

    [Fact]
    public void ExecuteWithRetry_NotAppliedFailure_RetriesAndSucceeds()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            if (callCount < 3) throw new NotAppliedException("deadlock victim");
        });

        callCount.Should().Be(3);
    }

    [Fact]
    public void ExecuteWithRetry_with_the_default_policy_a_deadlock_victim_is_retried()
    {
        var connector = new TestConnectorBase();
        int callCount = 0;

        connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            if (callCount == 1) throw new NotAppliedException("deadlock victim");
        });

        callCount.Should().Be(2, "the shipped default retries an error the server says it did not apply");
    }

    [Fact]
    public void ExecuteWithRetry_a_transient_failure_after_sending_is_not_retried()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            throw new TransientException("timeout: the write may have been applied");
        }, sent: true);

        act.Should().Throw<TransientException>();
        callCount.Should().Be(1, "re-running a write that may have applied could apply it twice (TASK-305)");
    }

    [Fact]
    public void ExecuteWithRetry_a_transient_failure_before_sending_is_retried()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            if (callCount < 3) throw new TransientException("could not open the connection");
        }, sent: false);

        callCount.Should().Be(3, "nothing reached the server, so trying again cannot apply anything twice");
    }

    [Fact]
    public void ExecuteWithRetry_NonTransientFailure_DoesNotRetry()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            throw new InvalidOperationException("non-transient");
        }, sent: false);

        act.Should().Throw<InvalidOperationException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public void ExecuteWithRetry_ExhaustsRetries_Throws()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(2) };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            throw new NotAppliedException("always fails");
        });

        act.Should().Throw<NotAppliedException>();
        callCount.Should().Be(3); // 1 initial + 2 retries
    }

    #endregion

    #region ExecuteWithRetryAsync

    [Fact]
    public async Task ExecuteWithRetryAsync_NotAppliedFailure_RetriesAndSucceeds()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        await connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            if (callCount < 3) throw new NotAppliedException("deadlock victim");
            await Task.CompletedTask;
        });

        callCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_a_transient_failure_after_sending_is_not_retried()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new TransientException("lost connection mid-statement");
        });

        await act.Should().ThrowAsync<TransientException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_a_transient_failure_before_sending_is_retried()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        await connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            if (callCount < 2) throw new TransientException("could not open the connection");
        }, sent: false);

        callCount.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonTransientFailure_DoesNotRetry()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(3) };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new InvalidOperationException("non-transient");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ExhaustsRetries_Throws()
    {
        var connector = new TestConnectorBase { RetryPolicy = Fast(2) };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new NotAppliedException("always fails");
        });

        await act.Should().ThrowAsync<NotAppliedException>();
        callCount.Should().Be(3); // 1 initial + 2 retries
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancellationToken_Respected()
    {
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 5, BaseDelay = TimeSpan.FromSeconds(10) }
        };
        using var cts = new CancellationTokenSource();
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            if (callCount == 1) cts.Cancel();
            throw new NotAppliedException("deadlock victim");
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
