# Birko.Models.Users

User, authentication, profile, role-based access control, and agenda management models for the Birko Framework.

## Features

- User model with unique username and email, active/verified flags
- Multi-provider authentication (local, Google, Apple, Microsoft, Facebook, GitHub)
- User profile with personal info, locale, timezone (1:1, GDPR-separated)
- Role-based access control with string permission codes (`{module}:{entity}:{action}`)
- Agenda model for application module/section segregation
- User-Role assignments optionally scoped to specific agendas
- Matching ViewModels with INotifyPropertyChanged
- Filter classes for all models with composable AND predicates

## Installation

Shared project — import in your `.csproj`:

```xml
<Import Project="..\Birko.Models.Users\Birko.Models.Users.projitems" Label="Shared" />
```

## Dependencies

- Birko.Data.Core (AbstractDatabaseLogModel, ILoadable, IDefault, IFilter)
- Birko.Data.Stores (`IAsyncBulkStore<T>`, `PropertyUpdate<T>`: the session service)
- Birko.Data.SQL (Table, UniqueField, PrecisionField, NamedField attributes)

## API Reference

### Models (Birko.Models.Users)

| Model | Description |
|-------|-------------|
| **User** | Core user: `UserName`, `Email`, `IsActive`, `LastLoginAt`, `EmailVerified` |
| **Agenda** | Application module: `Name`, `Description`, `Default`, `IsActive` |
| **UserAgenda** | User ↔ Agenda join: `UserGuid`, `AgendaGuid`, `IsOwner`, `JoinedAt` |
| **UserLogin** | Auth provider: `Provider`, `ProviderKey`, `PasswordHash`, `RefreshToken`, `IsVerified` |
| **UserProfile** | Profile (1:1): `FirstName`, `LastName`, `DisplayName`, `Phone`, `AvatarUrl`, `Locale`, `TimeZone`, `DateOfBirth`, `Bio` |
| **Role** | Named role: `Name`, `Description`, `IsSystem` |
| **RolePermission** | Role → permission: `RoleGuid`, `PermissionCode`, `GrantedAt` |
| **UserRole** | User → role: `UserGuid`, `RoleGuid`, `AgendaGuid` (optional), `GrantedAt` |
| **UserSession** | A signed-in device: `UserGuid`, `UserLoginGuid`, `TokenHash`, `PreviousTokenHash`, `TenantGuid`, `DeviceLabel`, `LastUsedAt`, `ExpiresAt`, `RevokedAt`, `RevokedReason` |

### ViewModels (Birko.Models.Users.ViewModels)

Matching ViewModels for all 8 models, extending `LogViewModel` with INotifyPropertyChanged.

### Filters (Birko.Models.Users.Filters)

Filter classes for all 8 models with nullable AND-combined properties. Notable:
- `UserProfile` filter uses Contains for FirstName/LastName
- `RolePermission` filter supports `PermissionCodePrefix` (StartsWith match)
- `UserRole` filter supports `GlobalOnly` flag (AgendaGuid == null)

### Interfaces

| Interface | Property | Implemented By |
|-----------|----------|---------------|
| **IRelatedToUser** | `Guid UserGuid` | UserAgenda, UserLogin, UserProfile, UserRole, UserSession |
| **IRelatedToAgenda** | `Guid AgendaGuid` | UserAgenda |
| **IRelatedToRole** | `Guid RoleGuid` | RolePermission, UserRole |
| **IRelatedToUserLogin** | `Guid UserLoginGuid` | (defined for future use) |

### Sessions (Birko.Models.Users.Sessions, TASK-805)

`UserSession` is one signed-in device: a row per refresh token. A sign-in on a phone no longer signs the PC out, and each
device keeps its own tenant. `UserSessionService` issues, refreshes and revokes them over any `IAsyncBulkStore<T>`:

