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
}
