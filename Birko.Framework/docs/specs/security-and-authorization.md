---
area: security-and-authorization
generated-at: 4b5bf7e6a33b66aa46da5e6adc40ff88e547da43
generated-on: 2026-10-16
sources:
  - ../Birko.Security.AspNetCore/Authentication/JwtAuthenticationOptions.cs
  - ../Birko.Security.AspNetCore/Authentication/JwtBearerExtensions.cs
  - ../Birko.Security.AspNetCore/Authentication/JwtClaimNames.cs
  - ../Birko.Security.AspNetCore/Authentication/TokenServiceAdapter.cs
  - ../Birko.Security.AspNetCore/Authorization/ClaimsPermissionChecker.cs
  - ../Birko.Security.AspNetCore/Authorization/IUserPermissionResolver.cs
  - ../Birko.Security.AspNetCore/Authorization/PermissionEndpointFilter.cs
  - ../Birko.Security.AspNetCore/Authorization/PermissionResolutionMiddleware.cs
  - ../Birko.Security.AspNetCore/Extensions/PermissionResolutionExtensions.cs
  - ../Birko.Security.AspNetCore/Extensions/SecurityServiceExtensions.cs
  - ../Birko.Security.AspNetCore/User/ClaimMappingOptions.cs
  - ../Birko.Security.AspNetCore/User/ClaimsCurrentUser.cs
  - ../Birko.Security.AspNetCore/User/ICurrentUser.cs
  - ../Birko.Security.AspNetCore/User/ResolvedPermissionsCurrentUser.cs
  - ../Birko.Security.AzureKeyVault/AzureKeyVaultSecretProvider.cs
  - ../Birko.Security.AzureKeyVault/AzureKeyVaultSettings.cs
  - ../Birko.Security.BCrypt/Hashing/BCryptPasswordHasher.cs
  - ../Birko.Security.Grants.SQL/GrantSqlIndexes.cs
  - ../Birko.Security.Grants.SQL/GrantSqlMappings.cs
  - ../Birko.Security.Grants.SQL/Mappings/InvitationMapping.cs
  - ../Birko.Security.Grants.SQL/Mappings/ResourceGrantMapping.cs
  - ../Birko.Security.Grants.SQL/Stores/SqlGrantStores.cs
  - ../Birko.Security.Grants/GrantConfigurationException.cs
  - ../Birko.Security.Grants/GrantErrorCodes.cs
  - ../Birko.Security.Grants/GrantOptions.cs
  - ../Birko.Security.Grants/GrantRefusedException.cs
  - ../Birko.Security.Grants/GrantRoleTable.cs
  - ../Birko.Security.Grants/GrantSignInOptions.cs
  - ../Birko.Security.Grants/Internal/KeyedLock.cs
  - ../Birko.Security.Grants/Internal/TokenHasher.cs
  - ../Birko.Security.Grants/Models/Invitation.cs
  - ../Birko.Security.Grants/Models/ResourceGrant.cs
  - ../Birko.Security.Grants/ResourceVisibility.cs
  - ../Birko.Security.Grants/Services/AdminGrantService.cs
  - ../Birko.Security.Grants/Services/BootstrapAdminGrantSource.cs
  - ../Birko.Security.Grants/Services/GrantService.cs
  - ../Birko.Security.Grants/Services/GrantSignInService.cs
  - ../Birko.Security.Grants/Services/GrantedResources.cs
  - ../Birko.Security.Grants/Services/IAdminGrantService.cs
  - ../Birko.Security.Grants/Services/IGrantAdminSource.cs
  - ../Birko.Security.Grants/Services/IGrantService.cs
  - ../Birko.Security.Grants/Services/IGrantSignInService.cs
  - ../Birko.Security.Grants/Services/IInvitationService.cs
  - ../Birko.Security.Grants/Services/InvitationService.cs
  - ../Birko.Security.Grants/Stores/GrantPredicates.cs
  - ../Birko.Security.Grants/Stores/IInvitationStore.cs
  - ../Birko.Security.Grants/Stores/IResourceGrantStore.cs
  - ../Birko.Security.Jwt/JwtTokenProvider.cs
  - ../Birko.Security.Jwt/OpenIdConnect/HttpOidcSigningKeySource.cs
  - ../Birko.Security.Jwt/OpenIdConnect/IOidcIdTokenVerifier.cs
  - ../Birko.Security.Jwt/OpenIdConnect/IOidcSigningKeySource.cs
  - ../Birko.Security.Jwt/OpenIdConnect/OidcIdTokenVerifier.cs
  - ../Birko.Security.Jwt/OpenIdConnect/OidcProviderOptions.cs
  - ../Birko.Security.Jwt/OpenIdConnect/OidcVerificationResult.cs
  - ../Birko.Security.Vault.Configuration/LocalVaultConfigurationExtensions.cs
  - ../Birko.Security.Vault.Configuration/LocalVaultConfigurationProvider.cs
  - ../Birko.Security.Vault.Configuration/LocalVaultConfigurationSource.cs
  - ../Birko.Security.Vault.Configuration/LocalVaultOptions.cs
  - ../Birko.Security.Vault.Configuration/SecretConfigurationExtensions.cs
  - ../Birko.Security.Vault.Configuration/SecretConfigurationProvider.cs
  - ../Birko.Security.Vault.Configuration/SecretConfigurationSource.cs
  - ../Birko.Security.Vault/VaultSecretProvider.cs
  - ../Birko.Security.Vault/VaultSettings.cs
  - ../Birko.Security/Authentication/AuthenticationConfiguration.cs
  - ../Birko.Security/Authentication/AuthenticationService.cs
  - ../Birko.Security/Authentication/TokenBinding.cs
  - ../Birko.Security/Authorization/IRoleProvider.cs
  - ../Birko.Security/Core/IEncryptionProvider.cs
  - ../Birko.Security/Core/IPasswordHasher.cs
  - ../Birko.Security/Core/ISecretProvider.cs
  - ../Birko.Security/Core/ITokenProvider.cs
  - ../Birko.Security/Encryption/AesEncryptionProvider.cs
  - ../Birko.Security/Hashing/Pbkdf2PasswordHasher.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: []
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
---
# Hashing, encryption, tokens, secrets and permission enforcement

## Purpose

This capability is the framework's security substrate. It answers six questions for any Birko-based
application: how a password is stored and checked, how bytes are encrypted at rest, how a caller proves
who they are (an internally minted JWT, an externally issued OpenID Connect id token, or a static
pre-shared token bound to an IP), where secrets come from (HashiCorp Vault, Azure Key Vault, or any other
`ISecretProvider` — optionally projected straight into `IConfiguration`), how an authenticated caller's
permissions are read and enforced on an endpoint, and — through `Birko.Security.Grants` — which role a user
holds on one particular resource, who may share it, and how an invitation or a bootstrap identity turns into
such a grant at sign-in.

Consumers are ASP.NET Core applications (via `AddBirkoSecurity` / `AddBirkoJwtBearer` / `RequirePermission`),
console and worker processes (the hashers, the encryption provider, the token provider and the secret
providers carry no ASP.NET Core dependency), and the multi-tenant machinery, which reads the
`tenant_id` claim through `ICurrentUser` and through `BirkoSecurityOptions`.

## Requirements

### Requirement: PBKDF2 password hashes are self-describing

The system SHALL produce, from `Pbkdf2PasswordHasher.Hash`, a single string of exactly four
colon-separated segments — `PBKDF2-SHA512:{iterations}:{base64 16-byte salt}:{base64 32-byte hash}` —
using a per-call cryptographically random salt, `Rfc2898DeriveBytes.Pbkdf2` with `HashAlgorithmName.SHA512`,
and the iteration count the hasher was constructed with (default 600,000). The system SHALL reject a
construction-time iteration count below 10,000 with `ArgumentOutOfRangeException`, and SHALL throw
`ArgumentNullException` for a null password.

#### Scenario: Default hashing

- **Given** a `Pbkdf2PasswordHasher()` constructed with no arguments
- **When** `Hash("correct horse")` is called
- **Then** the result splits on `:` into 4 parts, part 0 is `PBKDF2-SHA512`, part 1 is `600000`, part 2 decodes to 16 bytes and part 3 decodes to 32 bytes

#### Scenario: Two hashes of the same password differ

- **Given** a `Pbkdf2PasswordHasher`
- **When** `Hash("p")` is called twice
- **Then** the two returned strings are not equal, because a fresh `RandomNumberGenerator.GetBytes(16)` salt is drawn each call

#### Scenario: Iteration floor enforced at construction

- **Given** a caller wanting a fast hasher
- **When** `new Pbkdf2PasswordHasher(9_999)` is constructed
- **Then** `ArgumentOutOfRangeException` is thrown with the message "Iterations must be at least 10,000"

### Requirement: PBKDF2 verification is total and fails closed on any malformed stored string

The system SHALL make `Pbkdf2PasswordHasher.Verify` return `false` — never throw — for any non-null stored
string that is not a well-formed hash of this format, and SHALL make that judgement **before** any
comparison is attempted. A stored string is well-formed only when it has exactly 4 colon-separated
segments, an algorithm tag of `PBKDF2-SHA512`, an iteration segment that parses as an `int` greater than
zero and no greater than the larger of 10,000,000 and the hasher's own configured count, salt and hash segments that are both valid Base64 (the `FormatException` is caught), a salt that
decodes to at least one byte, and a hash that decodes to exactly `HashSize` (32) bytes. The system SHALL
compare the recomputed and stored hashes with `CryptographicOperations.FixedTimeEquals`, and SHALL throw
`ArgumentNullException` when either argument is null.

`Hash` never emits a malformed shape, so reaching these guards takes a truncated column, a column defaulted
to `''`, or a half-finished migration — none of which is treated as a reason to succeed.

#### Scenario: Truncated database column

- **Given** a stored value `"PBKDF2-SHA512:600000:not-base64!:alsonot"`
- **When** `Verify("p", stored)` is called
- **Then** it returns `false` rather than throwing `FormatException`

#### Scenario: Foreign hash format

- **Given** a stored value produced by BCrypt, `"$2a$12$……"`
- **When** `Verify("p", stored)` is called
- **Then** it returns `false` (the segment count / algorithm tag guard fails first)

#### Scenario: Null arguments are programming errors, not verification failures

- **Given** any hasher
- **When** `Verify(null!, "…")` or `Verify("p", null!)` is called
- **Then** `ArgumentNullException` is thrown

#### Scenario: Non-positive iteration count in the stored hash

- **Given** a stored value `"PBKDF2-SHA512:0:{valid base64 salt}:{valid base64 hash}"`, or the same with `-1`
- **When** `Verify("p", stored)` is called
- **Then** it returns `false` and nothing is thrown; the `iterations <= 0` guard rejects the value before `Rfc2898DeriveBytes.Pbkdf2` can raise `ArgumentOutOfRangeException` out of the login path

#### Scenario: Stored iteration count above the ceiling

- **Given** a hasher constructed with the default 600,000 iterations and a stored value `"PBKDF2-SHA512:2147483647:{valid base64 salt}:{valid base64 hash}"`
- **When** `Verify("p", stored)` is called
- **Then** it returns `false` without deriving a key, because the count exceeds `Math.Max(10_000_000, 600_000)`; a hasher constructed with 20,000,000 iterations would still verify a hash stored at 20,000,000

#### Scenario: Empty salt and hash segments are rejected

- **Given** a stored value `"PBKDF2-SHA512:600000::"`, as left by a truncated column or a half-finished migration
- **When** `Verify(anyPassword, stored)` is called
- **Then** it returns `false` for every password — the empty salt and the 0-byte hash each fail the well-formedness guard, so no zero-length key is derived and `FixedTimeEquals` is never reached

#### Scenario: Empty salt with an otherwise genuine hash

- **Given** a genuine stored value whose salt segment has been blanked
- **When** `Verify(correctPassword, stored)` is called
- **Then** it returns `false`, because an unsalted column is not verifiable

### Requirement: PBKDF2 verification derives its key length from the algorithm, not from the stored value

The system SHALL recompute the candidate hash at a length of `HashSize` (32) bytes — the algorithm's own
output size — and SHALL reject any stored hash segment whose decoded length differs, in either direction.
The iteration count, by contrast, is taken from the stored string rather than from the hasher's own
configuration, so hashes written under an older iteration count remain verifiable. There is no
verification-time iteration minimum: a stored hash whose iteration segment is far below the constructor's
10,000 floor still verifies successfully, and `Pbkdf2PasswordHasher` exposes no rehash-needed query. There is
a verification-time maximum (see the well-formedness rule above), so a stored count cannot make one login
attempt cost hours of CPU.

