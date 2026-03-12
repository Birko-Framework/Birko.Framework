# Birko.Security

## Overview
Core security framework — password hashing, encryption, token provider interfaces, static token authentication, and RBAC authorization interfaces.

Includes the former `Birko.Communication.Authentication` (moved here — all Communication projects updated to use `Birko.Security.Authentication` namespace).

## Structure
```
Birko.Security/
├── Core/
│   ├── IPasswordHasher.cs            - Hash(password) → string, Verify(password, hash) → bool
│   ├── IEncryptionProvider.cs         - Encrypt/Decrypt byte[] and string, AES-256-GCM
│   └── ITokenProvider.cs             - GenerateToken/ValidateToken, TokenResult, TokenOptions
├── Authentication/                    ← Moved from Birko.Communication.Authentication
│   ├── AuthenticationService.cs       - Static token validation + IP binding, thread-safe
│   ├── AuthenticationConfiguration.cs - Enabled, Tokens[], TokenBindings[]
│   └── TokenBinding.cs               - Token + AllowedIps
├── Authorization/
│   └── IRoleProvider.cs               - IRoleProvider, IPermissionChecker, AuthorizationContext
├── Hashing/
│   └── Pbkdf2PasswordHasher.cs        - PBKDF2-SHA512, 600k iterations, self-contained hash format
└── Encryption/
    └── AesEncryptionProvider.cs        - AES-256-GCM, nonce+tag embedded in output
```

## Dependencies
- `Microsoft.Extensions.Logging` — for AuthenticationService (optional logger)
- `System.Security.Cryptography` — built-in .NET, no NuGet

## Password Hashing
```csharp
var hasher = new Pbkdf2PasswordHasher();
var hash = hasher.Hash("mypassword");  // "PBKDF2-SHA512:600000:base64salt:base64hash"
var valid = hasher.Verify("mypassword", hash);  // true
```

## Encryption
```csharp
var provider = new AesEncryptionProvider();
var key = AesEncryptionProvider.GenerateKey();  // Random 256-bit key
var encrypted = provider.EncryptString("sensitive data", key);
var decrypted = provider.DecryptString(encrypted, key);
```

## Static Token Auth (from former Birko.Communication.Authentication)
```csharp
var config = new MyAuthConfig { Enabled = true, Tokens = ["${API_TOKEN}"] };
var service = new AuthenticationService(config);
service.ValidateToken("my-token", "192.168.1.1");
```

## Key Design Decisions
- IPasswordHasher.Hash returns self-contained string (algorithm:iterations:salt:hash) — no separate salt storage
- Pbkdf2PasswordHasher uses FixedTimeEquals to prevent timing attacks
- AesEncryptionProvider embeds nonce+tag in output — single byte[] for storage/transport
- IRoleProvider/IPermissionChecker are async (implementations will hit database)
- AuthorizationContext is a simple POCO — populated per-request from token claims
- AuthenticationService namespace changed from `Birko.Communication.Authentication` → `Birko.Security.Authentication`

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