```csharp
var sessions = new UserSessionService(sessionStore, userTenantStore, userLoginStore, new UserSessionOptions
{
    Lifetime = TimeSpan.FromDays(7),
    UserAllowed = (user, ct) => IsActiveAsync(user, ct),               // false signs the session out
    TenantAllowed = (user, tenant, ct) => TenantIsActiveAsync(tenant, ct), // after the membership check
    LegacyTenant = RefreshTokenTenant.TryRead,                          // a tenant your old tokens carried, if any
});

var issued = await sessions.CreateAsync(user, tenant, loginGuid, userAgent);   // issued.Token once; sid = issued.Session.Guid
var refreshed = await sessions.RefreshAsync(token);                             // .Status, .Token, .Session.TenantGuid, .TenantDropped
await sessions.SwitchTenantAsync(sid, user, newTenant);                         // this device only
await sessions.RevokeAsync(sid, user);                                          // logout
await sessions.RevokeAllAsync(user, SessionRevokeReasons.PasswordChanged);      // everywhere
var devices = await sessions.ListActiveAsync(user);                             // view models, no hashes
await sessions.PruneAsync();                                                    // from a recurring job
```

- The row stores SHA-256 of the token, never the token; the view model has no hash at all.
- Every refresh rotates the token on the same row as a claim (a filtered update, then a re-read). Of two refreshes
  with one token, one wins and the other gets `Raced`, with nothing revoked.
- The token a row was last rotated from, presented again, is a reuse: the session is revoked (`Reused`).
- A refresh keeps the tenant while the user is still in `UserTenants` and `TenantAllowed` agrees; otherwise
  `TenantDropped` is set and you pick one as at login.
- A token from before sessions, found on `UserLogin.RefreshToken`, is redeemed once onto a new session and the column
  is cleared. Nobody is signed out by the upgrade. `RevokeAllAsync` clears that column too.
- The service opens no transaction; run it inside your unit of work if you want one.

## Usage

### User with Authentication

```csharp
using Birko.Models.Users;

// Create user
var user = new User { UserName = "john", Email = "john@example.com" };

// Local auth
var login = new UserLogin
{
    UserGuid = user.Guid!.Value,
    Provider = "local",
    ProviderKey = user.Email,
    PasswordHash = hasher.Hash("password"),
    IsVerified = true
};

// OAuth provider
var googleLogin = new UserLogin
{
    UserGuid = user.Guid!.Value,
    Provider = "google",
    ProviderKey = "google-user-id-123",
    DisplayName = "John via Google",
    IsVerified = true
};
```

### RBAC

```csharp
// Define roles and permissions
var adminRole = new Role { Name = "admin", IsSystem = true };
var permission = new RolePermission
{
    RoleGuid = adminRole.Guid!.Value,
    PermissionCode = "users:profile:edit"
};

// Assign role to user (global)
var globalRole = new UserRole
{
    UserGuid = user.Guid!.Value,
    RoleGuid = adminRole.Guid!.Value,
    AgendaGuid = null  // global
};

// Assign role scoped to agenda
var scopedRole = new UserRole
{
    UserGuid = user.Guid!.Value,
    RoleGuid = editorRole.Guid!.Value,
    AgendaGuid = inventoryAgenda.Guid  // only in Inventory
};
```

### Filtering

```csharp
using Birko.Models.Users.Filters;

// Find active, verified users
var filter = new Filters.User { IsActive = true, EmailVerified = true };

// Find all permissions for a module
var permFilter = new Filters.RolePermission { PermissionCodePrefix = "iot:" };

// Find global role assignments only
var roleFilter = new Filters.UserRole { UserGuid = userId, GlobalOnly = true };
```

## Related Projects

- [Birko.Models](../Birko.Models/) - Base model abstractions
- [Birko.Security](../Birko.Security/) - Password hashing, encryption
- [Birko.Security.Jwt](../Birko.Security.Jwt/) - JWT token provider
- [Birko.Security.AspNetCore](../Birko.Security.AspNetCore/) - ASP.NET Core auth integration

## License

Part of the Birko Framework.