Because PBKDF2 output is prefix-stable, deriving to `storedHash.Length` would let a truncated column decide
how many bytes were compared — a 1-byte segment would match an arbitrary password roughly 1 in 256, and a
0-byte segment would match every password. Pinning the length to the algorithm removes that entire class.

#### Scenario: Legacy weak hash still verifies

- **Given** a stored value `"PBKDF2-SHA512:10000:{salt}:{hash}"` written by an older deployment, and a hasher constructed with 600,000 iterations
- **When** `Verify(correctPassword, stored)` is called
- **Then** it returns `true`, because the 10,000 from the stored string is used for the recomputation

#### Scenario: Truncated hash segment rejected even for the correct password

- **Given** a genuine stored value whose hash segment has been truncated to 1, 4, 16 or 31 bytes
- **When** `Verify(correctPassword, stored)` is called
- **Then** it returns `false`, although the truncated segment is byte-for-byte the prefix the correct password derives to

#### Scenario: Over-long hash segment rejected

- **Given** a stored value whose hash segment decodes to 64 bytes
- **When** `Verify(correctPassword, stored)` is called
- **Then** it returns `false`; the length is pinned to 32 in both directions, not merely required to be non-empty

### Requirement: BCrypt hashing emits the standard modular crypt format with a bounded work factor

The system SHALL make `BCryptPasswordHasher.Hash` return a 60-character `$2a${cost:D2}${22-char salt}{31-char digest}`
string, where the salt is 16 random bytes in the BCrypt-Base64 alphabet and the digest is the 23-byte
EksBlowfish output. The system SHALL reject a work factor outside `MinWorkFactor` (4) … `MaxWorkFactor` (31)
at construction with `ArgumentOutOfRangeException`, and SHALL default to `DefaultWorkFactor` (12).

#### Scenario: Default work factor is encoded in the hash

- **Given** a `BCryptPasswordHasher()` with no arguments
- **When** `Hash("secret")` is called
- **Then** the result is 60 characters long and starts with `$2a$12$`

#### Scenario: Work factor out of range

- **Given** a caller passing 32
- **When** `new BCryptPasswordHasher(32)` is constructed
- **Then** `ArgumentOutOfRangeException` is thrown

### Requirement: BCrypt verification accepts $2a$ and $2b$ and rejects anything malformed

The system SHALL accept, in `Verify`, only a 60-character string with `$` at indices 0, 3 and 6, `2` at index 1,
`a` or `b` at index 2, two ASCII digits at indices 4–5 whose value lies within `MinWorkFactor` (4) …
`MaxWorkFactor` (31), and every character in indices 7…59 drawn from the BCrypt-Base64 alphabet
`./A-Za-z0-9`; anything else SHALL return `false` without reaching the decoder. The system SHALL re-hash the
candidate against the stored 29-character salt prefix and compare with
`CryptographicOperations.FixedTimeEquals` over the UTF-8 bytes of the two full hash strings.

A shaped hash whose cost does not parse (`$2a$ab$…`) or parses outside the range (`$2a$99$…`) is therefore
answered `false` by the shape guard, never by an exception out of `HashPassword`; `NeedsRehash` on the same
input answers `true`.

#### Scenario: Hash from another implementation tagged $2b$

- **Given** a valid 60-character `$2b$12$…` hash produced elsewhere
- **When** `Verify(correctPassword, hash)` is called
- **Then** it returns `true` (inputs are capped at 72 bytes, so `$2a$` and `$2b$` hash identically)

#### Scenario: Shaped-but-corrupt hash

- **Given** a 60-character string whose salt region contains `!`, which is outside the BCrypt-Base64 alphabet
- **When** `Verify("p", hash)` is called
- **Then** it returns `false`; `DecodeBCryptBase64` is never reached, so no silent garbage-salt decode occurs

#### Scenario: Wrong length

- **Given** a 59-character truncated hash
- **When** `Verify("p", hash)` is called
- **Then** it returns `false`

#### Scenario: Shaped hash whose cost is out of range

- **Given** a 60-character in-alphabet hash reading `$2a$99$…`
- **When** `Verify("p", hash)` is called
- **Then** it returns `false` and nothing is thrown — the shape guard rejects the cost before `HashPassword` is reached

#### Scenario: Shaped hash whose cost is not two digits

- **Given** a 60-character in-alphabet hash reading `$2a$ab$…`
- **When** `Verify("p", hash)` is called
- **Then** it returns `false`

### Requirement: BCrypt truncates the password at 72 bytes including its NUL terminator

The system SHALL encode the password as UTF-8 with a trailing `"\0"` appended and, if the result exceeds
72 bytes, SHALL copy only the first 72 bytes. Two passwords that agree on the first 71 UTF-8 bytes therefore
produce identical hashes and verify interchangeably.

#### Scenario: Long passwords collide past the cap

- **Given** `a` repeated 71 times, forming `p1`, and `p1 + "X"`, forming `p2`
- **When** `Verify(p2, Hash(p1))` is called
- **Then** it returns `true`, because both are truncated to the same 72 bytes

### Requirement: BCrypt reports when a stored hash was made with a weaker work factor

The system SHALL make `NeedsRehash` return `true` when the stored hash is not a valid BCrypt hash, when its
cost digits do not parse, or when the parsed cost is lower than the hasher's configured work factor; and
`false` when the parsed cost is greater than or equal to it.

#### Scenario: Cost upgrade needed

- **Given** a hasher configured with work factor 12 and a stored `$2a$10$…` hash
- **When** `NeedsRehash(stored)` is called
- **Then** it returns `true`

#### Scenario: Unparseable input is treated as needing a rehash

- **Given** a hasher configured with work factor 12 and a stored value `"not-a-hash"`
- **When** `NeedsRehash(stored)` is called
- **Then** it returns `true`

### Requirement: AES-256-GCM encryption emits a self-contained nonce/tag/ciphertext envelope

The system SHALL, in `AesEncryptionProvider.Encrypt`, draw a fresh 12-byte nonce per call, produce a
16-byte authentication tag, and return `[12-byte nonce][16-byte tag][ciphertext]` — so the output is exactly
28 bytes longer than the plaintext. The system SHALL require a key of exactly 32 bytes, throwing
`ArgumentNullException` for a null key and `ArgumentException` for any other length, and SHALL throw
`ArgumentNullException` for null data before its length is read. `GenerateKey()` SHALL return 32
cryptographically random bytes.

#### Scenario: Round trip

- **Given** a 32-byte key from `AesEncryptionProvider.GenerateKey()` and a 10-byte plaintext
- **When** `Decrypt(Encrypt(data, key), key)` is called
- **Then** the original 10 bytes are returned, and the intermediate ciphertext was 38 bytes

#### Scenario: Wrong key length

- **Given** a 16-byte key
- **When** `Encrypt(data, key)` is called
- **Then** `ArgumentException` is thrown naming the `key` parameter and stating 32 bytes are required

#### Scenario: String helpers are Base64 over the same envelope

- **Given** a 32-byte key
- **When** `DecryptString(EncryptString("čšž", key), key)` is called
- **Then** `"čšž"` is returned (UTF-8 in, Base64 out)

### Requirement: AES decryption fails closed on short or tampered input

The system SHALL throw `CryptographicException("Encrypted data is too short.")` when the input is shorter
than 28 bytes, and SHALL let `AesGcm.Decrypt`'s own authentication failure propagate when the tag does not
match the ciphertext, nonce or key. `DecryptString` SHALL throw `FormatException` for a non-Base64 input.

#### Scenario: Envelope too short to contain nonce and tag

- **Given** a 20-byte buffer
- **When** `Decrypt(buffer, key)` is called
- **Then** `CryptographicException` is thrown with "Encrypted data is too short."

#### Scenario: Flipped ciphertext byte

- **Given** a valid envelope with one ciphertext byte inverted
- **When** `Decrypt(tampered, key)` is called
- **Then** `AesGcm` raises a `CryptographicException` for the failed tag check and no plaintext is returned

#### Scenario: Decryption under the wrong key

- **Given** an envelope produced under key A
- **When** `Decrypt(envelope, keyB)` is called
- **Then** the tag check fails and `CryptographicException` propagates

### Requirement: JWT generation signs with HMAC-SHA256 and supplies jti and iat

The system SHALL make `JwtTokenProvider.GenerateToken` sign with `SecurityAlgorithms.HmacSha256` over
`SymmetricSecurityKey(UTF8(Secret))`, project every entry of the supplied claim dictionary into exactly one
JWT claim, and additionally add a random `jti` (`Guid.NewGuid()`) and an `iat` (`ClaimValueTypes.Integer64`)
when the caller did not supply those keys. The system SHALL read the clock exactly once per call
(`IDateTimeProvider.OffsetUtcNow`, defaulting to `SystemDateTimeProvider`) so that `iat` and `exp` derive
from one instant, and SHALL set `exp` to `now + ExpirationMinutes`. `TokenResult.ExpiresAt` SHALL be that
same UTC instant, and `TokenResult.RefreshToken` SHALL be left null.

#### Scenario: Standard claims added

- **Given** a provider with a valid secret and `ExpirationMinutes = 60`
- **When** `GenerateToken(new Dictionary<string,string> { ["sub"] = "…" })` is called
- **Then** the emitted token carries `sub`, a generated `jti` and a generated `iat`, and `ExpiresAt` is 60 minutes after the single clock read

#### Scenario: Caller-supplied jti wins

- **Given** a claim dictionary that already contains a `jti` entry
- **When** `GenerateToken` is called
- **Then** no second `jti` is added

#### Scenario: Null claims dictionary

- **Given** a valid provider
- **When** `GenerateToken(null!)` is called
- **Then** `ArgumentNullException` is thrown before any signing work

#### Scenario: Secret is required on the constructor and on every per-call override

- **Given** a provider constructed with a valid secret
- **When** `GenerateToken(claims, new TokenOptions { Secret = "" })` is called
- **Then** `ArgumentException("Secret must be provided.")` is thrown rather than an opaque signing failure

### Requirement: JWT validation requires an issuer and audience, and returns a result object for every token failure

The system SHALL make `JwtTokenProvider.ValidateToken` validate the signing key, the lifetime (with a fixed
one-minute `ClockSkew`), and **always** the issuer and audience against `TokenOptions.Issuer` /
`TokenOptions.Audience`. A `TokenOptions` (the default or a per-call override) whose `Issuer` or `Audience` is
null or whitespace SHALL be refused before any validation with
`ArgumentException("Issuer and Audience must be provided to validate a token.")` — a configuration error,
thrown like the missing-secret one, not a `Failure` result. The constructor does not check them, so the
refusal surfaces at the first `ValidateToken`, while `GenerateToken` still mints tokens without them.

For every failure of the token itself the system SHALL return `TokenValidationResult.Failure` with a
categorised message: `"Token has expired."` for `SecurityTokenExpiredException`,
`"Token validation failed: {message}"` for any other `SecurityTokenException`, and
`"Unexpected error: {message}"` for anything else. On success it SHALL group the principal's claims by type
(ordinal) and keep every distinct value of each type in token order: `ClaimValues` holds them as a list per
type, and `Claims` holds one string per type with the values joined by `';'`.

#### Scenario: Expired token

- **Given** a token whose `exp` is more than one minute in the past
- **When** `ValidateToken(token)` is called
- **Then** `IsValid` is false and `Error` is exactly `"Token has expired."`

#### Scenario: Garbage input

- **Given** the string `"not.a.jwt"`
- **When** `ValidateToken` is called
- **Then** a `Failure` result is returned; no exception escapes

#### Scenario: Issuer or audience unconfigured

- **Given** `TokenOptions` with `Secret` set and `Issuer`/`Audience` left empty
- **When** `ValidateToken(token)` is called with any token
- **Then** `ArgumentException("Issuer and Audience must be provided to validate a token.")` is thrown and no token is accepted whatever issuer it names

#### Scenario: Token from another issuer

- **Given** `TokenOptions` with `Issuer = "A"` and `Audience = "A"`
- **When** a token signed with the same secret but bearing `iss: "B"` is validated
- **Then** a `"Token validation failed: …"` failure is returned

#### Scenario: Repeated claim types are kept

