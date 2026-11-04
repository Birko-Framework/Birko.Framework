using System;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.MongoDB.Tests;

/// <summary>
/// TASK-722. Measured before the fix: <c>new MongoMigrationRunner(null)</c> read <c>mongoClient.Database</c> in its base
/// constructor call, before its own null check, so a null client was a <c>NullReferenceException</c>.
/// </summary>
public class Task722RunnerNullClientTests
{
    [Fact]
    public void A_null_client_is_an_argument_error()
    {
        var act = () => new MongoMigrationRunner(null!);

        act.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("mongoClient");
    }
}
