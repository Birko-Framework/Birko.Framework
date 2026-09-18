# Birko.Time.Abstractions

Lightweight clock abstraction for the Birko Framework with zero dependencies.

## Overview

Birko.Time.Abstractions extracts the core clock interface and providers from Birko.Time. This allows projects that only need `IDateTimeProvider` (e.g., MessageQueue, BackgroundJobs, EventBus) to avoid pulling in the full Birko.Time project with calendars, holidays, and working hours.

## Namespace

All types are in namespace `Birko.Time` (preserves backward compatibility with Birko.Time).

## Types

| Type | Description |
|------|-------------|
| `IDateTimeProvider` | Clock abstraction: `UtcNow`, `OffsetUtcNow`, `Today` |
| `SystemDateTimeProvider` | Production implementation using `DateTime.UtcNow` |
| `TestDateTimeProvider` | Test-controllable clock with `SetTime()` and `Advance()` |

## Dependencies

None. This is a zero-dependency project.

## Usage

```xml
<Import Project="..\Birko.Time.Abstractions\Birko.Time.Abstractions.projitems" Label="Shared" />
```

```csharp
using Birko.Time;

IDateTimeProvider clock = new SystemDateTimeProvider();
var now = clock.UtcNow;
```

`Birko.Time` transitively includes `Birko.Time.Abstractions`, so existing consumers are unaffected.

## License

MIT License - see [License.md](License.md)
