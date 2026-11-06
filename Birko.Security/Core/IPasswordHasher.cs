namespace Birko.Security;

/// <summary>
/// Hashes and verifies passwords.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes a password. Returns a self-contained string (includes algorithm, salt, iterations).
    /// </summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a password against a previously hashed value.
    /// </summary>
    bool Verify(string password, string hashedPassword);

    /// <summary>
    /// Whether a stored hash should be replaced by <see cref="Hash"/> after the next successful <see cref="Verify"/>:
    /// it was made with weaker parameters than this hasher's (fewer iterations, a lower work factor), or is not this
    /// hasher's format at all.
    /// </summary>
    /// <remarks>
    /// SH-L226: BCrypt had it and PBKDF2 did not, and it was not on this interface, so a caller holding an
    /// <see cref="IPasswordHasher"/> could not upgrade old hashes: one written at 1,000 iterations verified forever.
    /// </remarks>
    bool NeedsRehash(string hashedPassword);
}
