using Birko.Security.BCrypt.Hashing;
using FluentAssertions;
using Xunit;

namespace Birko.Security.BCrypt.Tests;

/// <summary>
/// Known-answer (reference-vector) tests for CR-C24. These are canonical OpenBSD / jBCrypt
/// <c>$2a$</c> test vectors. Verify() recomputes the hash from the reference salt embedded in each
/// published hash and compares — so a passing Verify against a *published* hash proves the
/// implementation actually is bcrypt (the hand-rolled EksBlowfish previously deviated from the spec
/// and produced non-interoperable output).
/// </summary>
public class BCryptReferenceVectorTests
{
    // password -> canonical $2a$06$ hash (jBCrypt test suite).
    [Theory]
    [InlineData("", "$2a$06$DCq7YPn5Rq63x1Lad4cll.TV4S6ytwfsfvkgY8jIucDrjc8deX1s.")]
    [InlineData("a", "$2a$06$m0CrhHm10qJ3lXRY.5zDGO3rS2KdeeWLuGmsfGlMfOxih58VYVfxe")]
    [InlineData("abc", "$2a$06$If6bvum7DFjUnE9p2uDeDu0YHzrHM6tf.iqN8.yx.jNN1ILEf7h0i")]
    [InlineData("~!@#$%^&*()      ~!@#$%^&*()PNBFRD", "$2a$06$fPIsBO8qRqkjj273rfaOI.HtSV9jLDpTbZn782DC6/t7qT67P6FfO")]
    public void Verify_MatchesCanonicalReferenceVector(string password, string referenceHash)
    {
        var hasher = new BCryptPasswordHasher(workFactor: BCryptPasswordHasher.MinWorkFactor);
        hasher.Verify(password, referenceHash).Should().BeTrue(
            "the implementation must reproduce the canonical bcrypt hash for this password+salt");
    }

    [Theory]
    [InlineData("a", "$2a$06$DCq7YPn5Rq63x1Lad4cll.TV4S6ytwfsfvkgY8jIucDrjc8deX1s.")] // wrong password for the "" vector
    [InlineData("", "$2a$06$m0CrhHm10qJ3lXRY.5zDGO3rS2KdeeWLuGmsfGlMfOxih58VYVfxe")]  // wrong password for the "a" vector
    public void Verify_RejectsWrongPasswordAgainstReferenceHash(string password, string referenceHash)
    {
        var hasher = new BCryptPasswordHasher(workFactor: BCryptPasswordHasher.MinWorkFactor);
        hasher.Verify(password, referenceHash).Should().BeFalse();
    }
}