- **Given** a valid token carrying two `role` claims, `"admin"` and `"auditor"`
- **When** `ValidateToken` is called
- **Then** `IsValid` is true, `ClaimValues["role"]` is `["admin", "auditor"]` and `Claims["role"]` is `"admin;auditor"`

#### Scenario: A single-valued success result

- **Given** a caller building `TokenValidationResult.Success(dictionary)` from a `string → string` dictionary
- **When** `ClaimValues` is read
- **Then** each key maps to a one-element list holding its value

### Requirement: Refresh tokens are opaque random strings

The system SHALL make `GenerateRefreshToken` return the Base64 encoding of 32 bytes from
`RandomNumberGenerator`, carrying no claims and no expiry — expiry is the caller's responsibility, guided by
`TokenOptions.RefreshExpirationDays` (default 7), which the provider itself never reads.

#### Scenario: Opaque, non-repeating

- **Given** a provider
- **When** `GenerateRefreshToken()` is called twice
- **Then** two different 44-character Base64 strings are returned and neither parses as a JWT

### Requirement: Structured token claims are joined into single claim values

The system SHALL make `TokenServiceAdapter.GenerateAccessToken` emit `ClaimTypes.NameIdentifier` for
`TokenRequest.UserGuid` and `ClaimTypes.Email` for `TokenRequest.Email`, the configured
`ClaimMappingOptions.TenantGuidClaim` only when `TenantGuid` has a value, and — because `ITokenProvider`
takes a single-valued dictionary — the roles and permissions sets as **one** claim each whose value is the
members joined by `';'`, emitted only when the set is non-empty. `ValidateToken` SHALL invert this,
splitting the role and permission claim values on `';'` with `RemoveEmptyEntries`, parsing `UserGuid` and
`TenantGuid` with `Guid.TryParse` (null when absent or unparseable), and returning
`new TokenValidationInfo(false)` — all other fields null — when the underlying validation failed.

#### Scenario: Multi-valued permissions survive a round trip

- **Given** a `TokenRequest` with permissions `{ "users:read", "users:write" }`
- **When** the token is generated and then passed to `ValidateToken`
- **Then** exactly one permission claim was written with the value `"users:read;users:write"`, and the returned `Permissions` set has both members

#### Scenario: Unparseable user id

- **Given** a validated token whose `ClaimTypes.NameIdentifier` claim is `"abc"`
- **When** `ValidateToken` is called
- **Then** `IsValid` is true but `UserGuid` is null

#### Scenario: Invalid token yields an all-null info

- **Given** an expired token
- **When** `ValidateToken` is called
- **Then** `TokenValidationInfo.IsValid` is false and `Roles`/`Permissions` are null (not empty sets)

### Requirement: The current user is projected from configurable claim types

The system SHALL resolve `ICurrentUser` from `IHttpContextAccessor.HttpContext?.User` using
`ClaimMappingOptions` — `UserIdClaim` (default `ClaimTypes.NameIdentifier`), `EmailClaim` (default
`ClaimTypes.Email`), `TenantGuidClaim` (default `"tenant_id"`), `RoleClaim` (default `ClaimTypes.Role`) and
`PermissionClaim` (default `"permission"`). `UserGuid` and `TenantGuid` SHALL be null when the claim is absent
or fails `Guid.TryParse`. `IsAuthenticated` SHALL be false when there is no `HttpContext`, no identity, or an
unauthenticated identity. `GetClaim` SHALL return the first value of an arbitrary claim type, or null.

#### Scenario: No ambient HttpContext

- **Given** a `ClaimsCurrentUser` whose accessor returns a null `HttpContext`
- **When** `IsAuthenticated`, `UserGuid` and `Permissions` are read
- **Then** they are `false`, `null` and an empty set respectively — no `NullReferenceException`

#### Scenario: Malformed tenant claim

- **Given** a principal with `tenant_id = "not-a-guid"`
- **When** `TenantGuid` is read
- **Then** it is null

### Requirement: Role and permission claim values are split on both ',' and ';'

The system SHALL make `ClaimsCurrentUser.Roles` and `.Permissions` collect **all** claims of the configured
type, split each value on both `','` and `';'` with `RemoveEmptyEntries | TrimEntries`, and return the union
as a set. Multiple same-named claims and a single joined claim are therefore equivalent to a reader.

#### Scenario: Superadmin wildcard inside a joined value

- **Given** a single permission claim with the value `"*,users:user:read, users:user:write"`
- **When** `Permissions` is read
- **Then** the set contains `"*"`, `"users:user:read"` and `"users:user:write"` (whitespace trimmed)

#### Scenario: Repeated claims

- **Given** two `permission` claims, `"a"` and `"b"`
- **When** `Permissions` is read
- **Then** the set is `{ "a", "b" }`

### Requirement: Permissions may instead be resolved per request and read from HttpContext.Items

The system SHALL provide `ResolvedPermissionsCurrentUser`, which reads `UserGuid`, `Email` and `TenantGuid`
from claims exactly as `ClaimsCurrentUser` does, but takes `Permissions` **only** from
`HttpContext.Items["birko:resolved-permissions"]`, falling back to an empty set when the item is absent or
not an `IReadOnlySet<string>` — claim-borne permissions are ignored entirely on this path. `Roles` SHALL
prefer `HttpContext.Items["birko:resolved-roles"]` and fall back to the role claims (split on `','`/`';'`).

#### Scenario: Middleware did not run

- **Given** `ResolvedPermissionsCurrentUser` on a request where `PermissionResolutionMiddleware` was not registered, and a token carrying `permission` claims
- **When** `Permissions` is read
- **Then** an empty set is returned and the token's permission claims are not consulted

#### Scenario: Roles fall back to claims

- **Given** a request where the resolver returned no roles item but the principal has `role = "Admin;Auditor"`
- **When** `Roles` is read
- **Then** the set is `{ "Admin", "Auditor" }`

### Requirement: Permission resolution runs only for an authenticated, identifiable caller

The system SHALL make `PermissionResolutionMiddleware.InvokeAsync` call `IUserPermissionResolver.GetAsync`
and `GetRolesAsync` — storing their results under `ItemsKey` and `RolesItemsKey` — only when
`context.User?.Identity?.IsAuthenticated == true` **and** the configured `UserIdClaim` value parses as a
`Guid`. The tenant argument SHALL be the parsed `TenantGuidClaim`, coerced to `null` when it is absent,
unparseable, or `Guid.Empty`. The middleware SHALL always invoke the next delegate, whether or not it
resolved anything.

#### Scenario: Anonymous request

- **Given** an unauthenticated request
- **When** the middleware runs
- **Then** no resolver call is made, neither `HttpContext.Items` key is set, and the pipeline continues

#### Scenario: Empty tenant claim is not a tenant

- **Given** an authenticated principal with `tenant_id = "00000000-0000-0000-0000-000000000000"`
- **When** the middleware runs
- **Then** `GetAsync(userId, null, …)` is called — not `GetAsync(userId, Guid.Empty, …)`

#### Scenario: Resolver default for roles

- **Given** an `IUserPermissionResolver` implementation that overrides only `GetAsync`
- **When** the middleware runs
- **Then** the interface's default `GetRolesAsync` returns an empty set and that empty set is stored under `RolesItemsKey`

### Requirement: Endpoint permission filtering distinguishes 401 from 403

The system SHALL make `PermissionEndpointFilter` return a JSON `401` `{ error = "Unauthorized" }` when
`ICurrentUser` is not resolvable from request services or `IsAuthenticated` is false, and a JSON `403`
`{ error = "Forbidden", required = <permission> }` when the caller is authenticated but the permission set
contains neither the required permission nor the wildcard `"*"`. Otherwise it SHALL invoke the next filter.
The wildcard SHALL be honoured unconditionally — `BirkoSecurityOptions.WildcardPermissionEnabled` is not
consulted here.

#### Scenario: Superadmin passes any check

- **Given** an authenticated caller whose `Permissions` contains `"*"`
- **When** an endpoint guarded by `.RequirePermission("iot:device:write")` is invoked
- **Then** the handler runs

#### Scenario: Authenticated but unauthorised

- **Given** an authenticated caller with `Permissions = { "iot:device:read" }`
- **When** the endpoint guarded by `"iot:device:write"` is invoked
- **Then** a 403 response is returned whose body names `required = "iot:device:write"`

#### Scenario: ICurrentUser not registered

- **Given** an application that never called `AddBirkoSecurity`
- **When** a `RequirePermission`-guarded endpoint is invoked
- **Then** `GetService(typeof(ICurrentUser))` yields null and a 401 is returned

#### Scenario: Null required permission

- **Given** a caller constructing the filter directly
- **When** `new PermissionEndpointFilter(null!)` is evaluated
- **Then** `ArgumentNullException` is thrown at construction

### Requirement: The claims permission checker only answers for the current caller

The system SHALL make `ClaimsPermissionChecker.HasPermissionAsync(userId, permission)` return `false`, and
`GetPermissionsAsync(userId)` return an empty list, whenever `userId` differs from `ICurrentUser.UserGuid`
(including when the latter is null). For the matching user it SHALL return true when the permission set
contains the permission or `"*"`. No store or database is consulted.

#### Scenario: Asking about somebody else

- **Given** a request authenticated as user A with `"*"` in its permissions
- **When** `HasPermissionAsync(userB, "users:read")` is called
- **Then** `false` is returned

#### Scenario: Unauthenticated caller

- **Given** a request with no principal, so `ICurrentUser.UserGuid` is null
- **When** `GetPermissionsAsync(someGuid)` is called
- **Then** an empty list is returned

### Requirement: JWT bearer wiring accepts a token from the query string only where a header cannot be set

The system SHALL, in `AddBirkoJwtBearer`, require a non-empty `Secret` (throwing `ArgumentException`
otherwise), register `JwtAuthenticationOptions`, `ClaimMappingOptions`, an `ITokenProvider`
(`JwtTokenProvider`) and `TokenServiceAdapter` as singletons, and configure JWT bearer as both the default
authenticate and challenge scheme. The bearer handler SHALL always validate issuer, audience, signing key
and lifetime — with `ValidIssuer = Issuer` (default `"Birko"`), `ValidAudience = Audience ?? Issuer`, and
`ClockSkew = ClockSkewSeconds` seconds (default 60). In `OnMessageReceived` it SHALL take the token from a
non-empty `?token=` query-string parameter only when the request carries no `Authorization` header **and**
is a WebSocket upgrade, or has an `Accept` value containing `text/event-stream` (case-insensitive), or has a
path that starts, by segment and case-insensitively, with one of `JwtAuthenticationOptions.QueryTokenPathPrefixes`
(empty by default; each entry trimmed of whitespace and `/`, blank entries ignored). On any other request the
query parameter is ignored.

#### Scenario: Missing secret

- **Given** a configure delegate that leaves `Secret` empty
- **When** `AddBirkoJwtBearer` is called
- **Then** `ArgumentException("JWT Secret is required.")` is thrown at startup

#### Scenario: Audience defaults to issuer

- **Given** options with `Issuer = "MyApp"` and `Audience` left null
- **When** the bearer options are built
- **Then** `ValidAudience` is `"MyApp"`

#### Scenario: SSE connection carrying the token in the URL

- **Given** an `EventSource` request to `/events?token=<jwt>` with `Accept: text/event-stream` and no `Authorization` header
- **When** the request is authenticated
- **Then** `context.Token` is set from the query value and the request authenticates

#### Scenario: Ordinary request carrying a token in the URL

- **Given** a plain `GET /api/orders?token=<jwt>` with no `Authorization` header, not a WebSocket upgrade, and no configured prefix covering `/api`
- **When** the request is authenticated
- **Then** the query value is not used and the request is unauthenticated

#### Scenario: A header always wins over the query string

- **Given** a WebSocket upgrade carrying both `Authorization: Bearer <a>` and `?token=<b>`
- **When** the request is authenticated
- **Then** the query value is ignored and the header's token is the one validated

#### Scenario: A configured download path

- **Given** `QueryTokenPathPrefixes = ["/files/"]`
- **When** `GET /Files/report.pdf?token=<jwt>` arrives with no `Authorization` header
- **Then** the token is taken from the query string; `/filesystem?token=…` is not covered, because matching is by segment

### Requirement: One-line security registration selects a tenant resolver and defaults to strict tenancy

