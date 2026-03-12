# Birko.Security

Core security framework for the Birko Framework: password hashing, encryption, token providers, and RBAC.

## Features

- Password hashing (PBKDF2 via Pbkdf2PasswordHasher)
- AES-256-GCM encryption/decryption
- Token provider interfaces (ITokenProvider)
- Static token authentication (moved from Birko.Communication.Authentication)
- RBAC authorization interfaces

## Installation

```bash
dotnet add package Birko.Security
```

## Dependencies

- .NET 10.0

## Usage

### Password Hashing

```csharp
using Birko.Security;

var hasher = new Pbkdf2PasswordHasher();
var hash = hasher.Hash("password");
var isValid = hasher.Verify("password", hash);
```

### Encryption

```csharp
var encrypted = AesEncryption.Encrypt(plaintext, key);
var decrypted = AesEncryption.Decrypt(encrypted, key);
```

### Token Provider

```csharp
ITokenProvider provider = GetTokenProvider();
var token = await provider.GenerateTokenAsync(claims);
var result = await provider.ValidateTokenAsync(token);
```

## API Reference

- **Pbkdf2PasswordHasher** - PBKDF2 password hashing
- **AesEncryption** - AES-256-GCM encrypt/decrypt
- **ITokenProvider** - Token generation and validation interface
- **StaticTokenAuthenticator** - Static token authentication

## Related Projects

- [Birko.Security.Jwt](../Birko.Security.Jwt/) - JWT token implementation

## License

Part of the Birko Framework.
