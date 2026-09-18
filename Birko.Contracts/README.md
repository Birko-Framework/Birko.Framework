# Birko.Contracts

Pure interface contracts for the Birko Framework with zero dependencies.

## Overview

Birko.Contracts contains the most fundamental interfaces used across the Birko Framework. These interfaces have no dependencies on any other project, making them the ideal foundation layer for lightweight consumers.

## Namespaces

- `Birko.Data.Models` — interfaces (preserves backward compatibility with Birko.Data.Core)
- `Birko` — shared utility types

## Interfaces

| Interface | Namespace | Description |
|-----------|-----------|-------------|
| `ILoadable<T>` | `Birko.Data.Models` | Load-from pattern: `void LoadFrom(T data)` |
| `ICopyable<T>` | `Birko.Data.Models` | Copy-to pattern: `T CopyTo(T clone)` |
| `IDefault` | `Birko.Data.Models` | Single `bool Default` property |
| `ITimestamped` | `Birko.Data.Models` | `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt` timestamp tracking |
| `IGuidEntity` | `Birko.Data.Models` | Entity with `Guid?` identifier |
| `ILogEntity` | `Birko.Data.Models` | Extends `IGuidEntity` + `ITimestamped` |

## Classes

| Class | Namespace | Description |
|-------|-----------|-------------|
| `RetryPolicy` | `Birko` | Configurable retry with exponential backoff (`MaxRetries`, `BaseDelay`, `MaxDelay`) |

## Dependencies

None. This is a zero-dependency project.

## Usage

```xml
<Import Project="..\Birko.Contracts\Birko.Contracts.projitems" Label="Shared" />
```

```csharp
using Birko.Data.Models;

public class MyEntity : IGuidEntity, ITimestamped
{
    public Guid? Guid { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? PrevUpdatedAt { get; set; }
}
```

## License

MIT License - see [License.md](License.md)