The system SHALL, in `AddBirkoSecurity`, call `AddBirkoJwtBearer` with the nested `Jwt` options (including
`QueryTokenPathPrefixes`), add `IHttpContextAccessor`, register the resolved `BirkoSecurityOptions` as a
singleton, register `ICurrentUser` → `ClaimsCurrentUser` as scoped **only when no `ICurrentUser` is already
registered** (`TryAddScoped`) and `IPermissionChecker` → `ClaimsPermissionChecker` as scoped,
register `Birko.Data.Tenant.Models.ITenantContext` as `Tenant.Current` plus `ITenantContext` →
`TenantContextAdapter`, and select the tenant resolver from `TenantResolverType`: `Header` →
`HeaderTenantResolver`, `Subdomain` → `SubdomainTenantResolver` (throwing `ArgumentException` when
`SubdomainLookup` is null), `Custom` → nothing registered. `RequireTenantHeaderMatchesClaim` SHALL default
to `true` and `WildcardPermissionEnabled` to `true`.

The system SHALL also register an `IStartupFilter` that, after the application's pipeline has been built,
throws `InvalidOperationException` — so the host refuses to start — when `RequireTenantHeaderMatchesClaim` is
true and `UseBirkoTenantHeaderGuard()` was never called on the application builder (it marks the builder's
`Properties`). The guard is not inserted on the application's behalf, because it must run after
authentication; setting `RequireTenantHeaderMatchesClaim = false` is the explicit opt-out and starts as before.

#### Scenario: Subdomain resolution without a lookup

- **Given** options with `TenantResolver = Subdomain` and `SubdomainLookup = null`
- **When** `AddBirkoSecurity` is called
- **Then** `ArgumentException("SubdomainLookup is required when TenantResolver = Subdomain.")` is thrown

#### Scenario: Custom resolver

- **Given** options with `TenantResolver = Custom`
- **When** `AddBirkoSecurity` is called
- **Then** no `ITenantResolver` is registered and the application must register one itself

#### Scenario: Header/claim agreement is on by default

- **Given** a caller that supplies no explicit value
- **When** `BirkoSecurityOptions` is resolved from the container
- **Then** `RequireTenantHeaderMatchesClaim` is `true`

#### Scenario: Pipeline without the tenant header guard

- **Given** `AddBirkoSecurity` with default options and an application that never calls `app.UseBirkoTenantHeaderGuard()`
- **When** the host builds its pipeline
- **Then** `InvalidOperationException` is thrown naming `RequireTenantHeaderMatchesClaim` and `UseBirkoTenantHeaderGuard()`, and the host does not start

#### Scenario: Guard explicitly opted out

- **Given** `RequireTenantHeaderMatchesClaim = false` and no `UseBirkoTenantHeaderGuard()` call
- **When** the host builds its pipeline
- **Then** it starts

### Requirement: Switching to resolved permissions replaces the registered ICurrentUser

The system SHALL make `UseResolvedPermissions` remove every existing `ICurrentUser` service descriptor
and register `ICurrentUser` → `ResolvedPermissionsCurrentUser` as scoped, leaving the concrete
`IUserPermissionResolver` registration to the application. `UseBirkoPermissionResolution` SHALL add
`PermissionResolutionMiddleware` to the pipeline, which must be placed after `UseAuthentication`.

#### Scenario: Replacing the claim-based reader

- **Given** a service collection where `AddBirkoSecurity` already registered `ICurrentUser` → `ClaimsCurrentUser`
- **When** `UseResolvedPermissions()` is called
- **Then** resolving `ICurrentUser` yields a `ResolvedPermissionsCurrentUser`

#### Scenario: Called before AddBirkoSecurity

- **Given** a service collection on which `UseResolvedPermissions()` is called first and `AddBirkoSecurity` afterwards
- **When** `ICurrentUser` is resolved
- **Then** it is a `ResolvedPermissionsCurrentUser` and it is the only `ICurrentUser` registration — `AddBirkoSecurity`'s `TryAddScoped` added nothing, so the order of the two calls does not matter

#### Scenario: Resolver not registered

- **Given** `UseResolvedPermissions` and `UseBirkoPermissionResolution` are both wired but no `IUserPermissionResolver` is registered
- **When** a request reaches the middleware
- **Then** the middleware's `IUserPermissionResolver` parameter cannot be resolved and the request fails with the container's own resolution exception

### Requirement: Static-token authentication allows everything only when deliberately switched off

The system SHALL treat `AuthenticationConfiguration.Enabled = false` as the **only** state in which every
caller is allowed through, exposed as `AuthenticationService.IsAuthenticationDisabled`. When `Enabled` is
true but no expanded token or binding survived initialisation, the service SHALL **reject** every caller
rather than allowing them, SHALL report the state through `IsMisconfigured`, and SHALL log an error at
construction naming both the likely causes and the deliberate opt-out.

`IsAuthenticationEnabled` SHALL continue to return true only when `Enabled` is true **and** at least one
expanded token or binding survived initialisation — it answers "enabled and configured", which is a
different question from the one a gate asks, and it is unchanged because five transport wrappers expose it.

#### Scenario: Feature switched off

- **Given** a configuration with `Enabled = false` and several tokens
- **When** `ValidateToken(null, "10.0.0.1")` is called
- **Then** it returns `true`, and `IsMisconfigured` is false — a deliberate switch-off is not a fault

#### Scenario: Enabled but nothing configured

- **Given** a configuration with `Enabled = true`, `Tokens` empty and `TokenBindings` empty
- **When** `ValidateToken("anything", "10.0.0.1")` is called
- **Then** it returns `false`, and so does `ValidateToken(null, "10.0.0.1")`
- **And** `IsMisconfigured` is true while `IsAuthenticationEnabled()` remains false

#### Scenario: Enabled with tokens that do not survive expansion

- **Given** a configuration with `Enabled = true` and `Tokens` containing only whitespace, or a `${VAR}`
  whose environment variable exists and is **blank** (so `GetEnvironmentVariable` returns `""`, the
  `?? value` fallback does not fire, and the token is dropped)
- **When** `ValidateToken` is called with any input
- **Then** it returns `false`

#### Scenario: Enabled with a `${VAR}` that names an absent variable

- **Given** a configuration with `Enabled = true` and a single token `"${MISSING}"` whose variable is unset
- **When** `ValidateToken` is called
- **Then** the literal `"${MISSING}"` is retained as the token, so `IsMisconfigured` is false and the
  service fails **closed** — every real token is rejected and only the literal is accepted

#### Scenario: Enabled and configured, no token presented

- **Given** a configuration with `Enabled = true` and one token
- **When** `ValidateToken("   ", "10.0.0.1")` is called
- **Then** it returns `false` and a warning naming the client IP is logged

### Requirement: Token bindings are evaluated before plain tokens and short-circuit the decision

The system SHALL evaluate `TokenBindings` first, in configuration order, comparing tokens with
`StringComparer.Ordinal`. On the **first** binding whose token matches, the outcome SHALL be decided there:
`false` when the client IP is null or whitespace, `true` when the binding's `AllowedIps` contains the IP
exactly, and `false` otherwise — the plain `Tokens` list is not consulted afterwards. Only when no binding
token matched SHALL the expanded `Tokens` set be checked by exact membership. A configuration that is
enabled but yields neither collection after expansion SHALL return `false` and log
"Authentication enabled but no tokens or bindings configured".

#### Scenario: IP-bound token from the wrong address

- **Given** a binding `{ Token = "T", AllowedIps = ["10.0.0.1"] }` and also `Tokens = ["T"]`
- **When** `ValidateToken("T", "10.0.0.2")` is called
- **Then** it returns `false` — the matching binding decides, and the plain-token list is never reached

#### Scenario: IP unknown for an IP-bound token

- **Given** the same binding
- **When** `ValidateToken("T", null)` is called
- **Then** it returns `false`

#### Scenario: Binding with an empty allow-list

- **Given** a binding `{ Token = "T", AllowedIps = [] }` with `Enabled = true`
- **When** `ValidateToken("T", "10.0.0.1")` is called
- **Then** it returns `false` for every possible IP, while the binding still makes `IsAuthenticationEnabled()` true
- **And** an error was logged at construction saying the binding has no allowed IP after expansion, so its token is refused from every address — the binding is kept, so a plain `Tokens` entry for the same token is still not reached

#### Scenario: Plain token accepted

- **Given** `Tokens = ["T"]` with no bindings and `Enabled = true`
- **When** `ValidateToken("T", "10.0.0.9")` is called
- **Then** it returns `true`

### Requirement: Configured tokens and IPs may be `${ENV_VAR}` references, resolved once at construction

The system SHALL expand any value of the exact form `${NAME}` via
`Environment.GetEnvironmentVariable(NAME)`, falling back to the literal `${NAME}` text when the variable is
unset, and SHALL treat a malformed reference with an empty or whitespace name (e.g. `"${}"`) as a literal
without querying the environment. Values that are not wrapped in `${…}` SHALL pass through unchanged.
Expansion SHALL happen once, in the constructor, under a write lock, discarding any token or IP that expands
to null/whitespace; `ValidateToken` reads the snapshot under a read lock. There is no reload API — a later
environment or configuration change has no effect on an existing instance.

#### Scenario: Token supplied by the environment

- **Given** `Tokens = ["${API_TOKEN}"]` and `API_TOKEN=s3cret` set before construction
- **When** `ValidateToken("s3cret", ip)` is called
- **Then** it returns `true`

#### Scenario: Unset variable stays literal

- **Given** `Tokens = ["${MISSING}"]` with `MISSING` unset
- **When** `ValidateToken("${MISSING}", ip)` is called
- **Then** it returns `true`, because the unexpanded literal was cached as the valid token

#### Scenario: Malformed reference

- **Given** the value `"${}"`
- **When** `ExpandEnvironmentVariable("${}")` is called
- **Then** `"${}"` is returned and no environment lookup occurs

#### Scenario: Late environment change is not observed

- **Given** a service constructed while `API_TOKEN` was unset
- **When** `API_TOKEN` is set afterwards and `ValidateToken("s3cret", ip)` is called
- **Then** it returns `false`

### Requirement: The service releases its lock and believes forwarding headers only from trusted proxies

The system SHALL implement `IDisposable` on `AuthenticationService`, disposing the
`ReaderWriterLockSlim` once and idempotently.

`GetClientIpAddress(getHeaderValue, fallbackIp, trustedProxies)` SHALL return the connection's own address
(`fallbackIp`, unchanged) whenever `trustedProxies` is null or empty, or `fallbackIp` is not one of them —
the forwarding headers are then not read at all. Only when the peer is trusted SHALL it consult, in order:
`X-Forwarded-For`, walked from the **right**, returning the first hop that is not itself a trusted proxy (or
the leftmost hop when every hop is trusted); then `X-Real-IP`, trimmed; then `CF-Connecting-IP`, trimmed;
and finally `fallbackIp`. A trusted-proxy entry is either an address, compared after mapping IPv4-mapped IPv6
to IPv4, or a CIDR range (it contains `/`), matched against the address and its IPv4-mapped form; an address
given with a port (`10.0.0.5:51234`, `[::1]:51234`) is parsed to its address; an unparseable address is never
trusted. The instance method `ResolveClientIp(getHeaderValue, connectionIp)` SHALL apply the configuration's
`AuthenticationConfiguration.TrustedProxies` (empty by default), expanded once at construction like tokens
(`${VAR}` references, blank results dropped). The REST, SOAP, SSE and WebSocket authentication wrappers resolve the
client IP through `ResolveClientIp`.

#### Scenario: Proxy chain behind a trusted proxy

- **Given** `X-Forwarded-For: 203.0.113.7, 10.0.0.1`, a fallback of `10.0.0.1` and trusted proxies `["10.0.0.0/8"]`
- **When** `GetClientIpAddress` is called
- **Then** `"203.0.113.7"` is returned — `10.0.0.1` is skipped as a trusted hop

#### Scenario: Forged header from an untrusted peer

- **Given** a direct connection from `198.51.100.9` sending `X-Forwarded-For: 10.0.0.1`, and no trusted proxies configured
- **When** `ResolveClientIp` is called
- **Then** `"198.51.100.9"` is returned, so the forged address cannot satisfy a token binding's allow-list

#### Scenario: Client-written left end is not believed

- **Given** trusted proxies `["10.0.0.5"]`, a fallback of `10.0.0.5` and `X-Forwarded-For: 10.0.0.1, 203.0.113.7`
- **When** `GetClientIpAddress` is called
- **Then** `"203.0.113.7"` is returned — the rightmost hop no trusted proxy vouches for, not the `10.0.0.1` the client prepended

