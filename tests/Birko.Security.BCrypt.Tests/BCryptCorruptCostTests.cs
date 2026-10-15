using Birko.Security.BCrypt.Hashing;
using FluentAssertions;
using Xunit;

namespace Birko.Security.BCrypt.Tests;

/// <summary>
/// SH-M247 (TASK-165): a stored hash shaped like BCrypt but with an unusable cost made Verify throw
/// <c>ArgumentException</c>, where a corrupt stored value must answer false, as NeedsRehash and the PBKDF2 hasher do.
/// </summary>
public class BCryptCorruptCostTests
{
    // 53 characters of the BCrypt-Base64 alphabet: a 22-char salt and a 31-char digest.
    private const string Body = "abcdefghijklmnopqrstuvABCDEFGHIJKLMNOPQRSTUVWXYZ01234";

    [Theory]
    [InlineData("99")] // above MaxWorkFactor
    [InlineData("32")]
    [InlineData("03")] // below MinWorkFactor
    [InlineData("ab")] // not a number
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData(" 5")]
    public void Verify_answers_false_for_an_unusable_cost(string cost)
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var stored = "$2a$" + cost + "$" + Body;
        stored.Length.Should().Be(60);

        hasher.Invoking(h => h.Verify("password", stored)).Should().NotThrow();
        hasher.Verify("password", stored).Should().BeFalse();
        hasher.NeedsRehash(stored).Should().BeTrue();
    }

    [Fact]
    public void A_real_hash_still_verifies()
    {
        var hasher = new BCryptPasswordHasher(workFactor: 4);
        var stored = hasher.Hash("password");

        hasher.Verify("password", stored).Should().BeTrue();
        hasher.NeedsRehash(stored).Should().BeFalse();
    }
}
