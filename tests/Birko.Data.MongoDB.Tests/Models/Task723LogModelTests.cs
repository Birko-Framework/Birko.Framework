using System;
using Birko.Data.Models;
using Birko.Data.MongoDB.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Models;

/// <summary>
/// TASK-723. Measured before the fix: <c>MongoDBLogModel</c> declared CreatedAt/UpdatedAt/PrevUpdatedAt but not
/// <c>ITimestamped</c>, so the timestamp decorator (which selects on it) never stamped one; and copying one Mongo log
/// model onto another kept only the Guid, as its timestamp copy accepted an <c>AbstractLogModel</c> target alone.
/// </summary>
public class Task723LogModelTests
{
    private sealed class Entry : MongoDBLogModel { }

    private static Entry Stamped() => new()
    {
        Guid = Guid.NewGuid(),
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        PrevUpdatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void A_mongo_log_model_is_timestamped()
    {
        typeof(ITimestamped).IsAssignableFrom(typeof(Entry)).Should().BeTrue();
        typeof(ILogEntity).IsAssignableFrom(typeof(Entry)).Should().BeTrue();
    }

    [Fact]
    public void Copying_one_mongo_log_model_onto_another_keeps_its_timestamps()
    {
        var source = Stamped();
        var target = new Entry();

        source.CopyTo((AbstractModel)target);

        target.Guid.Should().Be(source.Guid);
        target.CreatedAt.Should().Be(source.CreatedAt);
        target.UpdatedAt.Should().Be(source.UpdatedAt);
        target.PrevUpdatedAt.Should().Be(source.PrevUpdatedAt);
    }

    [Fact]
    public void Loading_from_another_log_entity_keeps_its_timestamps()
    {
        var source = Stamped();
        var target = new Entry();

        target.LoadFrom((IGuidEntity)source);

        target.UpdatedAt.Should().Be(source.UpdatedAt);
        target.PrevUpdatedAt.Should().Be(source.PrevUpdatedAt);
    }
}
