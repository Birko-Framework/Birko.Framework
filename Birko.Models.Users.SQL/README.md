# Birko.Models.Users.SQL

Canonical fluent SQL mappings for the `Birko.Models.Users` domain. Pairs with [`Birko.Models.SQL`](../Birko.Models.SQL/).

## Mappings

| Class | Table | Notable fields |
|---|---|---|
| `UserMapping` | `Users` | UserName, Email (both unique) |
| `UserLoginMapping` | `UserLogins` | Provider, ProviderKey, PasswordHash, RefreshToken, DisplayName |
| `UserProfileMapping` | `UserProfiles` | UserGuid unique; FirstName, LastName, DisplayName, Phone, AvatarUrl, Locale, TimeZone, Bio |
| `UserRoleMapping` | `UserRoles` | — |
| `UserTenantMapping` | `UserTenants` | — |
| `RoleMapping` | `Roles` | Name unique, Description |
| `RolePermissionMapping` | `RolePermissions` | PermissionCode |
| `TenantMapping` | `Tenants` | Name, Description |

## Installation

```xml
<Import Project="..\Birko.Models.Users\Birko.Models.Users.projitems"          Label="Shared" />
<Import Project="..\Birko.Models.SQL\Birko.Models.SQL.projitems"              Label="Shared" />
<Import Project="..\Birko.Models.Users.SQL\Birko.Models.Users.SQL.projitems"  Label="Shared" />
```

## Usage

```csharp
var registry = new Birko.Models.SQL.Mapping.ModelMapRegistry();
registry.RegisterFromAssembly(typeof(Program).Assembly);  // picks up every IModelMapping<T> in your DLL
registry.ApplyToDatabase();
```

To override a canonical mapping (e.g. rename the `Users` table), register your own `IModelMapping<User>` AFTER `RegisterFromAssembly`.

## Dependencies

- [`Birko.Models.SQL`](../Birko.Models.SQL/) — mapping framework
- [`Birko.Models.Users`](../Birko.Models.Users/) — domain models

## License

Part of the Birko Framework.
