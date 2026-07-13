using System;
using System.Security.Cryptography;

namespace Birko.Security.Hashing;

/// <summary>
/// PBKDF2 password hasher using SHA-512. No external NuGet dependencies.
/// Output format: "PBKDF2-SHA512:{iterations}:{base64salt}:{base64hash}"
/// </summary>
public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA512";
    private const int SaltSize = 16;     // 128-bit salt
    private const int HashSize = 32;     // 256-bit hash
    private const int DefaultIterations = 600_000;

    private readonly int _iterations;

    public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 10_000)
            throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be at least 10,000");
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _iterations,
            HashAlgorithmName.SHA512,
            HashSize);

        return $"{Algorithm}:{_iterations}:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hashedPassword)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(hashedPassword);

        var parts = hashedPassword.Split(':');
        if (parts.Length != 4 || parts[0] != Algorithm)
            return false;

        if (!int.TryParse(parts[1], out var iterations))
            return false;

        // CR-M233: Verify must be total over arbitrary stored strings — a corrupted/truncated DB column
        // with the right shape but non-base64 salt/hash segments must return false, not throw
        // FormatException. (The segment-count / algorithm / iteration guards above already return false.)
        byte[] salt;
        byte[] storedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            storedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var computedHash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            storedHash.Length);

        return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
    }
}
