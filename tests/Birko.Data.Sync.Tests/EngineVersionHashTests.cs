using Birko.Data.Models;
using Birko.Data.Sync.Internal;
using Birko.Data.Sync.Tests.TestInfrastructure;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.Sync.Tests;

/// <summary>
/// TASK-630: the content hash the tenant provider used for an entity with no <c>UpdatedAt</c> (CR-H104), moved into the
/// shared engine with the tenant provider's rebuild. These are its tests, carried over, plus the engine-specific rule
/// that a hash is never empty (an empty version reads as "this side is absent" in every knowledge store).
/// </summary>
public class EngineVersionHashTests
{
    private class Timestamped : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private class NullableTimestamped : AbstractModel
    {
        public string? Name { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    private class NoTimestamp : AbstractModel
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private sealed class Probe<T> : SyncProviderBase<T, TestSyncKnowledge> where T : AbstractModel
    {
        public string? Hash(T entity) => GetVersionHash(entity);
        public DateTime? UpdatedAt(T entity) => GetUpdatedAt(entity);
    }

    private static string? Hash<T>(T entity) where T : AbstractModel => new Probe<T>().Hash(entity);

    [Fact]
    public void Uses_UpdatedAt_when_present()
    {
        var ts = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Hash(new Timestamped { UpdatedAt = ts }).Should().Be(ts.ToString("O"));
    }

    [Fact]
    public void Uses_a_nullable_UpdatedAt_when_set()
    {
        var ts = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        Hash(new NullableTimestamped { UpdatedAt = ts }).Should().Be(ts.ToString("O"));
    }

    [Fact]
    public void With_no_timestamp_equal_content_hashes_equal()
    {
        var guid = Guid.NewGuid();
        var a = Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 7 });
        var b = Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 7 });

        a.Should().NotBeNullOrEmpty();
        a.Should().Be(b, "equal content must yield the same version, or an unchanged row counts as changed every run");
    }

    [Fact]
    public void With_no_timestamp_the_hash_changes_with_content()
    {
        var guid = Guid.NewGuid();
        Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 7 })
            .Should().NotBe(Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 8 }));
    }

    [Fact]
    public void A_null_UpdatedAt_falls_back_to_the_content_hash()
    {
        var guid = Guid.NewGuid();
        Hash(new NullableTimestamped { Guid = guid, Name = "x" })
            .Should().Be(Hash(new NullableTimestamped { Guid = guid, Name = "x" }));
    }

    [Fact]
    public void GetUpdatedAt_reads_DateTime_and_nullable_and_absent()
    {
        var ts = new DateTime(2026, 7, 15, 1, 2, 3, DateTimeKind.Utc);
        new Probe<Timestamped>().UpdatedAt(new Timestamped { UpdatedAt = ts }).Should().Be(ts);
        new Probe<NullableTimestamped>().UpdatedAt(new NullableTimestamped { UpdatedAt = ts }).Should().Be(ts);
        new Probe<NullableTimestamped>().UpdatedAt(new NullableTimestamped { UpdatedAt = null }).Should().BeNull();
        new Probe<NoTimestamp>().UpdatedAt(new NoTimestamp { Value = 5 }).Should().BeNull();
    }
}
