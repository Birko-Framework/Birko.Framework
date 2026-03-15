# Birko.Models.Users

User and agenda management models for the Birko Framework.

## Models

| Model | Description |
|-------|-------------|
| **User** | User with unique UserName and comma-separated Roles |
| **Agenda** | Application module/section with Name and Default flag |
| **UserAgenda** | Join table linking Users to Agendas with role permissions |

## Agenda vs Tenant

The `Agenda` model and `ITenant` serve different purposes:

| Aspect | **Agenda** | **ITenant** (Birko.Data.Tenant) |
|--------|-----------|----------------------------------|
| Purpose | Application module/section | Multi-tenancy isolation |
| Example | "Inventory", "Sales", "HR" modules | "CustomerA", "CustomerB" organizations |
| Usage | Functional segregation within an app | Separate data for different customers |

```csharp
// Agenda - for organizing app features
var agenda = new Agenda { Name = "Inventory", Default = true };

// ITenant - for multi-tenancy (separate entity)
public class MyEntity : ITenant
{
    public Guid TenantId { get; set; }  // Which tenant owns this
    public string? TenantName { get; set; }
}
```

## Interfaces

| Interface | Purpose |
|-----------|---------|
| `IRelatedToUser` | Entities that reference a User |
| `IRelatedToAgenda` | Entities that reference an Agenda |

## Constants

| Constant | Value | Purpose |
|----------|-------|---------|
| `User.UserRolesSeparator` | "," | Separator for role strings |

## Dependencies

- Birko.Data.Core (for base classes and attributes)
- Birko.Models (for base model classes)

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
