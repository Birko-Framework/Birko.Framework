using System;
using Birko.Data.InfluxDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// TASK-724. Measured before the fix: <c>Settings.IsTransientException</c> called any exception whose message held
/// "429", "503" or "unavailable" transient, so a refusal naming such a bucket or id was retried. The status decides now.
/// </summary>
public class Task724TransientTests
{
    private static readonly Settings S = new("http://localhost:8086", "b", "t", "o");

    [Theory]
    [InlineData("bucket 'metrics-503' not found")]
    [InlineData("organization 429-labs has no bucket")]
    [InlineData("field type unavailable for column x")]
    public void A_message_that_mentions_a_status_is_not_transient(string message)
    {
        S.IsTransientException(new InvalidOperationException(message)).Should().BeFalse();
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(503, true)]
    [InlineData(404, false)]
    public void The_status_decides(int status, bool transient)
    {
        var ex = new global::InfluxDB.Client.Core.Exceptions.HttpException("refused", status, null!);

        S.IsTransientException(ex).Should().Be(transient);
    }
}
