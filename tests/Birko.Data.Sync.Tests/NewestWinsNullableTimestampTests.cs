using System;
using Birko.Data.Models;
using Birko.Data.Sync.Internal;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// CR-H098: SyncProviderBase.GetUpdatedAt only matched a non-nullable DateTime UpdatedAt, so any
/// model with a DateTime? timestamp returned null — silently degrading NewestWins to LocalWins.
/// It now matches both DateTime and DateTime?.
/// </summary>
public class NewestWinsNullableTimestampTests
{
    private class NullableStampModel : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
    }

    // SyncProviderBase has no abstract members and a protected ctor, so a thin probe can expose the
    // protected GetUpdatedAt for direct testing.
    private class Probe<T> : SyncProviderBase<T, TestSyncKnowledge> where T : AbstractModel
    {
        public DateTime? Read(T entity) => GetUpdatedAt(entity);
    }

    [Fact]
    public void GetUpdatedAt_ReadsNullableTimestamp()
    {
        var now = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var probe = new Probe<NullableStampModel>();

        probe.Read(new NullableStampModel { UpdatedAt = now }).Should().Be(now);
    }

    [Fact]
    public void GetUpdatedAt_NullableUnset_ReturnsNull()
    {
        var probe = new Probe<NullableStampModel>();
        probe.Read(new NullableStampModel { UpdatedAt = null }).Should().BeNull();
    }

    [Fact]
    public void GetUpdatedAt_StillReadsNonNullableTimestamp()
    {
        var now = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var probe = new Probe<TestSyncModel>();

        probe.Read(new TestSyncModel { UpdatedAt = now }).Should().Be(now);
    }
}
