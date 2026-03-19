# Birko.Contracts

Pure interface contracts for the Birko Framework with zero dependencies.

## Overview

Birko.Contracts contains the most fundamental interfaces used across the Birko Framework. These interfaces have no dependencies on any other project, making them the ideal foundation layer for lightweight consumers.

## Namespace

All types are in namespace `Birko.Data.Models` (preserves backward compatibility with Birko.Data.Core).

## Interfaces

| Interface | Description |
|-----------|-------------|
| `ILoadable<T>` | Load-from pattern: `void LoadFrom(T data)` |
| `ICopyable<T>` | Copy-to pattern: `T CopyTo(T clone)` |
| `IDefault` | Single `bool Default` property |
| `ITimestamped` | `CreatedAt`, `UpdatedAt`, `PrevUpdatedAt` timestamp tracking |

## Dependencies

None. This is a zero-dependency project.

## Usage

```xml
<Import Project="..\Birko.Contracts\Birko.Contracts.projitems" Label="Shared" />
```

```csharp
using Birko.Data.Models;

public class MySettings : ILoadable<MySettings>
{
    public string Name { get; set; }
    public void LoadFrom(MySettings data) => Name = data.Name;
}
```

## License

MIT License - see [License.md](License.md)
