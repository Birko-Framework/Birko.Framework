using FluentAssertions;
using Birko.Security.NFC;

namespace Birko.Security.NFC.Tests;

public class InMemoryNfcTagMappingStoreTests
{
    private readonly InMemoryNfcTagMappingStore _store = new();

    [Fact]
    public async Task AddAsync_And_GetByTagUidAsync_RoundTrip()
    {
        var mapping = new NfcTagMapping { TagUid = "AABB", UserId = Guid.NewGuid() };
        await _store.AddAsync(mapping);

        var retrieved = await _store.GetByTagUidAsync("AABB");
        retrieved.Should().NotBeNull();
        retrieved!.UserId.Should().Be(mapping.UserId);
    }

    [Fact]
    public async Task AddAsync_DuplicateKey_Throws()
    {
        await _store.AddAsync(new NfcTagMapping { TagUid = "AABB" });
        var act = async () => await _store.AddAsync(new NfcTagMapping { TagUid = "AABB" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetByTagUidAsync_NotFound_ReturnsNull()
    {
        var result = await _store.GetByTagUidAsync("NOPE");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsAllForUser()
    {
        var userId = Guid.NewGuid();
        await _store.AddAsync(new NfcTagMapping { TagUid = "A1", UserId = userId });
        await _store.AddAsync(new NfcTagMapping { TagUid = "A2", UserId = userId });
        await _store.AddAsync(new NfcTagMapping { TagUid = "B1", UserId = Guid.NewGuid() });

        var results = await _store.GetByUserIdAsync(userId);
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesExisting()
    {
        var mapping = new NfcTagMapping { TagUid = "AABB", IsActive = true };
        await _store.AddAsync(mapping);

        mapping.IsActive = false;
        await _store.UpdateAsync(mapping);

        var retrieved = await _store.GetByTagUidAsync("AABB");
        retrieved!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesMapping()
    {
        await _store.AddAsync(new NfcTagMapping { TagUid = "AABB" });
        await _store.DeleteAsync("AABB");

        var result = await _store.GetByTagUidAsync("AABB");
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NotFound_DoesNotThrow()
    {
        var act = async () => await _store.DeleteAsync("NOPE");
        await act.Should().NotThrowAsync();
    }
}
