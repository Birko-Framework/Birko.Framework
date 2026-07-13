using System;
using System.Security.Cryptography;
using Birko.Security.Encryption;
using Birko.Security.Hashing;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// CR-M233/M234: the security-critical hashing + encryption surface was untested. CR-M233 specifically:
/// Pbkdf2PasswordHasher.Verify must be total over arbitrary stored strings (malformed salt/hash → false,
/// not FormatException).
/// </summary>
public class PasswordHasherAndEncryptionTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 10_000);

    [Fact]
    public void Hash_Then_Verify_RoundTrips()
    {
        var hash = _hasher.Hash("s3cret");
        _hasher.Verify("s3cret", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_False()
    {
        var hash = _hasher.Hash("s3cret");
        _hasher.Verify("wrong", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("WRONG-ALGO:10000:AAAA:BBBB")]        // wrong algorithm tag
    [InlineData("PBKDF2-SHA512:notanint:AAAA:BBBB")]  // non-integer iterations
    [InlineData("PBKDF2-SHA512:10000:!!!notbase64!!!:BBBB")] // CR-M233: non-base64 salt
    [InlineData("PBKDF2-SHA512:10000:AAAA:%%%notbase64%%%")] // CR-M233: non-base64 hash
    public void Verify_MalformedStoredHash_ReturnsFalse_NeverThrows(string stored)
    {
        _hasher.Invoking(h => h.Verify("pw", stored)).Should().NotThrow();
        _hasher.Verify("pw", stored).Should().BeFalse();
    }

    // ── AES-256-GCM ──────────────────────────────────────

    [Fact]
    public void Aes_EncryptDecrypt_RoundTrips()
    {
        var provider = new AesEncryptionProvider();
        var key = AesEncryptionProvider.GenerateKey();

        var cipher = provider.EncryptString("hello world", key);
        provider.DecryptString(cipher, key).Should().Be("hello world");
    }

    [Fact]
    public void GenerateKey_Produces256BitKey()
    {
        AesEncryptionProvider.GenerateKey().Should().HaveCount(32);
    }

    [Fact]
    public void Aes_Decrypt_WrongKey_Throws()
    {
        var provider = new AesEncryptionProvider();
        var cipher = provider.Encrypt(new byte[] { 1, 2, 3, 4 }, AesEncryptionProvider.GenerateKey());

        provider.Invoking(p => p.Decrypt(cipher, AesEncryptionProvider.GenerateKey()))
            .Should().Throw<CryptographicException>("GCM tag verification must fail for a wrong key");
    }

    [Fact]
    public void Aes_Decrypt_TamperedCiphertext_Throws()
    {
        var provider = new AesEncryptionProvider();
        var key = AesEncryptionProvider.GenerateKey();
        var cipher = provider.Encrypt(new byte[] { 1, 2, 3, 4, 5 }, key);
        cipher[^1] ^= 0xFF; // flip a ciphertext byte

        provider.Invoking(p => p.Decrypt(cipher, key)).Should().Throw<CryptographicException>();
    }
}
