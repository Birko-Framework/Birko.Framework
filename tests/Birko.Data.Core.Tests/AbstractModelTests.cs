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
    public void CopyTo_without_target_throws_instead_of_returning_self()
    {
        // Inverted (rule 56). CR-M082 replaced a null return — model.CopyTo().Guid NRE'd — with `return this`, and this
        // test pinned that. SH-M424 (TASK-323): returning `this` is an alias, not a copy — mutating the "clone" mutated
        // the source. The abstract base cannot allocate the concrete type, so it refuses instead.
        var source = new TestModel { Guid = Guid.NewGuid() };

        var act = () => source.CopyTo();

        act.Should().Throw<ArgumentNullException>().WithParameterName("clone");
    }

    [Fact]
    public void LogModel_CopyTo_without_target_throws_instead_of_returning_self()
    {
        // Inverted (rule 56) — the same CR-M082 → SH-M424 history in AbstractLogModel.CopyTo.
        var source = new TestLogModel
        {
            Guid = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
        };

        var act = () => source.CopyTo();

        act.Should().Throw<ArgumentNullException>().WithParameterName("clone");
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
