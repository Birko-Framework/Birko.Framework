# Birko.Security.NFC

NFC-based authentication for the Birko Framework. Maps NFC tag UIDs to user identities and optionally issues JWT tokens on successful authentication.

## Features

- **Tag-to-user mapping** — Enroll, revoke, and look up NFC cards per user
- **JWT integration** — Optionally issues JWT tokens via `ITokenProvider` on successful tap
- **UID normalization** — Handles hex with/without separators, case-insensitive
- **Tag limits** — Configurable max tags per user
- **Expiration** — Optional tag expiration dates
- **Usage tracking** — Records last authentication timestamp per tag
- **In-memory store** — Included for testing; implement `INfcTagMappingStore` for production

## Installation

Add the shared project reference to your `.csproj`:

```xml
<Import Project="..\Birko.Security.NFC\Birko.Security.NFC.projitems" Label="Shared" />
```

## Dependencies

- **Birko.Security** — `ITokenProvider`, `TokenResult`, `TokenOptions`

## Usage

### Basic authentication (no JWT)

```csharp
using Birko.Security.NFC;

var store = new InMemoryNfcTagMappingStore();
var settings = new NfcAuthSettings { IssueTokens = false };
var auth = new NfcAuthProvider(store, settings);

// Enroll a card
await auth.EnrollAsync(
    userId: Guid.Parse("..."),
    tagUid: "04A1B2C3D4E5F6",
    label: "Office badge",
    userName: "John Doe",
    email: "john@example.com"
);

// Authenticate
var result = await auth.AuthenticateAsync("04A1B2C3D4E5F6");
if (result.IsAuthenticated)
{
    Console.WriteLine($"Welcome, {result.UserName}!");
}
```

### With JWT token issuance

```csharp
using Birko.Security;
using Birko.Security.NFC;

var store = new InMemoryNfcTagMappingStore();
var tokenProvider = new JwtTokenProvider(); // from Birko.Security.Jwt
var tokenOptions = new TokenOptions
{
    Secret = "your-secret-key",
    Issuer = "your-app",
    ExpirationMinutes = 480
};

var auth = new NfcAuthProvider(store, tokenProvider: tokenProvider, tokenOptions: tokenOptions);

var result = await auth.AuthenticateAsync("04A1B2C3D4E5F6");
if (result.IsAuthenticated && result.Token != null)
{
    // Use result.Token.Token as Bearer token
    Console.WriteLine($"JWT: {result.Token.Token}");
    Console.WriteLine($"Expires: {result.Token.ExpiresAt}");
}
```

### End-to-end with NFC reader

```csharp
using Birko.Communication.NFC.Ports;
using Birko.Communication.NFC.Transports;
using Birko.Security.NFC;

// Setup reader
var readerSettings = new NfcReaderSettings { TransportType = "hid" };
using var transport = new HidNfcTransport();
var port = new NfcReaderPort(readerSettings, transport);

// Setup auth
var store = new InMemoryNfcTagMappingStore();
var auth = new NfcAuthProvider(store);

// Listen for badges
port.OnTagDetected += async (sender, tag) =>
{
    var result = await auth.AuthenticateAsync(tag.Uid);
    if (result.IsAuthenticated)
    {
        Console.WriteLine($"Access granted: {result.UserName}");
    }
    else
    {
        Console.WriteLine($"Access denied: {result.Error}");
    }
};

port.Open();
await port.StartPollingAsync();
```

### Managing tags

```csharp
// List user's tags
var tags = await auth.GetUserTagsAsync(userId);

// Revoke a specific tag
await auth.RevokeAsync("04A1B2C3D4E5F6");

// Revoke all tags for a user
await auth.RevokeAllAsync(userId);

// Check enrollment
bool enrolled = await auth.IsEnrolledAsync("04A1B2C3D4E5F6");
```

## API Reference

### Interfaces

| Interface | Description |
|-----------|-------------|
| `INfcAuthProvider` | Authentication: authenticate, enroll, revoke, query |
| `INfcTagMappingStore` | Persistence: CRUD for tag-to-user mappings |

### Classes

| Class | Description |
|-------|-------------|
| `NfcAuthProvider` | Default auth provider with in-memory or custom store |
| `InMemoryNfcTagMappingStore` | In-memory store for testing/development |
| `NfcTagMapping` | Tag-to-user mapping model (UID, UserId, label, expiration) |
| `NfcAuthResult` | Authentication result (success/failure, token, claims) |
| `NfcAuthSettings` | Configuration (token issuance, max tags, expiration, normalization) |

## Related Projects

- [Birko.Communication.NFC](../Birko.Communication.NFC/README.md) — NFC tag reading (transports, protocols)
- [Birko.Security](../Birko.Security/README.md) — Core security interfaces (ITokenProvider)
- [Birko.Security.Jwt](../Birko.Security.Jwt/README.md) — JWT token implementation
- [Birko.Security.AspNetCore](../Birko.Security.AspNetCore/README.md) — ASP.NET Core auth integration

## License

See [License.md](License.md) for details.
