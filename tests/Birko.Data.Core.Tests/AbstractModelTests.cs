using System;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

public class AbstractModelTests
{
    private sealed class TestModel : AbstractModel { }

    private sealed class TestLogModel : AbstractLogModel { }

    [Fact]
    public void CopyTo_with_target_copies_guid_and_returns_target()
    {
        var source = new TestModel { Guid = Guid.NewGuid() };
        var target = new TestModel();

        var result = source.CopyTo(target);

        result.Should().BeSameAs(target);
        target.Guid.Should().Be(source.Guid);
    }

    [Fact]
    public void CopyTo_without_target_returns_non_null_self()
    {
        // CR-M082: CopyTo() returned clone! (null) when called with no argument, violating the
        // non-null ICopyable<T> contract — model.CopyTo().Guid then NRE'd. It now returns this.
        var source = new TestModel { Guid = Guid.NewGuid() };

        var result = source.CopyTo();

        result.Should().NotBeNull();
        result.Should().BeSameAs(source);
        result.Guid.Should().Be(source.Guid);
    }

    [Fact]
    public void LogModel_CopyTo_without_target_returns_non_null_self()
    {
        // Same CR-M082 pattern in AbstractLogModel.CopyTo.
        var source = new TestLogModel
        {
            Guid = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
        };

        var result = source.CopyTo();

        result.Should().NotBeNull();
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void LogModel_CopyTo_with_target_copies_base_and_log_fields()
    {
        var source = new TestLogModel
        {
            Guid = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            PrevUpdatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        var target = new TestLogModel();

        var result = source.CopyTo(target);

        result.Should().BeSameAs(target);
        target.Guid.Should().Be(source.Guid);
        target.CreatedAt.Should().Be(source.CreatedAt);
        target.UpdatedAt.Should().Be(source.UpdatedAt);
        target.PrevUpdatedAt.Should().Be(source.PrevUpdatedAt);
    }

    [Fact]
    public void LoadFrom_copies_guid()
    {
        var source = new TestModel { Guid = Guid.NewGuid() };
        var target = new TestModel();

        target.LoadFrom(source);

        target.Guid.Should().Be(source.Guid);
    }
}
