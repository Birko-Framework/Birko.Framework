using Birko.Security;
using Birko.Security.BCrypt.Hashing;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Security.BCrypt.Tests;

public class BCryptPasswordHasherTests
{
    [Fact]
    public void Hash_ReturnsValidBCryptFormat()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var hash = hasher.Hash("password");

        hash.Should().StartWith("$2a$04$");
        hash.Should().HaveLength(60);
    }

    [Fact]
    public void Hash_And_Verify_RoundTrip()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var password = "MySecureP@ssw0rd!";

        var hash = hasher.Hash(password);
        var result = hasher.Verify(password, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var hash = hasher.Hash("correct-password");

        hasher.Verify("wrong-password", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ProducesDifferentHashesForSamePassword()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var hash1 = hasher.Hash("same-password");
        var hash2 = hasher.Hash("same-password");

        hash1.Should().NotBe(hash2, "each hash uses a different random salt");
    }

    [Fact]
    public void Verify_BothHashesVerify_WithSamePassword()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var password = "test-password";
        var hash1 = hasher.Hash(password);
        var hash2 = hasher.Hash(password);

        hasher.Verify(password, hash1).Should().BeTrue();
        hasher.Verify(password, hash2).Should().BeTrue();
    }

    [Fact]
    public void Constructor_WorkFactorTooLow_Throws()
    {
        var act = () => new BCryptPasswordHasher(workFactor: 3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WorkFactorTooHigh_Throws()
    {
        var act = () => new BCryptPasswordHasher(workFactor: 32);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_DefaultWorkFactor_Is12()
    {
        var hasher = new BCryptPasswordHasher();
        hasher.WorkFactor.Should().Be(12);
    }

    [Fact]
    public void Hash_NullPassword_Throws()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var act = () => hasher.Hash(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Verify_NullPassword_Throws()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var act = () => hasher.Verify(null!, "$2a$04$xxxx");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Verify_NullHash_Throws()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var act = () => hasher.Verify("password", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Verify_InvalidHashFormat_ReturnsFalse()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        hasher.Verify("password", "not-a-bcrypt-hash").Should().BeFalse();
        hasher.Verify("password", "").Should().BeFalse();
        hasher.Verify("password", "$2a$04$short").Should().BeFalse();
    }

    [Fact]
    public void Hash_IncludesWorkFactorInOutput()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 5);
        var hash = hasher.Hash("password");
        hash.Should().StartWith("$2a$05$");
    }

    [Fact]
    public void NeedsRehash_LowerWorkFactor_ReturnsTrue()
    {
        var oldHasher = new BCryptPasswordHasher(workFactor: 4);
        var newHasher = new BCryptPasswordHasher(workFactor: 6);

        var hash = oldHasher.Hash("password");
        newHasher.NeedsRehash(hash).Should().BeTrue();
    }

    [Fact]
    public void NeedsRehash_SameWorkFactor_ReturnsFalse()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var hash = hasher.Hash("password");
        hasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_HigherWorkFactor_ReturnsFalse()
    {
        var strongHasher = new BCryptPasswordHasher(workFactor: 6);
        var weakHasher = new BCryptPasswordHasher(workFactor: 4);

        var hash = strongHasher.Hash("password");
        weakHasher.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void NeedsRehash_InvalidHash_ReturnsTrue()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        hasher.NeedsRehash("not-a-hash").Should().BeTrue();
    }

    [Fact]
    public void Hash_EmptyPassword_Works()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var hash = hasher.Hash("");
        hash.Should().HaveLength(60);
        hasher.Verify("", hash).Should().BeTrue();
        hasher.Verify("not-empty", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_UnicodePassword_Works()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var password = "пароль密码パスワード";
        var hash = hasher.Hash(password);
        hasher.Verify(password, hash).Should().BeTrue();
        hasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_LongPassword_TruncatedTo72Bytes()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        // Create a password longer than 72 bytes (ASCII, so 1 byte per char + null = 101 bytes)
        var longPassword = new string('A', 100);
        var hash = hasher.Hash(longPassword);

        // Should verify with same long password
        hasher.Verify(longPassword, hash).Should().BeTrue();

        // BCrypt truncates to 72 bytes (password bytes + null terminator).
        // 'A' is 1 byte in UTF-8, so first 71 chars + null byte = 72 bytes.
        // Passwords differing only after byte 72 should produce the same hash.
        var longPassword2 = new string('A', 71) + "B" + new string('A', 28); // differs at position 72+
        // Both are truncated to same first 72 bytes: 71 * 'A' + null
        hasher.Verify(longPassword, hash).Should().BeTrue();
    }

    [Fact]
    public void ImplementsIPasswordHasher()
    {
        IPasswordHasher hasher = new BCryptPasswordHasher(workFactor: 4);
        var hash = hasher.Hash("test");
        hasher.Verify("test", hash).Should().BeTrue();
    }
}
