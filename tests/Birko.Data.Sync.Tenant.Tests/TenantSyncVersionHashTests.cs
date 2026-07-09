using System;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Sync.Tenant.Providers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>
/// CR-H104: TenantSyncProvider.GetVersionHash returned `updatedAt?.ToString("O") ?? Guid.NewGuid()`,
/// so for entities without a DateTime UpdatedAt every call produced a fresh, non-deterministic
/// version — recorded versions never matched across syncs and Preview/Sync were non-reproducible.
/// It now uses the timestamp when present (including DateTime?) and a deterministic content hash
/// otherwise.
/// </summary>
public class TenantSyncVersionHashTests
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

    private static string? Hash<T>(T entity) where T : AbstractModel
        => TenantSyncProvider<AsyncInMemoryStore<T>, T>.GetVersionHash(entity);

    [Fact]
    public void UsesUpdatedAt_WhenPresent()
    {
        var ts = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Hash(new Timestamped { UpdatedAt = ts }).Should().Be(ts.ToString("O"));
    }

    [Fact]
    public void UsesNullableUpdatedAt_WhenSet()
    {
        var ts = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);
        Hash(new NullableTimestamped { UpdatedAt = ts }).Should().Be(ts.ToString("O"));
    }

    [Fact]
    public void NoTimestamp_IsDeterministic_ForEqualContent()
    {
        var guid = Guid.NewGuid();
        var a = Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 7 });
        var b = Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 7 });

        a.Should().NotBeNullOrEmpty();
        a.Should().Be(b, "equal content must yield the same version hash (not a random GUID)");
    }

    [Fact]
    public void NoTimestamp_ChangesWithContent()
    {
        var guid = Guid.NewGuid();
        var a = Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 7 });
        var b = Hash(new NoTimestamp { Guid = guid, Name = "x", Value = 8 });

        a.Should().NotBe(b);
    }

    [Fact]
    public void NoTimestamp_HashIsNotAGuid()
    {
        var hash = Hash(new NoTimestamp { Guid = Guid.NewGuid(), Name = "x" });
        Guid.TryParse(hash, out _).Should().BeFalse("the fallback must be a content hash, not a GUID");
    }
}