#### Scenario: No forwarding headers

- **Given** a trusted peer `"127.0.0.1"`, all three headers absent and a fallback of `"127.0.0.1"`
- **When** `GetClientIpAddress` is called
- **Then** `"127.0.0.1"` is returned

#### Scenario: Double dispose

- **Given** a disposed `AuthenticationService`
- **When** `Dispose()` is called again
- **Then** it returns without touching the already-disposed lock

### Requirement: An inbound OIDC id token is refused, never waved through, whenever it cannot be verified

The system SHALL make `OidcIdTokenVerifier.VerifyAsync` return a refusal with a distinct
`OidcVerificationOutcome` for every reason it cannot produce a verified identity, and SHALL never throw for
untrusted input: `ProviderNotConfigured` for a null/whitespace provider name, an unknown provider name, or a
provider whose `OidcProviderOptions.FirstMissingSetting()` reports a missing `ClientId`, `Issuer` or
`JwksUri`; `NoTokenSupplied` for a null/whitespace token; `SigningKeysUnavailable` when no key could be
obtained; `TokenInvalid` when validation failed or the validated token is not a `JsonWebToken`; and
`SubjectMissing` when the verified payload carries no non-whitespace `sub`. `Reason` SHALL be a log-safe
string that never contains token or key material, and `Identity` SHALL be non-null exactly when
`IsVerified` is true.

#### Scenario: A bare provider key is not proof

- **Given** a configured `"google"` provider and a caller that supplies the provider name but no id token
- **When** `VerifyAsync("google", null)` is called
- **Then** the outcome is `NoTokenSupplied` and `Identity` is null

#### Scenario: No providers configured at all

- **Given** a verifier constructed with an empty provider map
- **When** `VerifyAsync("google", someToken)` is called
- **Then** the outcome is `ProviderNotConfigured` with the reason "no OIDC providers are configured"

#### Scenario: Half-configured provider

- **Given** a `"google"` entry whose `JwksUri` is empty
- **When** `VerifyAsync("google", validToken)` is called
- **Then** the outcome is `ProviderNotConfigured` and the reason names `JwksUri`

#### Scenario: JWKS unreachable is not the same as a bad token

- **Given** a fully configured provider whose key source returns an empty collection
- **When** `VerifyAsync` is called with any token
- **Then** the outcome is `SigningKeysUnavailable`, not `TokenInvalid`

#### Scenario: Token without a subject

- **Given** a correctly signed, in-date token for the right audience that omits `sub`
- **When** `VerifyAsync` is called
- **Then** the outcome is `SubjectMissing`

### Requirement: OIDC id-token validation is asymmetric-only and audience-bound

The system SHALL validate an id token with `ValidateIssuer = true` against `OidcProviderOptions.Issuer`,
`ValidateAudience = true` against `ClientId` plus every non-whitespace entry of `AdditionalAudiences`,
`ValidateIssuerSigningKey = true` against the fetched keys, `RequireSignedTokens = true`,
`ValidateLifetime = true`, `RequireExpirationTime = true`, and a `ValidAlgorithms` allow-list restricted to
RS256/384/512, PS256/384/512 and ES256/384/512 — so `HS*` and `none` are rejected, closing the
algorithm-confusion path where a provider's public key is used as an HMAC secret. Clock skew SHALL default
to `DefaultClockSkew` (2 minutes) and be overridable.

#### Scenario: HS256-signed token using the provider's public key as the secret

- **Given** a token forged with `alg: HS256` and the provider's published RSA public key as the HMAC secret
- **When** `VerifyAsync` is called
- **Then** the outcome is `TokenInvalid` — `HS256` is not in `AllowedAlgorithms`

#### Scenario: Token minted for another relying party

- **Given** a genuine, correctly signed provider token whose `aud` is a different client id
- **When** `VerifyAsync` is called
- **Then** the outcome is `TokenInvalid`

#### Scenario: Sibling native-app audience explicitly widened

- **Given** `AdditionalAudiences = ["native-client-id"]`
- **When** a token with `aud: "native-client-id"` is verified
- **Then** it verifies successfully

#### Scenario: Token with no exp

- **Given** a signed token with the right issuer and audience but no `exp` claim
- **When** `VerifyAsync` is called
- **Then** the outcome is `TokenInvalid`, because `RequireExpirationTime` is true

### Requirement: A rotated provider key triggers exactly one forced key refresh

The system SHALL, when validation fails with `SecurityTokenSignatureKeyNotFoundException`, retry validation
exactly once with `forceRefresh = true` so a provider's key rotation does not lock users out until the cache
expires. The system SHALL report "keys were available" separately from "the token validated", so an
unavailable key set is never reported as an invalid token.

#### Scenario: Key rotated since the last fetch

- **Given** a cached key set that predates the provider's rotation and a token signed with the new key
- **When** `VerifyAsync` is called
- **Then** the first attempt fails with an unknown `kid`, a forced refresh fetches the new key set, and the token verifies

#### Scenario: Refresh also yields nothing

- **Given** a cached key set that does not contain the token's `kid` and a JWKS endpoint that is down
- **When** `VerifyAsync` is called
- **Then** the retry still has keys (the stale cached set) so the outcome is `TokenInvalid`

### Requirement: The verified identity is derived only from the verified payload

The system SHALL read `sub` from the validated `JsonWebToken` payload rather than from the mapped
`ClaimsIdentity`, so no inbound claim-type mapping can shadow it, and SHALL return it trimmed as
`VerifiedOidcIdentity.Subject`. The system SHALL set `Provider` to the canonical form
(`provider.Trim().ToLowerInvariant()`), lower-case and trim `email` (null when absent or whitespace), read
`email_verified` as either a JSON boolean or a parseable string and default it to `false`, and take
`DisplayName` from `name`, else `preferred_username`, else null.

#### Scenario: Provider name canonicalised

- **Given** `VerifyAsync("GOOGLE", token)` against a configured `"google"` provider
- **When** verification succeeds
- **Then** `Identity.Provider` is `"google"`

#### Scenario: email_verified sent as a string

- **Given** a token whose payload contains `"email_verified": "true"`
- **When** verification succeeds
- **Then** `Identity.EmailVerified` is true

#### Scenario: email_verified absent

- **Given** a token with an `email` claim but no `email_verified`
- **When** verification succeeds
- **Then** `Identity.EmailVerified` is false — the framework never asserts verification on the provider's behalf

#### Scenario: Mixed-case email

- **Given** a token with `"email": " User@Example.COM "`
- **When** verification succeeds
- **Then** `Identity.Email` is `"user@example.com"`

### Requirement: Provider names are matched by their canonical form and duplicates fail at construction

The system SHALL always re-key the supplied provider map by `Canonical(name)` (trim + lower-case invariant)
under an ordinal comparer, whatever comparer the caller's dictionary uses, so `"Google"`, `"google"` and
`" Google "` resolve identically. A map that holds two spellings of the same canonical name SHALL therefore
throw at construction rather than silently preferring one at login time. A null key source or null map SHALL
throw `ArgumentNullException`.

The lookup SHALL use the canonical form of the supplied name — the same form used for the key-source cache
key and the returned identity — so matching is both case- and surrounding-whitespace-insensitive.

#### Scenario: Case-insensitive lookup from an ordinal dictionary

- **Given** an ordinal dictionary containing only `"google"`
- **When** the verifier is constructed and `VerifyAsync("GOOGLE", token)` is called
- **Then** the `"google"` options are used

#### Scenario: Surrounding whitespace in the supplied name

- **Given** a configured `"google"` provider
- **When** `VerifyAsync("  google  ", validToken)` is called
- **Then** the `"google"` options are used, not `ProviderNotConfigured`

#### Scenario: Two spellings of one provider

- **Given** an ordinal dictionary containing both `"Google"` and `"google"` (or `"google"` and `" google"`)
- **When** `new OidcIdTokenVerifier(keySource, providers)` is constructed
- **Then** the re-keying `ToDictionary` throws for the duplicate key, at construction

### Requirement: JWKS key sets are cached per provider and served stale on failure for a bounded time

The system SHALL make `HttpOidcSigningKeySource.GetSigningKeysAsync` return the cached key set while it is
younger than the cache duration (`DefaultCacheDuration` = 1 hour, overridable) unless `forceRefresh` is
requested. It SHALL fetch `OidcProviderOptions.JwksUri` and cache only a **non-empty** result — a JWKS that
parses but exposes no signing keys SHALL be returned without being cached, and SHALL set `LastFetchError`.
On any exception other than `OperationCanceledException` it SHALL record `LastFetchError` and return the
previously cached (stale) key set only while that set is younger than the maximum stale age
(`DefaultMaxStaleAge` = 24 hours, overridable), otherwise an empty collection; `OperationCanceledException`
SHALL propagate. `LastServedStale` SHALL be true exactly when the last answer was a stale set served because
a fetch failed. Ages SHALL be measured with the injectable `IDateTimeProvider` (the system clock by
default). Provider keys SHALL be cached under a case-insensitive key.

#### Scenario: Second call inside the window does not hit the network

- **Given** a successful fetch 5 minutes ago and a 1-hour cache duration
- **When** `GetSigningKeysAsync(provider, options, forceRefresh: false)` is called
- **Then** the cached keys are returned and no HTTP request is made

#### Scenario: Transient network failure

- **Given** a cached key set and a JWKS endpoint that now returns 503
- **When** the cache has expired and keys are requested
- **Then** `LastFetchError` is set to the thrown exception and the stale cached keys are returned
- **And** `LastServedStale` is true

#### Scenario: Endpoint down past the maximum stale age

- **Given** a key set cached 25 hours ago, the default 24-hour maximum stale age, and a JWKS endpoint that fails
- **When** keys are requested
- **Then** an empty collection is returned and `LastServedStale` is false, so a key the provider revoked while the endpoint was down stops being accepted

#### Scenario: First fetch ever fails

- **Given** an empty cache and an unreachable JWKS endpoint
- **When** keys are requested
- **Then** an empty collection is returned, which the verifier turns into `SigningKeysUnavailable`

#### Scenario: JWKS with no usable keys is not cached

- **Given** a JWKS document containing only encryption keys
- **When** keys are requested twice
- **Then** both calls hit the endpoint, so a corrected document takes effect immediately rather than after the TTL

#### Scenario: Cancellation

- **Given** a cancelled `CancellationToken`
- **When** keys are requested
- **Then** `OperationCanceledException` propagates and `LastFetchError` is not overwritten

### Requirement: Any secret provider exposes multi-field secrets through a single default projection

The system SHALL give `ISecretProvider` a default `GetSecretPairsAsync` that calls `GetSecretAsync` and
returns either null (key absent) or a single-entry dictionary `{ "value" → secret }`. Implementations with
genuinely multi-field secrets SHALL override it. `AzureKeyVaultSecretProvider` SHALL re-declare that same
single-entry body as a public member so it is callable on the concrete type as well as through the
interface.

#### Scenario: Single-valued provider

- **Given** any `ISecretProvider` that does not override the member and a stored secret `"db/password" = "pw"`
- **When** `GetSecretPairsAsync("db/password")` is called
- **Then** `{ ["value"] = "pw" }` is returned

#### Scenario: Missing key

- **Given** a key that does not exist
- **When** `GetSecretPairsAsync` is called
- **Then** null is returned, distinguishing "absent" from "present but empty"

### Requirement: The Vault provider speaks KV v1 and KV v2 with per-request credentials

The system SHALL build every Vault request as an absolute URI against `VaultSettings.Address` (trailing
slash normalised) and attach `X-Vault-Token` and `X-Vault-Namespace` as **per-request** headers, so a
caller-supplied `HttpClient` is never mutated; only a client the provider created itself SHALL have its
`Timeout` set from `TimeoutSeconds`. Read/write paths SHALL be `{MountPath}/data/{key}` for
`KvVersion == 2` and `{MountPath}/{key}` otherwise; delete SHALL use `{MountPath}/metadata/{key}` for v2 and
`{MountPath}/{key}` otherwise; write payloads SHALL be `{ data: { value } }` for v2 and `{ value }` for v1.
A `404` SHALL yield null from the read paths and SHALL be tolerated by delete; any other non-success status
SHALL throw via `EnsureSuccessStatusCode`.

