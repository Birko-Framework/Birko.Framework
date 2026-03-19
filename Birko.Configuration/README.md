# Birko.Configuration

Lightweight shared project containing the settings hierarchy for the Birko Framework.

## Overview

Birko.Configuration extracts the `Settings`, `PasswordSettings`, and `RemoteSettings` classes from `Birko.Data.Stores` into a standalone shared project. This allows lightweight consumers (e.g., message queues, communication layers) to depend on connection settings without pulling in the full store abstraction layer.

## Namespace

All types are in namespace `Birko.Configuration` (avoids collision with local `Settings` classes in other projects).

## Classes

| Class | Description |
|-------|-------------|
| `ISettings` | Interface for store settings with `GetId()` |
| `Settings` | Base settings with `Location` and `Name` properties |
| `PasswordSettings` | Extends Settings with `Password` |
| `RemoteSettings` | Extends PasswordSettings with `UserName`, `Port`, `UseSecure` |

## Dependencies

- **Birko.Contracts** — provides `ILoadable<T>` interface (namespace `Birko.Data.Models`)

## Usage

Consumers that only need settings (not full store abstractions) can import this project instead of `Birko.Data.Stores`:

```xml
<Import Project="..\Birko.Configuration\Birko.Configuration.projitems" Label="Shared" />
```

Birko.Configuration transitively imports Birko.Contracts, so a single import is sufficient.

```csharp
using Birko.Configuration;

var settings = new RemoteSettings("localhost", "mydb", "admin", "secret", 5432);
```

`Birko.Data.Stores` transitively includes `Birko.Configuration`, so existing consumers are unaffected.

## License

MIT License - see [License.md](License.md)
