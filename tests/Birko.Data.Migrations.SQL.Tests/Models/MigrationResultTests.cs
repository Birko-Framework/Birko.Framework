using Birko.Data.Migrations;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests.Models;

public class MigrationResultTests
{
    [Fact]
    public void Successful_SetsAllFields()
    {
        var executed = new List<ExecutedMigration>();
        var result = MigrationResult.Successful(1, 3, MigrationDirection.Up, executed);

        result.Success.Should().BeTrue();
        result.FromVersion.Should().Be(1);
        result.ToVersion.Should().Be(3);
        result.Direction.Should().Be(MigrationDirection.Up);
        result.ExecutedMigrations.Should().BeSameAs(executed);
        result.ErrorMessage.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Failed_SetsAllFields()
    {
        var ex = new InvalidOperationException("test");
        var result = MigrationResult.Failed(2, MigrationDirection.Down, "Migration failed", ex);

        result.Success.Should().BeFalse();
        result.FromVersion.Should().Be(2);
        result.ToVersion.Should().Be(2);
        result.Direction.Should().Be(MigrationDirection.Down);
        result.ErrorMessage.Should().Be("Migration failed");
        result.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void Failed_WithoutException_ExceptionIsNull()
    {
        var result = MigrationResult.Failed(0, MigrationDirection.Up, "error");

        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Default_ExecutedMigrations_IsEmpty()
    {
        var result = new MigrationResult();

        result.ExecutedMigrations.Should().BeEmpty();
    }
}
