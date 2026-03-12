# Birko.Models.Users

User and agenda management models for the Birko Framework.

## Features

- User model with unique UserName and comma-separated Roles
- Agenda model (application modules/sections)
- UserAgenda join table with role permissions
- Distinguished from ITenant (multi-tenancy isolation)

## Installation

```bash
dotnet add package Birko.Models.Users
```

## Dependencies

- Birko.Models

## API Reference

### Models (namespace: Birko.Models.Users)

- **User** - User entity (UserName, Roles, `UserRolesSeparator = ","`)
- **Agenda** - Application module/section
- **UserAgenda** - User-Agenda join with role permissions

### Interfaces

- **IRelatedToUser** / **IRelatedToAgenda**

## License

Part of the Birko Framework.