The system SHALL place the key (and a list path) into the URL one `/`-separated segment at a time, each
escaped with `Uri.EscapeDataString`, so `/` keeps its meaning of nesting and every other character is data. A
key with an empty, `.` or `..` segment SHALL be refused with `ArgumentException` before any request is sent,
because dot segments are resolved by `new Uri(base, path)` and would address another Vault endpoint. A list
path has its trailing `/` trimmed first (a folder name from a listing ends with one), and an empty list path
lists the mount root.

#### Scenario: Path traversal in a key is refused

- **Given** any `VaultSecretProvider`
- **When** `DeleteSecretAsync("../../sys/policy/x")` is called
- **Then** `ArgumentException` is thrown and no request reaches Vault

#### Scenario: Query characters in a key are data

- **Given** KV v2
- **When** `GetSecretAsync("app/a?b")` is called
- **Then** the request path is `{mount}/data/app/a%3Fb` — the `?` does not start a query string

#### Scenario: Two providers over one shared HttpClient

- **Given** one `HttpClient` injected into two `VaultSecretProvider` instances with different tokens
- **When** both issue requests
- **Then** each request carries its own `X-Vault-Token` and neither provider changed the client's `Timeout` or default headers

#### Scenario: Deleting an absent secret

- **Given** a key that does not exist
- **When** `DeleteSecretAsync(key)` is called
- **Then** the 404 is swallowed and the call completes successfully

#### Scenario: Absent secret read

- **Given** a key that does not exist
- **When** `GetSecretAsync(key)` is called
- **Then** null is returned

#### Scenario: Health probe never throws

- **Given** an unreachable Vault address
- **When** `IsHealthyAsync()` is called
- **Then** `false` is returned

### Requirement: Vault responses are parsed defensively for KV v2 and KV v1

The system SHALL, for `KvVersion == 2`, tolerate a `200` whose body lacks `data` or the inner `data` node by
returning a `SecretResult` with an empty `Value` (from `GetSecretWithMetadataAsync`) or null (from
`GetSecretPairsAsync`). `Version`, `CreatedAt` and custom metadata SHALL be read from `data.metadata` when
present, and `UpdatedAt` SHALL be populated only from a real `updated_time`/`mtime` field — never copied from
`created_time`. For `KvVersion == 1`, `ParseKv1Response` SHALL likewise return a `SecretResult` with an empty
`Value` when the body lacks `data` or `data` is not a JSON object.

#### Scenario: Malformed KV2 body

- **Given** `KvVersion = 2` and a 200 response body of `{}`
- **When** `GetSecretWithMetadataAsync(key)` is called
- **Then** a `SecretResult` with `Value == ""` is returned

#### Scenario: Malformed KV1 body

- **Given** `KvVersion = 1` and a 200 response body of `{}`
- **When** `GetSecretWithMetadataAsync(key)` is called
- **Then** a `SecretResult` with `Value == ""` is returned and nothing is thrown

#### Scenario: No update timestamp in KV2 metadata

- **Given** KV2 metadata that carries only `created_time`
- **When** the secret is read
- **Then** `CreatedAt` is set and `UpdatedAt` is null

#### Scenario: Non-string field values are JSON-stringified

- **Given** a KV2 secret whose fields include `"port": 5432` and `"tls": null`
- **When** `GetSecretPairsAsync` is called
- **Then** `"port"` maps to `"5432"` and `"tls"` maps to `""`

### Requirement: The Azure Key Vault provider authenticates with cached client credentials

The system SHALL require a non-empty `VaultUri` at construction (`ArgumentException` otherwise) and SHALL,
on the first request, obtain an OAuth2 token from
`https://login.microsoftonline.com/{TenantId}/oauth2/v2.0/token` with `grant_type=client_credentials` and
scope `https://vault.azure.net/.default`, throwing `InvalidOperationException` when `TenantId`, `ClientId`
or `ClientSecret` is missing. It SHALL cache the token, reusing it until 5 minutes before its `expires_in`
expiry (defaulting to 3600 seconds when the response omits it), guarding the refresh with a
`SemaphoreSlim` double-check. It SHALL call the Key Vault REST API at api-version 7.4 with a
`Bearer` header, treat `404` as null (read) / empty (list) / success (delete), and dispose only an
`HttpClient` it created itself. `AzureKeyVaultSettings` SHALL alias `RemoteSettings` members —
`VaultUri`↔`Location`, `TenantId`↔`Name`, `ClientId`↔`UserName`, `ClientSecret`↔`Password`.

#### Scenario: Missing credentials surface at first use, not at construction

- **Given** settings with only `VaultUri` set
- **When** the provider is constructed and then `GetSecretAsync("k")` is called
- **Then** construction succeeds and the call throws `InvalidOperationException` naming TenantId, ClientId and ClientSecret

#### Scenario: Missing vault URI

- **Given** settings with an empty `VaultUri`
- **When** the provider is constructed
- **Then** `ArgumentException("VaultUri is required")` is thrown

#### Scenario: Token reuse

- **Given** a token obtained with `expires_in = 3600`
- **When** two secret reads happen a minute apart
- **Then** only one token request is issued

#### Scenario: Malformed secret id in a list response

- **Given** a list response whose `id` is a relative or malformed URI
- **When** `ListSecretsAsync()` is called
- **Then** that entry is skipped (name resolves to null) instead of a `UriFormatException` escaping

### Requirement: Azure Key Vault secret ids are recognised only as https URLs unless explicitly widened

The system SHALL treat a secret `id` returned by the vault as a secret id only when it parses as an absolute
URI **and** its scheme is `https`; `http` SHALL additionally be accepted only when
`AzureKeyVaultSettings.AllowInsecureSecretIds` is true (default `false`). Any other id — relative, malformed,
`file:`, `ftp:`, or `http:` with the switch off — SHALL yield a null secret name (the entry is skipped by
`ListSecretsAsync`) and a null `Version` (from `GetSecretWithMetadataAsync`), rather than an exception. When
`AllowInsecureSecretIds` is true the provider SHALL emit a `Trace.TraceWarning` at construction and SHALL
report it through `AzureKeyVaultSecretProvider.InsecureSecretIdsAllowed`. The switch affects only id parsing;
it does not change which URI the vault is reached over.

#### Scenario: A leading-slash id is not a secret id on any platform

- **Given** a list response whose `id` is `/secrets/relative-only`, which parses as an absolute `file:` URI on
  Linux
- **When** `ListSecretsAsync()` is called
- **Then** the entry is skipped, because its scheme is not `https` — the name `relative-only` is never
  returned

#### Scenario: An http secret id is rejected by default

- **Given** default settings and a list response whose `id` is `http://vault.local/secrets/name`
- **When** `ListSecretsAsync()` is called
- **Then** the entry is skipped

#### Scenario: The insecure switch opens http ids and announces itself

- **Given** settings with `AllowInsecureSecretIds = true`
- **When** the provider is constructed and `ListSecretsAsync()` returns an `http://…/secrets/name` id
- **Then** a trace warning was written at construction, `InsecureSecretIdsAllowed` is true, and `name` is
  returned

### Requirement: Azure Key Vault secret names are refused unless they are Key Vault names

The system SHALL, before building the `secrets/{name}` route for `GetSecretAsync`, `GetSecretWithMetadataAsync`,
`SetSecretAsync` and `DeleteSecretAsync`, require the name to be 1–127 characters of ASCII letters, digits
and `-`, throwing `ArgumentException` otherwise. The name is refused rather than escaped, because nothing
outside that alphabet names a secret, and an unescaped `/`, `..`, `?` or `&` would otherwise select a version
segment, another route, or extra query parameters.

#### Scenario: A slash would become the version segment

- **Given** any `AzureKeyVaultSecretProvider`
- **When** `DeleteSecretAsync("db/1234")` is called
- **Then** `ArgumentException` is thrown and no request is sent

#### Scenario: A valid name

- **Given** the name `App-Db-Password`
- **When** `GetSecretAsync("App-Db-Password")` is called
- **Then** the request goes to `{VaultUri}/secrets/App-Db-Password?api-version=7.4`

### Requirement: Secret listing scoping differs between the Vault and Azure Key Vault providers

The system SHALL make `VaultSecretProvider.ListSecretsAsync(path)` issue a `LIST` against
`{MountPath}[/metadata]/{path}` and return the server-side `data.keys` for that KV folder, whereas
`AzureKeyVaultSecretProvider.ListSecretsAsync(path)` SHALL always request the vault-wide
`secrets?api-version=7.4` collection and filter the extracted names client-side with
`StartsWith(path, OrdinalIgnoreCase)`. The `path` argument therefore means "KV folder" for Vault and "secret
name prefix" for Azure Key Vault. `ISecretProvider.IsHierarchical` SHALL state which: its default is `true`
(names are a tree and a listing answers children relative to the path), and `AzureKeyVaultSecretProvider`
SHALL answer `false` (names are flat and a listing answers full names).

The Azure Key Vault listing SHALL follow every `nextLink` until a page has none, accumulating the names of
all pages. It SHALL follow a `nextLink` only when it starts (case-insensitively) with the vault's own base
URI — any other throws `InvalidOperationException` without being requested, so the access token is never
sent elsewhere — and SHALL throw `InvalidOperationException` for a `nextLink` already read. A `404` on any
page SHALL end the listing with the names gathered so far.

#### Scenario: Listing a Vault folder

- **Given** KV v2 with secrets under `projects/myapp`
- **When** `ListSecretsAsync("projects")` is called
- **Then** the request is a `?list=true` GET on `{mount}/metadata/projects` and the returned names are that folder's children

#### Scenario: Listing an Azure Key Vault prefix

- **Given** a vault with secrets `App-Db`, `App-Api` and `Other`
- **When** `ListSecretsAsync("App-")` is called
- **Then** all secrets are enumerated over the wire and `{ "App-Db", "App-Api" }` is returned

#### Scenario: A vault larger than one page

- **Given** a first list page carrying `App-Db` and a `nextLink` on the same vault, whose page carries `App-Api` and no `nextLink`
- **When** `ListSecretsAsync("App-")` is called
- **Then** both pages are requested and `{ "App-Db", "App-Api" }` is returned

#### Scenario: A nextLink pointing outside the vault

- **Given** a list page whose `nextLink` is `https://elsewhere.example/secrets?...`
- **When** `ListSecretsAsync()` is called
- **Then** `InvalidOperationException` is thrown and no request is sent to that address

#### Scenario: Empty vault

- **Given** a vault that returns 404 for the list endpoint
- **When** `ListSecretsAsync()` is called on either provider
- **Then** an empty list is returned rather than an exception

### Requirement: Secrets projected into IConfiguration fail open

The system SHALL make `SecretConfigurationProvider.Load` and `LocalVaultConfigurationProvider.Load` catch
every exception — at the top level, while reading a path's pairs, and while listing a path's children — and
report it through the injectable `Action<string>` diagnostics sink (default `Console.WriteLine`), leaving the
affected configuration keys absent. A provider outage or auth failure therefore yields an empty
configuration section rather than a failed startup, and consumers must validate the presence of secrets they
require.

#### Scenario: Vault down at startup

- **Given** a registered secret configuration source whose provider throws on every call
- **When** the configuration is built
- **Then** the build succeeds, the diagnostics sink receives a message naming the path, and the keys are missing from configuration

#### Scenario: Listing fails but the path itself read fine

- **Given** a path whose pairs read successfully but whose child listing throws
- **When** `Load` runs
- **Then** the path's own keys are present, a listing warning is reported, and recursion stops there

### Requirement: Secret keys are flattened into configuration paths with a `--` separator convention

The system SHALL trim leading/trailing `/` from the configured path, prefix child keys with the accumulated
path prefix joined by `ConfigurationPath.KeyDelimiter`, and rewrite every `--` occurrence in the resulting
key to that delimiter — so a Vault field `Security--DevCertificate--Fingerprint` becomes
`config["Security:DevCertificate:Fingerprint"]`. `SecretConfigurationProvider` SHALL recurse into child
paths only when `recursive` is true (its default), skipping empty child names and trimming a trailing `/`
from each; `LocalVaultConfigurationProvider` SHALL always recurse.

For a provider whose `IsHierarchical` is false (Azure Key Vault), `SecretConfigurationProvider` SHALL NOT walk
children. It SHALL load the secret named by the path itself (when the path is non-empty) at the root prefix,
and — only when `recursive` is true — list once with the path as a name prefix and load every listed name
of the form `{path}--{rest}` (any name when the path is empty) at the prefix `rest` with each `--` read as a
level; a listed name that merely starts with the path without the `--` separator, or equals `{path}--`, is
skipped. A listing failure is reported through the diagnostics sink as
`"SecretConfiguration: warning listing '{path}': …"` and ends the load.

