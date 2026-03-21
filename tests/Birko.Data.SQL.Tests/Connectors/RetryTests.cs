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

    private class TransientException : DbException
    {
        public override bool IsTransient => true;
        public TransientException(string message) : base(message) { }
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
    /// Minimal concrete connector for testing base retry behavior.
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

        // Expose protected methods for testing
        public void TestExecuteWithRetry(Action action) => ExecuteWithRetry(action);
        public Task TestExecuteWithRetryAsync(Func<Task> action, CancellationToken ct = default)
            => ExecuteWithRetryAsync(action, ct);
    }

    #endregion

    #region IsTransientException

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

    #endregion

    #region RetryPolicy property

    [Fact]
    public void RetryPolicy_DefaultsToNone()
    {
        var connector = new TestConnectorBase();
        connector.RetryPolicy.MaxRetries.Should().Be(0);
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
    public void ExecuteWithRetry_NoRetryPolicy_ExecutesOnce()
    {
        var connector = new TestConnectorBase();
        int callCount = 0;

        connector.TestExecuteWithRetry(() => callCount++);

        callCount.Should().Be(1);
    }

    [Fact]
    public void ExecuteWithRetry_NoRetryPolicy_ThrowsOnFirstFailure()
    {
        var connector = new TestConnectorBase();

        var act = () => connector.TestExecuteWithRetry(() => throw new TransientException("fail"));

        act.Should().Throw<TransientException>();
    }

    [Fact]
    public void ExecuteWithRetry_TransientFailure_RetriesAndSucceeds()
    {
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1), UseExponentialBackoff = false }
        };
        int callCount = 0;

        connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            if (callCount < 3) throw new TransientException("transient");
        });

        callCount.Should().Be(3);
    }

    [Fact]
    public void ExecuteWithRetry_NonTransientFailure_DoesNotRetry()
    {
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) }
        };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            throw new InvalidOperationException("non-transient");
        });

        act.Should().Throw<InvalidOperationException>();
        callCount.Should().Be(1);
    }

    [Fact]
    public void ExecuteWithRetry_ExhaustsRetries_Throws()
    {
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1), UseExponentialBackoff = false }
        };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetry(() =>
        {
            callCount++;
            throw new TransientException("always fails");
        });

        act.Should().Throw<TransientException>();
        callCount.Should().Be(3); // 1 initial + 2 retries
    }

    #endregion

    #region ExecuteWithRetryAsync

    [Fact]
    public async Task ExecuteWithRetryAsync_TransientFailure_RetriesAndSucceeds()
    {
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1), UseExponentialBackoff = false }
        };
        int callCount = 0;

        await connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            if (callCount < 3) throw new TransientException("transient");
            await Task.CompletedTask;
        });

        callCount.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NonTransientFailure_DoesNotRetry()
    {
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 3, BaseDelay = TimeSpan.FromMilliseconds(1) }
        };
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
        var connector = new TestConnectorBase
        {
            RetryPolicy = new RetryPolicy { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1), UseExponentialBackoff = false }
        };
        int callCount = 0;

        var act = () => connector.TestExecuteWithRetryAsync(async () =>
        {
            callCount++;
            await Task.CompletedTask;
            throw new TransientException("always fails");
        });

        await act.Should().ThrowAsync<TransientException>();
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
            throw new TransientException("transient");
        }, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
