# Birko.Security.BCrypt

BCrypt password hashing implementation for the Birko framework. Implements `IPasswordHasher` using the BCrypt adaptive hashing algorithm with no external NuGet dependencies — a pure C# Blowfish/BCrypt implementation.

> **Maintenance note — consider replacing with `BCrypt.Net-Next`.** This is hand-rolled cryptography.
> The EksBlowfish key schedule was rewritten in 2026-07 to match the canonical OpenBSD/Provos–Mazières
> algorithm and is now validated against published `$2a$` reference vectors (see
> `Birko.Security.BCrypt.Tests/BCryptReferenceVectorTests`), but maintaining bespoke crypto is a
> long-term liability. If a NuGet dependency becomes acceptable, prefer swapping the internals for the
> vetted [`BCrypt.Net-Next`](https://www.nuget.org/packages/BCrypt.Net-Next) package (keeping the
> `IPasswordHasher` surface) — it is continuously reviewed and reference-tested by the community.
> Any change here **must** keep the reference-vector tests green so interoperability is never lost again.

## Features

- **BCrypt adaptive hashing** — work factor can be increased over time as hardware improves
- **Standard output format** — `$2a$XX$` modular crypt format, compatible with other BCrypt implementations
- **Configurable work factor** — 4 to 31 (default: 12, ~250ms per hash)
- **NeedsRehash detection** — check if stored hashes need upgrading to a higher work factor
- **Constant-time comparison** — `CryptographicOperations.FixedTimeEquals` prevents timing attacks
- **No external dependencies** — pure C# implementation of Blowfish and BCrypt
- **72-byte password limit** — per BCrypt specification (UTF-8 encoded, null-terminated)

## Usage

```csharp
using Birko.Security;
using Birko.Security.BCrypt.Hashing;

// Create hasher with default work factor (12)
IPasswordHasher hasher = new BCryptPasswordHasher();

// Or with a custom work factor
IPasswordHasher strongHasher = new BCryptPasswordHasher(workFactor: 14);

// Hash a password
string hash = hasher.Hash("MySecurePassword!");
// Output: $2a$12$xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

// Verify a password
bool isValid = hasher.Verify("MySecurePassword!", hash);  // true
bool isWrong = hasher.Verify("WrongPassword", hash);      // false

// Check if rehash is needed (e.g., after increasing work factor)
var upgraded = new BCryptPasswordHasher(workFactor: 14);
if (upgraded.NeedsRehash(hash))
{
    // Re-hash with stronger work factor
    string newHash = upgraded.Hash("MySecurePassword!");
}
```

## BCrypt vs PBKDF2

| Feature | BCrypt | PBKDF2 (built-in) |
|---------|--------|-------------------|
| Algorithm | Blowfish-based, memory-hard | HMAC-SHA512 |
| Work factor | 2^N iterations | Configurable iterations |
| Memory usage | ~4KB (Blowfish state) | Minimal |
| GPU resistance | Better (memory-bound) | Lower |
| Max password | 72 bytes | Unlimited |
| Dependencies | None (pure C#) | None (BCL) |

## Dependencies

- Birko.Security (IPasswordHasher interface)
- No external NuGet packages

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