#### Scenario: Nested field name

- **Given** a secret path whose pairs include the key `Security--DevCertificate--Fingerprint`
- **When** the configuration is loaded
- **Then** `config["Security:DevCertificate:Fingerprint"]` holds its value

#### Scenario: Non-recursive registration

- **Given** `AddSecretConfiguration(provider, "app", recursive: false)`
- **When** the configuration is loaded
- **Then** only `app`'s own pairs are read and `ListSecretsAsync` is never called

#### Scenario: Nested folder becomes a nested key

- **Given** a KV folder `app` containing a child `db` with a field `password`
- **When** the recursive load runs
- **Then** `config["app:db:password"]`'s value is present — the child prefix is joined with the delimiter

#### Scenario: Flat provider under a path

- **Given** an Azure Key Vault provider holding `app--db--password = "pw"` and `apple = "x"`, registered with `AddSecretConfiguration(provider, "app")`
- **When** the configuration is loaded
- **Then** the vault is listed once, `config["db:password:value"]` is `"pw"` (the provider's single-field `value` pair under the level `db:password`), and `apple` is not loaded

### Requirement: LocalVault configuration is opt-in by environment variable and ordered by specificity

The system SHALL make `AddLocalVaultConfiguration` a no-op unless the `LOCAL_VAULT_ENABLED` environment
variable equals `"true"` case-insensitively, SHALL throw `ArgumentException` for a null/whitespace project
name, and SHALL report "LocalVault: token is empty — skipping." and return unchanged when no token was
resolved. Option values SHALL be resolved as "explicit value, else environment variable
(`LOCAL_VAULT_TOKEN` / `_ADDR` / `_USER` / `_DOMAIN` / `_ENVIRONMENT`), else fallback", with `Url` falling
back to `http://localhost:8200`, and `User`/`Domain`/`Environment` lower-cased. Sources SHALL be registered
in this order, so later ones override earlier: `projects/defaults`, `projects/defaults.{env}`,
`projects/{project}`, `projects/{project}.{env}`, and — only when a user is set —
`users/{user}/{project}`, `users/{user}/{project}.{env}`; each prefixed by `{domain}/` when a domain is set,
and `.{env}` variants emitted only when an environment is set. The project name SHALL be lower-cased.

#### Scenario: Disabled by default

- **Given** `LOCAL_VAULT_ENABLED` unset
- **When** `AddLocalVaultConfiguration(builder, "MyApp")` is called
- **Then** the builder is returned untouched and no Vault traffic occurs

#### Scenario: Enabled without a token

- **Given** `LOCAL_VAULT_ENABLED=true` and no token anywhere
- **When** the extension is called
- **Then** the diagnostics sink receives the skip message and no source is added

#### Scenario: Full scope

- **Given** `LOCAL_VAULT_ENABLED=true`, a token, `LOCAL_VAULT_DOMAIN=Acme`, `LOCAL_VAULT_ENVIRONMENT=Staging`, `LOCAL_VAULT_USER=Fero` and project `"MyApp"`
- **When** the extension is called
- **Then** six sources are added in order: `acme/projects/defaults`, `acme/projects/defaults.staging`, `acme/projects/myapp`, `acme/projects/myapp.staging`, `acme/users/fero/myapp`, `acme/users/fero/myapp.staging`

#### Scenario: No environment configured

- **Given** the same setup with `LOCAL_VAULT_ENVIRONMENT` unset
- **When** the extension is called
- **Then** the `.{env}` variants are omitted, leaving three sources

#### Scenario: Empty project name

- **Given** `LOCAL_VAULT_ENABLED=true`
- **When** `AddLocalVaultConfiguration(builder, "  ")` is called
- **Then** `ArgumentException("Project name is required.")` is thrown

### Requirement: Configuration-source registration guards its arguments

The system SHALL throw `ArgumentNullException` from `AddSecretConfiguration` (both overloads) and both
`AddVaultPath` overloads when the builder, the provider/client, or the paths enumerable is null, and from
`SecretConfigurationSource` / `LocalVaultConfigurationSource` / their providers when the provider or client
is null. A null path SHALL be normalised to the empty string rather than rejected.

#### Scenario: Null provider

- **Given** a valid builder
- **When** `AddSecretConfiguration(builder, null!, "app")` is called
- **Then** `ArgumentNullException` is thrown before any source is added

#### Scenario: Null path is the root

- **Given** a valid provider
- **When** a source is constructed with a null path
- **Then** the path becomes `""` and the provider loads from the root

### Requirement: Role and permission abstractions are user-id keyed and tenant-aware

The system SHALL define `IRoleProvider` (`GetRolesAsync`, `IsInRoleAsync`) and `IPermissionChecker`
(`HasPermissionAsync`, `GetPermissionsAsync`) keyed by a `Guid` user id, and
`IUserPermissionResolver.GetAsync(userId, tenantId)` for request-time resolution, whose `GetRolesAsync`
default returns an empty set so implementations may supply only permissions. `AuthorizationContext` SHALL
answer `IsInRole` / `HasPermission` by plain membership over its `Roles` / `Permissions` lists, with no
wildcard handling.

#### Scenario: AuthorizationContext has no wildcard semantics

- **Given** an `AuthorizationContext` whose `Permissions` is `["*"]`
- **When** `HasPermission("users:read")` is called
- **Then** `false` is returned — unlike `ClaimsPermissionChecker`, this type does not honour `"*"`

#### Scenario: Resolver that supplies permissions only

- **Given** an `IUserPermissionResolver` implementing just `GetAsync`
- **When** `GetRolesAsync(userId, tenantId)` is called
- **Then** the interface default returns an empty set

### Requirement: The grant role table is validated once and refuses anything undeclared

The system SHALL make `GrantOptions.Build()` produce an immutable `GrantRoleTable`, throwing
`GrantConfigurationException` (an `InvalidOperationException`) when a role, action or resource-type name is
null or whitespace, equals the reserved `"*"`, or contains `':'`; when a role or resource type is declared
twice; when a role allows no actions; when a resource type has an undefined `ResourceVisibility`; when no
resource type is declared; when `ShareAction` (default `"share"`) is allowed by no role; or when `OwnerRole`
(default `"owner"`) is undeclared or does not allow the share action. Names compare ordinally. A resource type
declared without a visibility SHALL be `ResourceVisibility.Granted`. Every later lookup of an undeclared role
(`ActionsOf`, `RequireRole`), action (`Allows`, `RequireAction`) or resource type (`VisibilityOf`,
`RequireResourceType`) SHALL throw `GrantConfigurationException` rather than answer "no". A role or type read back
from a STORED grant is looked up with the non-throwing `IsRole` / `IsResourceType` instead (TASK-641): a stored grant
whose role is no longer declared SHALL grant nothing, and `StaleGrantDetector.FindAsync(store, table)` SHALL list every
stored grant whose role or resource type is undeclared (the reserved admin grant excepted), so the retirement is
reported rather than thrown at the user it affects.

#### Scenario: A stored grant whose role was retired grants nothing

- **Given** a user holding a grant on `presentation` A whose role has since been removed from the table, a type-wide
  `viewer` grant, and `owner` on B
- **When** `CanAsync` asks about A or B, `ListResourcesAsync(user, "presentation", "read")` runs, or an owner of A
  replaces or revokes the stale grant
- **Then** nothing throws: the stale grant contributes no actions (A answers from the `viewer` grant alone), the
  listing leaves it out, and the owner needs only `share` and the new role's actions. `StaleGrantDetector` lists it
  with `RoleNotDeclared`. Measured before (TASK-641): `CanAsync` on A, every `CanAsync` of the type for a type-wide
  stale grant, and every listing of the type threw `GrantConfigurationException`

#### Scenario: A typo in an action name fails loudly

- **Given** a table whose roles allow `read`, `edit`, `delete` and `share`
- **When** `CanAsync(user, "presentation", id, "raed")` is called
- **Then** `GrantConfigurationException` is thrown — not `false`

#### Scenario: A colon inside a name

- **Given** `new GrantOptions().DefineRole("owner", "share", "b:c")`
- **When** `Build()` is called
- **Then** `GrantConfigurationException` is thrown, because `"{type}:{action}"` permission strings could otherwise collide across types

#### Scenario: The owner role cannot share

- **Given** `DefineRole("owner", "read")` and `DefineRole("admin", "read", "share")`
- **When** `Build()` is called
- **Then** `GrantConfigurationException` says the owner role must allow the share action

### Requirement: A grant check counts admins, type-wide grants and grants on exactly that resource

The system SHALL make `GrantService.CanAsync(user, type, resourceId, action, tenant)` return `true` when the
`IGrantAdminSource` (default `BootstrapAdminGrantSource`) says the user is an admin in that tenant scope, and
otherwise when the union of the actions of the user's grants on that type — type-wide grants
(`ResourceId == null`) plus grants whose `ResourceId` equals `resourceId` — contains the action. With
`resourceId` null only type-wide grants count. `ResourceVisibility` SHALL never change the answer. Nothing is
cached, so a revoke takes effect on the next check. A stored grant whose role is no longer declared makes the
check throw `GrantConfigurationException`.

#### Scenario: Type-wide grant

- **Given** a user holding `viewer` (allows `read`) on `presentation` with `ResourceId = null`
- **When** `CanAsync(user, "presentation", anyId, "read")` is called
- **Then** it returns `true`

#### Scenario: A grant on another resource does not count

- **Given** a user holding `editor` on presentation A only
- **When** `CanAsync(user, "presentation", B, "edit")` is called
- **Then** it returns `false`

#### Scenario: Create-style check with no resource

- **Given** a user holding `owner` on presentation A only
- **When** `CanAsync(user, "presentation", null, "edit")` is called
- **Then** it returns `false`, because only type-wide grants count when no resource is named

### Requirement: Grant queries scope tenants explicitly and render null as an explicit null test

The system SHALL take the tenant as an explicit argument on every `IGrantService` method and never read an
ambient tenant. Reads and checks (`ListForUserAsync`, `ListTypeWideForUserAsync`, `ListForResourceAsync`,
`HasAdminGrantAsync`) SHALL be "in scope": with a tenant, untenanted grants plus that tenant's; with none,
untenanted grants only — a tenant-bound grant never matches outside its tenant. A grant's identity
(`GetExactAsync`, update/delete by key) and the last-owner and last-admin counts SHALL be "exact": the tenant
and resource columns equal the arguments, null included. Every predicate SHALL be built by
`GrantPredicates` with a separate branch for a null argument, so a null is always an explicit `== null` term
and never a comparison against a null variable, which SQL would read as match-nothing.

#### Scenario: Untenanted grant applies in every tenant

- **Given** a user's untenanted `editor` grant on presentation A
- **When** `CanAsync(user, "presentation", A, "edit", tenantGuid: T)` is called
- **Then** it returns `true`

#### Scenario: Tenant-bound grant does not leak

- **Given** a user's `editor` grant on presentation A in tenant T1
- **When** `CanAsync(user, "presentation", A, "edit", tenantGuid: T2)` or `CanAsync(…, tenantGuid: null)` is called
- **Then** it returns `false`

### Requirement: Listing reports type-wide reach separately from exact ids

The system SHALL make `ListResourcesAsync(user, type, action, tenant)` return `GrantedResources(true, [])`
for an admin, and otherwise consider only the user's in-scope grants on that type whose role allows the
action: `AllOfType` is true when any of them is type-wide, and `Ids` lists the distinct resource ids of the
exact ones. `ListGranteesAsync(type, resourceId, tenant)` SHALL return every in-scope grant on that resource
or type-wide on its type, each flagged `IsTypeWide` when its `ResourceId` is null.

#### Scenario: Type-wide grant without the action

- **Given** a user holding a type-wide role on `presentation` that does not allow `read`, and `viewer` on presentation A
- **When** `ListResourcesAsync(user, "presentation", "read")` is called
- **Then** `AllOfType` is false and `Ids` is `[A]`

#### Scenario: Admin

- **Given** an admin
- **When** `ListResourcesAsync` is called
- **Then** `AllOfType` is true and `Ids` is empty — "no ids" does not mean "nothing"

### Requirement: Granting needs the share action and every action of the role, and never escalates

The system SHALL let a non-admin caller write or remove a grant on (type, resource, tenant) only when the
caller's own effective actions there include `ShareAction` — otherwise `GrantRefusedException` with
`GrantErrorCodes.NotAuthorized` — and include every required action — otherwise `GrantRefusedException`
with `GrantErrorCodes.Escalation` naming the missing ones. `AuthorizeGrantAsync` SHALL require the role's
actions; `GrantAsync` SHALL require the union of the new role's actions and, when the user already holds a
grant with that exact key, the replaced role's; `RevokeAsync` SHALL require the removed role's actions, except
that a caller removing their own grant needs nothing. An admin passes every rule, and for an admin the
required set is never resolved, so an admin can still act on an invitation whose role was retired. A replaced or
removed STORED role that is no longer declared requires no actions (it grants none, TASK-641), so a non-admin with
`share` may replace or remove such a grant too; a role the caller passes in is still refused when undeclared.
`GrantAsync` SHALL create the grant (`GrantedBy` = caller, `CreatedAt` from the clock) or, when the key
exists with a different role, update `Role` and `GrantedBy` by the key — every row with that key; the same
role writes nothing. `RevokeAsync` SHALL delete every row with the key and return `false` when none existed.
`Guid.Empty` as a resource id SHALL be refused with `ArgumentException` (null means type-wide). The reserved
admin grant cannot be written or removed here, since `"*"` is never a declared type.

#### Scenario: Editor tries to make someone an owner

- **Given** a caller holding a role with `read`, `edit` and `share` on presentation A, and `owner` allowing also `delete`
- **When** `GrantAsync(caller, other, "presentation", A, "owner")` is called
- **Then** `GrantRefusedException` with `Code = "Grant.Escalation"` is thrown, naming `delete`

#### Scenario: Caller without share

- **Given** a caller holding `editor` (no `share`) on presentation A
- **When** `GrantAsync(caller, other, "presentation", A, "viewer")` is called
- **Then** `GrantRefusedException` with `Code = "Grant.NotAuthorized"` is thrown

#### Scenario: Leaving a resource

- **Given** a user holding `viewer` on presentation A and no share right
- **When** `RevokeAsync(user, user, "presentation", A)` is called
- **Then** the grant is deleted and `true` is returned

### Requirement: A resource is never left without an owner

The system SHALL refuse, with `GrantRefusedException` and `GrantErrorCodes.LastOwner`, a `GrantAsync` that
replaces an owner's role on a specific resource, or a `RevokeAsync` that removes an owner grant on a specific
resource, when the exact-key count of owner grants there is at most one. Because that count cannot be
serialised across processes, the system SHALL re-count after the write with `CancellationToken.None` and,
finding zero owners, compensate — restore the previous role and `GrantedBy`, or re-create the deleted grant
as a new record with a fresh identity — and then throw `LastOwner`. Type-wide owner grants are not protected
by this rule.

#### Scenario: Sole owner demotes themselves

- **Given** presentation A whose only owner is U
- **When** `GrantAsync(U, U, "presentation", A, "viewer")` is called
- **Then** `GrantRefusedException` with `Code = "Grant.LastOwner"` is thrown and U is still owner

#### Scenario: Concurrent removal in another process

- **Given** two owners of A, each removed at once by a different process, both passing the pre-check
- **When** the second delete finds zero owners on its re-count
- **Then** that process re-creates its deleted owner grant and throws `LastOwner`

### Requirement: Redeeming upgrades but never downgrades, and creators claim only unowned resources

The system SHALL make `GrantAtLeastAsync` return the existing grant unchanged, without authorizing or
writing, when its role's actions are a superset of the requested role's; throw `GrantRefusedException` with
`GrantErrorCodes.RoleConflict` when neither action set contains the other; and otherwise authorize and write
the requested role as `GrantAsync` would — the coverage decision and the write under one resource lock.
`GrantOwnerOnCreateAsync(user, type, resourceId, tenant)` SHALL perform no authorization check, refuse
`Guid.Empty` with `ArgumentException`, return an existing owner grant of that user unchanged, refuse with
`GrantErrorCodes.NotAuthorized` when the resource already has any other owner, upgrade the user's existing
non-owner grant to the owner role, or else create an owner grant with `GrantedBy = null`.

#### Scenario: Invitation for less than the user holds

- **Given** a user who is `owner` of A and an invitation granting `viewer` on A
- **When** `GrantAtLeastAsync(inviter, user, "presentation", A, "viewer")` runs
- **Then** the owner grant is returned unchanged and nothing is written

#### Scenario: Claiming someone else's resource

- **Given** presentation A owned by U
- **When** `GrantOwnerOnCreateAsync(V, "presentation", A)` is called
- **Then** `GrantRefusedException` with `Code = "Grant.NotAuthorized"` is thrown

### Requirement: Admins hold a reserved grant, and the last admin of a scope cannot be removed

The system SHALL treat a user as an admin, through `BootstrapAdminGrantSource`, when they hold the reserved
grant `{ResourceType = "*", ResourceId = null, Role = "*"}` in tenant scope — an untenanted one is an admin in
every tenant. `AdminGrantService.RevokeAdminAsync(caller, user, tenant)` SHALL require the caller to be an
admin in that scope unless removing themselves (`GrantErrorCodes.NotAuthorized` otherwise), return `false`
when the user holds no admin grant with exactly that tenant, and refuse with `GrantErrorCodes.LastAdmin` when
the admins administering the scope — the exact-tenant admin grants plus, for a tenant, the untenanted ones —
number at most one; after deleting it SHALL re-count with `CancellationToken.None` and, finding none,
re-create the grant and throw `LastAdmin`.

#### Scenario: Tenant admin removable while a global admin exists

- **Given** tenant T with one tenant admin A and one untenanted admin G
- **When** `RevokeAdminAsync(G, A, T)` is called
- **Then** A's grant is removed and `true` is returned

#### Scenario: Last global admin

- **Given** a single untenanted admin G
- **When** `RevokeAdminAsync(G, G, null)` is called
- **Then** `GrantRefusedException` with `Code = "Grant.LastAdmin"` is thrown

### Requirement: Invitations are authorized like a grant and store only the token's hash

The system SHALL make `InvitationService.CreateAsync` normalise the email by trimming and folding only ASCII
`A–Z` to lower case, refuse with `ArgumentException` an email that is not ASCII-printable without spaces or
does not have exactly one `@` with text on both sides, require the type and role to be declared, authorize
exactly as `AuthorizeGrantAsync`, and store an `Invitation` whose `TokenHash` is the upper-case hex SHA-256 of
a fresh 32-byte base64url token and whose `ExpiresAt` is `now + GrantSignInOptions.InvitationLifetime`
(default 7 days; a non-positive lifetime throws `GrantConfigurationException` at construction). The plaintext
token SHALL be returned once, in `InvitationCreated`, and never stored. `RevokeAsync` SHALL return `false` for
an absent invitation or one that is redeemed, revoked or expired; the inviter may revoke without further
checks and anyone else must pass `AuthorizeGrantAsync` for its role and resource. It SHALL set `RevokedAt`
with a filtered update conditional on the invitation still being unredeemed and unrevoked, and report the
re-read state, not the intent.

#### Scenario: Non-ASCII email refused

- **Given** the email `"Kate@example.com"` (KELVIN SIGN)
- **When** `CreateAsync` is called
- **Then** `ArgumentException` is thrown, so no other mailbox can fold onto this one

#### Scenario: Token is returned once

- **Given** a successful `CreateAsync`
- **When** the stored invitation is read
- **Then** it carries only `TokenHash`, equal to `SHA256(token)` in upper-case hex

### Requirement: An invitation is redeemed once, for its own verified email, as its inviter

The system SHALL redeem an invitation under a per-invitation lock by first checking the email — a mismatch
answers `EmailMismatch` before revealing whether it is used, revoked or expired — then its state
(`InvitationRevoked`, `InvitationUsed`, `InvitationExpired` when `ExpiresAt <= now`), then claiming it with a
filtered update that sets `RedeemedAt`/`RedeemedBy` only where it is still unredeemed and unrevoked, and
re-reading to see whose claim stuck. It SHALL then call `GrantAtLeastAsync` as the inviter, so the grant rules
run again at redemption. A `GrantRefusedException` SHALL be reported as its code, a
`GrantConfigurationException` as `InvitationInvalid`, and in both cases — and on cancellation, which is
rethrown — the claim SHALL be released. Single use across processes holds only on a backend that runs a
filtered property update as one conditional statement (SQL, MongoDB); file and in-memory stores get
single use within one process only.

#### Scenario: Inviter has lost the share right

- **Given** a pending invitation whose inviter is not an admin and no longer holds `share` on the resource
- **When** the invitee signs in with the verified email
- **Then** the invitation is reported as skipped with `Grant.NotAuthorized` and is left pending

#### Scenario: Retired role

- **Given** a pending invitation whose role was removed from the role table
- **When** it is redeemed
- **Then** it is skipped with `Invitation.Invalid` and nothing is thrown

### Requirement: Sign-in makes bootstrap admins and redeems invitations in a fixed order

The system SHALL make `GrantSignInService.OnSignInAsync` first ensure the untenanted admin grant for a
context whose `provider:subject` is listed in `GrantSignInOptions.BootstrapAdmins` (provider compared
trimmed and case-insensitively, subject trimmed and exactly; an entry without both parts throws
`GrantConfigurationException` at construction; writing is idempotent and reported as `AdminGrantWritten`);
then redeem every pending, unexpired invitation for the normalised email except the token's own, skipping
each with `EmailNotVerified` when `EmailVerified` is false and reporting other refusals in `Skipped`; and
last redeem the explicit `InvitationToken`, throwing `GrantRefusedException` — `InvitationNotFound`,
`EmailNotVerified`, or the redemption's code — only for that token, after everything else is written.

#### Scenario: Dead link does not cost the other invitations

- **Given** two pending invitations for the user's verified email and an `InvitationToken` matching nothing
- **When** `OnSignInAsync` runs
- **Then** both invitations are redeemed and written, and then `GrantRefusedException` with `Code = "Invitation.NotFound"` is thrown

#### Scenario: Bootstrap admin by external identity

- **Given** `BootstrapAdmins = ["GitHub:4821337"]`
- **When** a user signs in with `Provider = "github"`, `Subject = "4821337"`
- **Then** `IsBootstrapAdmin` is true and, on the first such sign-in, `AdminGrantWritten` is true

### Requirement: Grant writes on one key are serialised in process

The system SHALL serialise grant writes on one (type, resource, tenant) key, admin writes per tenant scope,
and redemption/revocation per invitation through a process-wide `KeyedLock` shared by every service
instance; an entry SHALL exist only while it is held or awaited and be removed on the last release, and a
cancelled wait SHALL leave no entry behind. Across processes the post-write re-counts above are the only
protection.

#### Scenario: Two services over one store

- **Given** two `GrantService` instances constructed with the public constructor
- **When** both grant on the same resource at once
- **Then** the writes run one after the other, and afterwards no lock entry remains

### Requirement: Grants persist on any SQL provider with their lookup indexes

The system SHALL provide `SqlResourceGrantStore<DB>` and `SqlInvitationStore<DB>`, bare
`AsyncDataBaseBulkStore` subclasses whose lookups are the store interfaces' default methods, and
`GrantSqlMappings.Register`, which maps `ResourceGrant` to table `BirkoResourceGrants` (required `UserGuid`,
`ResourceType`, `Role`) and `Invitation` to `BirkoInvitations` (`TokenHash` required and declared unique;
required `Email`, `ResourceType`, `Role`). `GrantSqlIndexes.EnsureAsync(grants, invitations, indexManager)`
SHALL initialise both stores and create, when absent, `IX_BirkoResourceGrants_User` (UserGuid, ResourceType),
`IX_BirkoResourceGrants_Resource` (ResourceType, ResourceId) and `IX_BirkoInvitations_Email` (Email) through
the caller-supplied provider index manager, throwing `ArgumentNullException` for a null argument and
`InvalidOperationException` for a store without a connector. No unique index is declared on a grant's
(user, type, resource, tenant) key, which is why updates and deletes go by the key rather than by the entity.

#### Scenario: Idempotent index ensure

- **Given** the three indexes already present
- **When** `EnsureAsync` runs again
- **Then** `ExistsAsync` answers true for each and nothing is created

#### Scenario: No index manager

- **Given** valid stores
- **When** `EnsureAsync(grants, invitations, null!)` is called
- **Then** `ArgumentNullException` is thrown before either store is initialised
