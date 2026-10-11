using System;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// SH-M422 / SH-M423 (TASK-323) — <c>AbstractLogModel</c>'s <c>CopyTo</c> and <c>LoadFrom</c> were overloads of the
/// <c>AbstractModel</c> virtuals, not overrides. Called through a base-typed reference — <c>AbstractModel</c>,
/// <c>ICopyable&lt;AbstractModel&gt;</c>, <c>ILoadable&lt;IGuidEntity&gt;</c> — they bound to the base, which copies only
/// the <c>Guid</c>, so <c>CreatedAt</c> / <c>UpdatedAt</c> / <c>PrevUpdatedAt</c> stayed at <c>0001-01-01</c>. Both are
/// overrides now. SH-M424 — a null target returned <c>this</c>, so the "copy" aliased the source; the abstract bases
/// now refuse it.
///
/// <para>Observed state: the timestamps read back off the target, and object identity.</para>
/// </summary>
public class LogModelDispatchTests
{
    private sealed class LogModel : AbstractLogModel { }

    /// <summary>A derived model that overrides the virtual and calls base with its own type — the recursion shape.</summary>
    private sealed class AllocatingLogModel : AbstractLogModel
    {
        public string Name { get; set; } = "";

        public override AbstractModel CopyTo(AbstractModel? clone = null)
        {
            var target = clone as AllocatingLogModel ?? new AllocatingLogModel();
            base.CopyTo(target);
            target.Name = Name;
            return target;
        }
    }

    private sealed class LogEntity : ILogEntity
    {
        public Guid? Guid { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? PrevUpdatedAt { get; set; }
    }

    private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Updated = new(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PrevUpdated = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    private static LogModel Source() => new()
    {
        Guid = Guid.NewGuid(), CreatedAt = Created, UpdatedAt = Updated, PrevUpdatedAt = PrevUpdated,
    };

    [Fact]
    public void CopyTo_through_an_AbstractModel_reference_keeps_the_timestamps()
    {
        AbstractModel source = Source();
        var target = new LogModel();

        source.CopyTo(target);

        target.CreatedAt.Should().Be(Created);
        target.UpdatedAt.Should().Be(Updated);
        target.PrevUpdatedAt.Should().Be(PrevUpdated);
    }

    [Fact]
    public void CopyTo_through_ICopyable_of_AbstractModel_keeps_the_timestamps()
    {
        ICopyable<AbstractModel> source = Source();
        var target = new LogModel();

        source.CopyTo(target);

        target.CreatedAt.Should().Be(Created);
        target.UpdatedAt.Should().Be(Updated);
    }

    [Fact]
    public void LoadFrom_through_ILoadable_of_IGuidEntity_keeps_the_timestamps()
    {
        ILoadable<IGuidEntity> target = new LogModel();
        var source = new LogEntity { Guid = Guid.NewGuid(), CreatedAt = Created, UpdatedAt = Updated, PrevUpdatedAt = PrevUpdated };

        target.LoadFrom(source);

        var loaded = (LogModel)target;
        loaded.Guid.Should().Be(source.Guid);
        loaded.CreatedAt.Should().Be(Created);
        loaded.UpdatedAt.Should().Be(Updated);
        loaded.PrevUpdatedAt.Should().Be(PrevUpdated);
    }

    [Fact]
    public void A_null_target_is_refused_through_a_base_reference_too()
    {
        AbstractModel source = Source();

        var act = () => source.CopyTo(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void An_overriding_model_that_calls_base_with_its_own_type_copies_and_does_not_recurse()
    {
        // Contract pin for the shape the fix had to avoid: base.CopyTo(target) with a derived-typed target binds to the
        // typed overload, which must not dispatch back to this override.
        var source = new AllocatingLogModel { Guid = Guid.NewGuid(), CreatedAt = Created, Name = "n" };

        var copy = (AllocatingLogModel)((AbstractModel)source).CopyTo();

        copy.Should().NotBeSameAs(source);
        copy.Name.Should().Be("n");
        copy.CreatedAt.Should().Be(Created);
    }

    [Fact]
    public void LoadFrom_a_plain_guid_entity_leaves_the_timestamps_alone()
    {
        // Contract pin: only a source that carries timestamps overwrites them.
        var target = Source();
        ILoadable<IGuidEntity> loadable = target;
        var guid = Guid.NewGuid();

        loadable.LoadFrom(new LogModelDispatchTests.GuidOnly { Guid = guid });

        target.Guid.Should().Be(guid);
        target.CreatedAt.Should().Be(Created);
    }

    private sealed class GuidOnly : IGuidEntity
    {
        public Guid? Guid { get; set; }
    }
}
