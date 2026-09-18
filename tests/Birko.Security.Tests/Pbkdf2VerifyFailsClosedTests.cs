using System;
using System.Security.Cryptography;
using Birko.Security.Hashing;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// SH-H039 (TASK-108): <see cref="Pbkdf2PasswordHasher.Verify"/> must fail closed on any stored hash that
/// is not well-formed.
///
/// A stored column of "PBKDF2-SHA512:600000::" authenticated <em>any</em> password: an empty segment is
/// valid Base64 (so the CR-M233 FormatException guard never fired), the derived key length was taken from
/// storedHash.Length so Pbkdf2 was asked for zero bytes, and FixedTimeEquals of two zero-length spans is
/// true. That length inversion is the root cause, and it also made a one-byte truncated column match an
/// arbitrary password roughly 1 in 256 — which is why the fix could not simply be "reject empty".
/// </summary>
public class Pbkdf2VerifyFailsClosedTests
{
    private const int Iterations = 10_000;
    private const int HashSize = 32;   // the algorithm's output length, mirrored from the hasher

    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: Iterations);

    /// <summary>Rebuilds a stored-hash string with its hash segment truncated to <paramref name="keptBytes"/>.</summary>
    private static string TruncateHashSegment(string stored, int keptBytes)
    {
        var parts = stored.Split(':');
        var hash = Convert.FromBase64String(parts[3]);
        parts[3] = Convert.ToBase64String(hash[..keptBytes]);
        return string.Join(':', parts);
    }

    /// <summary>Rebuilds a stored-hash string with one segment replaced.</summary>
    private static string WithSegment(string stored, int index, string value)
    {
        var parts = stored.Split(':');
        parts[index] = value;
        return string.Join(':', parts);
    }

    // ── The bypass itself ────────────────────────────────

    [Theory]
    [InlineData("anything")]
    [InlineData("")]
    [InlineData("a totally unrelated password")]
    public void Verify_AllEmptySegments_ReturnsFalse_ForAnyPassword(string password)
    {
        _hasher.Verify(password, "PBKDF2-SHA512:600000::").Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptyHashSegment_WithAGenuineSalt_ReturnsFalse()
    {
        var stored = WithSegment(_hasher.Hash("s3cret"), 3, string.Empty);

        _hasher.Verify("s3cret", stored).Should().BeFalse();
        _hasher.Verify("anything at all", stored).Should().BeFalse();
    }

    [Fact]
    public void Verify_EmptySaltSegment_ReturnsFalse()
    {
        var stored = WithSegment(_hasher.Hash("s3cret"), 2, string.Empty);

        _hasher.Verify("s3cret", stored).Should().BeFalse("an unsalted column is not verifiable");
    }

    // ── The derived length comes from the algorithm, not from the stored value ──

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(HashSize - 1)]
    public void Verify_TruncatedHashSegment_ReturnsFalse_EvenForTheCorrectPassword(int keptBytes)
    {
        // Asserts the length inversion directly. PBKDF2 output is prefix-stable, so deriving keptBytes
        // bytes of the CORRECT password yields exactly this truncated segment — under a
        // storedHash.Length-driven derivation every row here returns true.
        var truncated = TruncateHashSegment(_hasher.Hash("s3cret"), keptBytes);

        _hasher.Verify("s3cret", truncated).Should().BeFalse();
    }

    [Fact]
    public void Verify_OneByteHashSegment_DoesNotAuthenticateAnUnrelatedPassword()
    {
        // The ~1-in-256 case, made deterministic: craft the one-byte column so it matches "attacker"
        // rather than waiting for a collision. Under a stored-length-driven derivation this is a true.
        var parts = _hasher.Hash("s3cret").Split(':');
        var salt = Convert.FromBase64String(parts[2]);
        var oneByte = Rfc2898DeriveBytes.Pbkdf2("attacker", salt, Iterations, HashAlgorithmName.SHA512, 1);
        var crafted = $"{parts[0]}:{parts[1]}:{parts[2]}:{Convert.ToBase64String(oneByte)}";

        _hasher.Verify("attacker", crafted).Should().BeFalse();
    }

    [Fact]
    public void Verify_OverlongHashSegment_ReturnsFalse()
    {
        // The length is pinned to the algorithm's output size in both directions, not merely "non-empty".
        var stored = WithSegment(_hasher.Hash("s3cret"), 3, Convert.ToBase64String(new byte[HashSize + 32]));

        _hasher.Verify("s3cret", stored).Should().BeFalse();
    }

    // ── A corrupt iteration count fails, and does not throw out of the login path ──

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-600000")]
    [InlineData("notanint")]
    [InlineData("")]
    [InlineData("999999999999999")]   // overflows int.TryParse
    public void Verify_CorruptIterationSegment_ReturnsFalse_AndThrowsNothing(string iterations)
    {
        var stored = WithSegment(_hasher.Hash("s3cret"), 1, iterations);

        _hasher.Invoking(h => h.Verify("s3cret", stored)).Should().NotThrow();
        _hasher.Verify("s3cret", stored).Should().BeFalse();
    }

    // ── Shape guards (back-compat: these already held before the fix) ──

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData(":::")]
    [InlineData("PBKDF2-SHA512:10000:AAAA")]                    // three segments
    [InlineData("PBKDF2-SHA512:10000:AAAA:BBBB:CCCC")]          // five segments
    [InlineData("PBKDF2-SHA256:10000:AAAA:BBBB")]               // unknown algorithm
    [InlineData("PBKDF2-SHA512:10000:!!!notbase64!!!:BBBB")]    // non-base64 salt
    [InlineData("PBKDF2-SHA512:10000:AAAA:%%%notbase64%%%")]    // non-base64 hash
    public void Verify_MalformedShape_ReturnsFalse_AndThrowsNothing(string stored)
    {
        _hasher.Invoking(h => h.Verify("pw", stored)).Should().NotThrow();
        _hasher.Verify("pw", stored).Should().BeFalse();
    }

    // ── The genuine path is untouched (back-compat) ──

    [Fact]
    public void Verify_GenuineRoundTrip_StillSucceeds()
    {
        var stored = _hasher.Hash("s3cret");

        _hasher.Verify("s3cret", stored).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_AgainstAGenuineHash_StillReturnsFalse()
    {
        var stored = _hasher.Hash("s3cret");

        _hasher.Verify("wrong", stored).Should().BeFalse();
    }

    [Fact]
    public void Verify_AHashWrittenAtADifferentIterationCount_StillVerifies()
    {
        // Iterations are read from the column, so a hash written before an iteration bump must still
        // verify — the new guard rejects only non-positive counts, not merely different ones.
        var stored = new Pbkdf2PasswordHasher(iterations: 20_000).Hash("s3cret");

        _hasher.Verify("s3cret", stored).Should().BeTrue();
    }
}
