# Birko.Models.Users

User, authentication, profile, role-based access control, and agenda management models for the Birko Framework.

## Project Location
`Birko.Models.Users/`

## Components

### Models (`Birko.Models.Users`)

| Model | Table | Description |
|-------|-------|-------------|
| **User** | Users | Core user entity with `UserName` (unique), `Email` (unique, optional), `IsActive`, `LastLoginAt`, `EmailVerified` |
| **Agenda** | Agendas | Application module/section with `Name`, `Description`, `Default` (IDefault), `IsActive` |
| **UserAgenda** | UserAgendas | Join: User ↔ Agenda with `IsOwner`, `JoinedAt`. Implements IRelatedToUser, IRelatedToAgenda |
| **UserLogin** | UserLogins | Authentication provider entry: `Provider` (local/google/apple/etc.), `ProviderKey`, `PasswordHash`, `RefreshToken`, `RefreshTokenExpiry`, `DisplayName`, `IsVerified`, `LastUsedAt`. Implements IRelatedToUser |
| **UserProfile** | UserProfiles | 1:1 with User (GDPR separation): `FirstName`, `LastName`, `DisplayName`, `Phone`, `AvatarUrl`, `Locale`, `TimeZone`, `DateOfBirth`, `Bio`. Has `GetDisplayName()` method. Implements IRelatedToUser |
| **Role** | Roles | Named role definition: `Name` (unique), `Description`, `IsSystem` (cannot be deleted/renamed) |
| **RolePermission** | RolePermissions | Links Role to permission code string: `RoleGuid`, `PermissionCode` (format: `{module}:{entity}:{action}`), `GrantedAt`. Implements IRelatedToRole |
| **UserRole** | UserRoles | Assigns Role to User: `UserGuid`, `RoleGuid`, `AgendaGuid` (optional — null = global role), `GrantedAt`. Implements IRelatedToUser, IRelatedToRole |

All models extend `AbstractDatabaseLogModel` and implement `ILoadable<ViewModels.*>`.

### ViewModels (`Birko.Models.Users.ViewModels`)

All extend `LogViewModel` with INotifyPropertyChanged property tracking:

| ViewModel | Key Properties |
|-----------|---------------|
| **User** | UserName, Email, IsActive, LastLoginAt, EmailVerified |
| **Agenda** | Name, Description, Default, IsActive |
| **UserAgenda** | IsOwner, JoinedAt |
| **UserLogin** | Provider, ProviderKey, PasswordHash, RefreshToken, RefreshTokenExpiry, DisplayName, IsVerified, LastUsedAt |
| **UserProfile** | FirstName, LastName, DisplayName, Phone, AvatarUrl, Locale, TimeZone, DateOfBirth, Bio |
| **Role** | Name, Description, IsSystem |
| **RolePermission** | PermissionCode, GrantedAt |
| **UserRole** | AgendaGuid, GrantedAt |

### Filters (`Birko.Models.Users.Filters`)

All implement `IFilter<T>` with AND-combined nullable filter properties:

| Filter | Properties |
|--------|-----------|
| **User** | UserName, Email, IsActive, EmailVerified |
| **Agenda** | Name, Default, IsActive |
| **UserAgenda** | UserGuid, AgendaGuid, IsOwner |
| **UserLogin** | UserGuid, Provider, ProviderKey, IsVerified |
| **UserProfile** | UserGuid, FirstName (contains), LastName (contains), Locale, TimeZone |
| **Role** | Name, IsSystem |
| **RolePermission** | RoleGuid, PermissionCode, PermissionCodePrefix (StartsWith) |
| **UserRole** | UserGuid, RoleGuid, AgendaGuid, GlobalOnly (filters AgendaGuid == null) |

### Interfaces (`Birko.Models.Users`)

| Interface | Extends | Property | Implemented By |
|-----------|---------|----------|---------------|
| **IRelatedToUser** | ILoadable\<ViewModels.User\> | `Guid UserGuid` | UserAgenda, UserLogin, UserProfile, UserRole |
| **IRelatedToAgenda** | ILoadable\<ViewModels.Agenda\> | `Guid AgendaGuid` | UserAgenda |
| **IRelatedToRole** | ILoadable\<ViewModels.Role\> | `Guid RoleGuid` | RolePermission, UserRole |
| **IRelatedToUserLogin** | ILoadable\<ViewModels.UserLogin\> | `Guid UserLoginGuid` | (defined, not yet used) |

## File Structure
```
Models/
├── User.cs, Agenda.cs, UserAgenda.cs
├── UserLogin.cs, UserProfile.cs
├── Role.cs, RolePermission.cs, UserRole.cs
ViewModels/
├── User.cs, Agenda.cs, UserAgenda.cs
├── UserLogin.cs, UserProfile.cs
├── Role.cs, RolePermission.cs, UserRole.cs
Filters/
├── User.cs, Agenda.cs, UserAgenda.cs
├── UserLogin.cs, UserProfile.cs
├── Role.cs, RolePermission.cs, UserRole.cs
```

## Key Design Decisions

### Agenda vs Tenant
| Aspect | **Agenda** | **ITenant** (Birko.Data.Tenant) |
|--------|-----------|----------------------------------|
| Purpose | Application module/section | Multi-tenancy isolation |
| Example | "Inventory", "Sales", "HR" | "CustomerA", "CustomerB" |
| Scope | Functional segregation within an app | Separate data for different customers |

### Authentication (UserLogin)
- Multiple auth providers per user (local, Google, Apple, Microsoft, Facebook, GitHub)
- `PasswordHash` only for local provider (Birko.Security PBKDF2)
- `RefreshToken` / `RefreshTokenExpiry` for JWT refresh flow

### RBAC (Role, RolePermission, UserRole)
- Permission codes follow `{module}:{entity}:{action}` convention
- Roles can be system-level (`IsSystem = true`) — cannot be deleted/renamed
- UserRole can be agenda-scoped (`AgendaGuid`) or global (`AgendaGuid = null`)

### Profile Separation (UserProfile)
- 1:1 with User via `UserGuid` (UniqueField)
- Separate table for GDPR compliance and lazy-loading
- `GetDisplayName()` fallback: DisplayName → "FirstName LastName" → empty

## Dependencies
- **Birko.Data.Core** — AbstractDatabaseLogModel, ILoadable, IDefault, IFilter
- **Birko.Data.SQL** — Table, UniqueField, PrecisionField, NamedField attributes

## Maintenance

### README Updates
When making changes that affect the public API, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests.
