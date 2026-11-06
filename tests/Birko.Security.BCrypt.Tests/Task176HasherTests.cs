using System;
using System.Diagnostics;
using Birko.Security.BCrypt.Hashing;
using Birko.Security.Hashing;
using FluentAssertions;
using Xunit;

namespace Birko.Security.BCrypt.Tests;

/// <summary>
/// TASK-176 — the hashers' low findings: SH-L224 (a "$2y$" hash, PHP's name for "$2b$", was rejected), SH-L225 (Verify ran
/// whatever cost the stored hash named, up to 31) and SH-L226 (NeedsRehash was not on IPasswordHasher and PBKDF2 had
/// none, so a weak stored hash could not be detected through the interface).
/// </summary>
public class Task176HasherTests
{
    // ── SH-L224 ──

    [Fact]
    public void A_2y_hash_verifies_as_its_2b_twin()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var made = hasher.Hash("correct horse");                 // "$2a$04$..."
        var asPhp = "$2y$" + made[4..];

        hasher.Verify("correct horse", asPhp).Should().BeTrue("$2y$ and $2a$/$2b$ hash identically for inputs under 72 bytes");
        hasher.Verify("wrong", asPhp).Should().BeFalse();
        hasher.NeedsRehash(asPhp).Should().BeFalse("a $2y$ hash at the configured cost is current, not an unknown format");
    }

    // ── SH-L225 ──

    [Fact]
    public void A_stored_cost_above_the_ceiling_is_refused_without_the_work()
    {
        var hasher = new BCryptPasswordHasher(); // work factor 12, ceiling 16
        var hostile = "$2a$18$" + new BCryptPasswordHasher(workFactor: 4).Hash("x")[7..]; // 2^18 rounds: ~16 s of CPU

        var clock = Stopwatch.StartNew();
        var verified = hasher.Verify("x", hostile);

        verified.Should().BeFalse();
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "a cost above the ceiling is refused before any key schedule");
    }

    [Fact]
    public void A_hash_at_the_default_cost_still_verifies() // contract pin: the ceiling leaves ordinary hashes alone
    {
        var hasher = new BCryptPasswordHasher(workFactor: 10);
        hasher.Verify("pw", hasher.Hash("pw")).Should().BeTrue();
    }

    // ── SH-L226 ──

    [Fact]
    public void Pbkdf2_reports_a_weaker_or_foreign_hash_through_the_interface()
    {
        IPasswordHasher strong = new Pbkdf2PasswordHasher(iterations: 20_000);
        var weakHash = new Pbkdf2PasswordHasher(iterations: 10_000).Hash("pw");

        strong.NeedsRehash(weakHash).Should().BeTrue("fewer iterations than the hasher's");
        strong.Verify("pw", weakHash).Should().BeTrue("it still verifies, so the caller can upgrade it");
        strong.NeedsRehash(strong.Hash("pw")).Should().BeFalse();
        strong.NeedsRehash("$2a$12$not-pbkdf2").Should().BeTrue("not this hasher's format");
    }

    [Fact]
    public void BCrypt_answers_NeedsRehash_through_the_interface_too()
    {
        IPasswordHasher hasher = new BCryptPasswordHasher(workFactor: 5);
        hasher.NeedsRehash(new BCryptPasswordHasher(workFactor: 4).Hash("pw")).Should().BeTrue();
        hasher.NeedsRehash(hasher.Hash("pw")).Should().BeFalse();
    }
}
