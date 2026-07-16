using FluentAssertions;
using Birko.Security.NFC;

namespace Birko.Security.NFC.Tests;

public class NfcAuthProviderTests
{
    private readonly InMemoryNfcTagMappingStore _store = new();
    private readonly NfcAuthProvider _auth;
    private readonly Guid _userId = Guid.NewGuid();

    public NfcAuthProviderTests()
    {
        _auth = new NfcAuthProvider(_store);
    }

    // ── Constructor ──

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new NfcAuthProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Enroll ──

    [Fact]
    public async Task EnrollAsync_ValidTag_CreatesMapping()
    {
        var mapping = await _auth.EnrollAsync(_userId, "04A1B2C3", "Office badge", "John", "john@test.com");

        mapping.Should().NotBeNull();
        mapping.TagUid.Should().Be("04A1B2C3");
        mapping.UserId.Should().Be(_userId);
        mapping.Label.Should().Be("Office badge");
        mapping.UserName.Should().Be("John");
        mapping.Email.Should().Be("john@test.com");
        mapping.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task EnrollAsync_EmptyUid_Throws()
    {
        var act = async () => await _auth.EnrollAsync(_userId, "");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnrollAsync_DuplicateActiveTag_Throws()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");

        var act = async () => await _auth.EnrollAsync(Guid.NewGuid(), "04A1B2C3");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EnrollAsync_RevokedTag_CanReenroll()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        await _auth.RevokeAsync("04A1B2C3");

        // Should succeed because the old mapping is inactive
        var newUserId = Guid.NewGuid();
        var mapping = await _auth.EnrollAsync(newUserId, "04A1B2C3");
        mapping.UserId.Should().Be(newUserId);
    }

    [Fact]
    public async Task EnrollAsync_MaxTagsExceeded_Throws()
    {
        var settings = new NfcAuthSettings { MaxTagsPerUser = 2 };
        var auth = new NfcAuthProvider(_store, settings);

        await auth.EnrollAsync(_userId, "AAAA");
        await auth.EnrollAsync(_userId, "BBBB");

        var act = async () => await auth.EnrollAsync(_userId, "CCCC");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*max: 2*");
    }

    [Fact]
    public async Task EnrollAsync_MaxTagsZero_Unlimited()
    {
        var settings = new NfcAuthSettings { MaxTagsPerUser = 0 };
        var auth = new NfcAuthProvider(_store, settings);

        for (int i = 0; i < 10; i++)
        {
            await auth.EnrollAsync(_userId, $"TAG{i:X4}");
        }

        var tags = await auth.GetUserTagsAsync(_userId);
        tags.Should().HaveCount(10);
    }

    // ── Authenticate ──

    [Fact]
    public async Task AuthenticateAsync_EnrolledTag_ReturnsSuccess()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3", userName: "John", email: "john@test.com");

        var result = await _auth.AuthenticateAsync("04A1B2C3");

        result.IsAuthenticated.Should().BeTrue();
        result.UserId.Should().Be(_userId);
        result.UserName.Should().Be("John");
        result.Email.Should().Be("john@test.com");
        result.TagUid.Should().Be("04A1B2C3");
        result.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_UnknownTag_ReturnsFailure()
    {
        var result = await _auth.AuthenticateAsync("DEADBEEF");

        result.IsAuthenticated.Should().BeFalse();
        result.UserId.Should().BeNull();
        result.Error.Should().Contain("not registered");
    }

    [Fact]
    public async Task AuthenticateAsync_RevokedTag_ReturnsFailure()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        await _auth.RevokeAsync("04A1B2C3");

        var result = await _auth.AuthenticateAsync("04A1B2C3");

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Contain("revoked");
    }

