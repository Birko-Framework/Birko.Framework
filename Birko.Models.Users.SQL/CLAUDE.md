# Birko.Models.Users.SQL

## Overview
Canonical `IModelMapping<T>` implementations for the `Birko.Models.Users` domain. Provides ready-to-use fluent SQL mappings for User, UserLogin, UserProfile, UserRole, UserTenant, Role, RolePermission, and Tenant — paired with `Birko.Models.SQL` (the fluent mapping framework).

## Project Location
`Birko.Models.Users.SQL/`

## Components (`Birko.Models.Users.SQL.Mappings`)
- **UserMapping** → `Users` table (UserName, Email both unique)
- **UserLoginMapping** → `UserLogins` table (Provider, ProviderKey, PasswordHash, RefreshToken, DisplayName)
- **UserProfileMapping** → `UserProfiles` table (FirstName, LastName, DisplayName, Phone, AvatarUrl, Locale, TimeZone, Bio; UserGuid unique)
- **UserRoleMapping** → `UserRoles` table
- **UserTenantMapping** → `UserTenants` table
- **RoleMapping** → `Roles` table (Name unique)
- **RolePermissionMapping** → `RolePermissions` table (PermissionCode)
- **TenantMapping** → `Tenants` table (Name, Description)

## File Structure
```
Mappings/
├── UserMapping.cs
├── UserLoginMapping.cs
├── UserProfileMapping.cs
├── UserRoleMapping.cs
├── UserTenantMapping.cs
├── RoleMapping.cs
├── RolePermissionMapping.cs
└── TenantMapping.cs
```

## Dependencies
- **Birko.Models.SQL** — `ModelMap<T>`, `IModelMapping<T>`, `FieldBuilder<T>` (the mapping framework)
- **Birko.Models.Users** — `User`, `UserLogin`, `UserProfile`, `UserRole`, `UserTenant`, `Role`, `RolePermission`, `Tenant` (domain models)

## Usage

Import alongside `Birko.Models.SQL` and `Birko.Models.Users`:

```xml
<Import Project="..\Birko.Models.Users\Birko.Models.Users.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"              Label="Shared" />
<Import Project="..\Birko.Models.Users.SQL\Birko.Models.Users.SQL.projitems"  Label="Shared" />
```

Then assembly-scan picks the mappings up automatically:

```csharp
var registry = new ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);
registry.ApplyToDatabase();
```

If you want to override any canonical mapping (e.g. rename the `Users` table), register your override with `registry.Register(new MyUserMapping())` AFTER `RegisterFromAssembly`.

## Maintenance

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.
