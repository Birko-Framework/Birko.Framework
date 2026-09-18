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
| **IRelatedToUser** | `Guid UserGuid` | UserAgenda, UserLogin, UserProfile, UserRole |
| **IRelatedToAgenda** | `Guid AgendaGuid` | UserAgenda |
| **IRelatedToRole** | `Guid RoleGuid` | RolePermission, UserRole |
| **IRelatedToUserLogin** | `Guid UserLoginGuid` | (defined for future use) |

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