    [Fact]
    public async Task AuthenticateAsync_EmptyUid_ReturnsFailure()
    {
        var result = await _auth.AuthenticateAsync("");

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Contain("empty");
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredTag_ReturnsFailure()
    {
        var mapping = await _auth.EnrollAsync(_userId, "04A1B2C3");
        mapping.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await _store.UpdateAsync(mapping);

        var result = await _auth.AuthenticateAsync("04A1B2C3");

        result.IsAuthenticated.Should().BeFalse();
        result.Error.Should().Contain("expired");
    }

    [Fact]
    public async Task AuthenticateAsync_ExpiredTagExpirationDisabled_ReturnsSuccess()
    {
        var settings = new NfcAuthSettings { EnforceExpiration = false };
        var auth = new NfcAuthProvider(_store, settings);

        var mapping = await auth.EnrollAsync(_userId, "EXPIRED1");
        mapping.ExpiresAt = DateTime.UtcNow.AddHours(-1);
        await _store.UpdateAsync(mapping);

        var result = await auth.AuthenticateAsync("EXPIRED1");

        result.IsAuthenticated.Should().BeTrue();
    }

    // ── UID Normalization ──

    [Fact]
    public async Task AuthenticateAsync_NormalizesUid_CaseInsensitive()
    {
        await _auth.EnrollAsync(_userId, "04a1b2c3");
        var result = await _auth.AuthenticateAsync("04A1B2C3");
        result.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_NormalizesUid_RemovesSeparators()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        var result = await _auth.AuthenticateAsync("04:A1:B2:C3");
        result.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task AuthenticateAsync_NormalizesUid_RemovesDashes()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        var result = await _auth.AuthenticateAsync("04-A1-B2-C3");
        result.IsAuthenticated.Should().BeTrue();
    }

    // ── Usage Tracking ──

    [Fact]
    public async Task AuthenticateAsync_TracksLastUsed()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        var before = DateTime.UtcNow;

        await _auth.AuthenticateAsync("04A1B2C3");

        var mapping = await _auth.GetTagMappingAsync("04A1B2C3");
        mapping!.LastUsedAt.Should().NotBeNull();
        mapping.LastUsedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public async Task AuthenticateAsync_TrackingDisabled_DoesNotUpdateLastUsed()
    {
        var settings = new NfcAuthSettings { TrackUsage = false };
        var auth = new NfcAuthProvider(_store, settings);

        await auth.EnrollAsync(_userId, "NOTRACK1");
        await auth.AuthenticateAsync("NOTRACK1");

        var mapping = await auth.GetTagMappingAsync("NOTRACK1");
        mapping!.LastUsedAt.Should().BeNull();
    }

    // ── Revoke ──

    [Fact]
    public async Task RevokeAsync_DeactivatesTag()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        await _auth.RevokeAsync("04A1B2C3");

        var mapping = await _auth.GetTagMappingAsync("04A1B2C3");
        mapping!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAsync_UnknownTag_DoesNotThrow()
    {
        var act = async () => await _auth.RevokeAsync("UNKNOWN1");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeAllAsync_DeactivatesAllUserTags()
    {
        await _auth.EnrollAsync(_userId, "TAG00001");
        await _auth.EnrollAsync(_userId, "TAG00002");
        await _auth.EnrollAsync(_userId, "TAG00003");

        await _auth.RevokeAllAsync(_userId);

        var tags = await _auth.GetUserTagsAsync(_userId);
        tags.Should().BeEmpty(); // GetUserTagsAsync only returns active tags
    }

    // ── Query ──

    [Fact]
    public async Task GetUserTagsAsync_ReturnsOnlyActiveTags()
    {
        await _auth.EnrollAsync(_userId, "ACTIVE01");
        await _auth.EnrollAsync(_userId, "ACTIVE02");
        await _auth.EnrollAsync(_userId, "REVOKED1");
        await _auth.RevokeAsync("REVOKED1");

        var tags = await _auth.GetUserTagsAsync(_userId);

        tags.Should().HaveCount(2);
        tags.Should().OnlyContain(t => t.IsActive);
    }

    [Fact]
    public async Task IsEnrolledAsync_ActiveTag_ReturnsTrue()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        var enrolled = await _auth.IsEnrolledAsync("04A1B2C3");
        enrolled.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnrolledAsync_RevokedTag_ReturnsFalse()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3");
        await _auth.RevokeAsync("04A1B2C3");

        var enrolled = await _auth.IsEnrolledAsync("04A1B2C3");
        enrolled.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnrolledAsync_UnknownTag_ReturnsFalse()
    {
        var enrolled = await _auth.IsEnrolledAsync("NOPE0001");
        enrolled.Should().BeFalse();
    }

    [Fact]
    public async Task GetTagMappingAsync_ReturnsMapping()
    {
        await _auth.EnrollAsync(_userId, "04A1B2C3", "Test card");
        var mapping = await _auth.GetTagMappingAsync("04A1B2C3");

        mapping.Should().NotBeNull();
        mapping!.Label.Should().Be("Test card");
    }

    [Fact]
    public async Task GetTagMappingAsync_UnknownTag_ReturnsNull()
    {
        var mapping = await _auth.GetTagMappingAsync("NOPE0001");
        mapping.Should().BeNull();
    }

    // ── Claims population (CR-L345) ──

    [Fact]
    public async Task AuthenticateAsync_Success_PopulatesClaims_WithoutTokenIssuance()
    {
        // CR-L345: Claims must be populated on a successful auth even when no token provider is configured.
        await _auth.EnrollAsync(_userId, "04A1B2C3", userName: "John", email: "john@test.com");

        var result = await _auth.AuthenticateAsync("04A1B2C3");

        result.IsAuthenticated.Should().BeTrue();
        result.Token.Should().BeNull(); // default provider issues no token
        result.Claims.Should().Contain("sub", _userId.ToString());
        result.Claims.Should().Contain("nfc_uid", "04A1B2C3");
        result.Claims.Should().Contain("auth_method", "nfc");
        result.Claims.Should().Contain("email", "john@test.com");
        result.Claims.Should().Contain("name", "John");
    }

    [Fact]
    public async Task AuthenticateAsync_Success_OmitsOptionalClaimsWhenAbsent()
    {
        // No userName/email enrolled → those optional claims are not added, but the core ones are.
        await _auth.EnrollAsync(_userId, "04A1B2C3");

        var result = await _auth.AuthenticateAsync("04A1B2C3");

        result.Claims.Keys.Should().BeEquivalentTo(new[] { "sub", "nfc_uid", "auth_method" });
    }

    // ── Concurrent-enroll collision (CR-L346) ──

    [Fact]
    public async Task EnrollAsync_AddRaceCollision_ThrowsFriendlyAlreadyEnrolled()
    {
        // CR-L346: simulate the TOCTOU window — GetByTagUidAsync reports "not enrolled" (null) so the pre-check
        // passes, but AddAsync loses the race and throws the low-level store collision. EnrollAsync must
        // normalize that to the friendly "already enrolled" message rather than leaking the store's wording.
        var store = new AddCollisionStore();
        var auth = new NfcAuthProvider(store);

        var act = async () => await auth.EnrollAsync(_userId, "04A1B2C3");

        (await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already enrolled*"))
            .And.InnerException.Should().BeOfType<InvalidOperationException>(); // original store error preserved
    }

    /// <summary>Store stub whose GetByTagUidAsync reports absent but whose AddAsync always collides.</summary>
    private sealed class AddCollisionStore : INfcTagMappingStore
    {
        public Task<NfcTagMapping?> GetByTagUidAsync(string tagUid, CancellationToken cancellationToken = default)
            => Task.FromResult<NfcTagMapping?>(null);

        public Task<IReadOnlyList<NfcTagMapping>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<NfcTagMapping>>(new List<NfcTagMapping>());

        public Task AddAsync(NfcTagMapping mapping, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException($"Tag {mapping.TagUid} already exists in the store.");

        public Task UpdateAsync(NfcTagMapping mapping, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(string tagUid, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
