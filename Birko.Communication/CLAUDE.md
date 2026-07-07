# Birko.Communication

## Overview
Base **port** abstraction for the Birko Framework communication layer: the byte-buffer `IPort`
interface and the `AbstractPort` base class. Transport-specific projects (Network, Hardware,
Bluetooth, …) implement `IPort` on top of their wire protocol.

## Purpose
- Define the byte-oriented port interface (`IPort`)
- Provide an `AbstractPort` base that manages the read buffer + process-data notifications
- Establish a common shape for transport implementations

## Public Surface (namespace `Birko.Communication.Ports`)

### `IPort`
- **Transport:** `Open()`, `Close()`, `IsOpen()`, `Write(byte[] data)`, `Read(int size)` (`size < 0` = read all buffered)
- **Buffered reads:** `HasReadData(int size)`, `RemoveReadData(int size)`, `GetData()`, `Clear()`, `IsEmpty()`
- **Notifications:** `SubscribeProcessData(ProcessDataDelegate)`, `UnSubscribeProcessData(ProcessDataDelegate)`

### `AbstractPort : IPort`
Holds the `ReadData` (`List<byte>`) buffer and a `PortSettings`. Implements `IsOpen`, `Clear`,
`IsEmpty`, `GetData`, the subscribe/unsubscribe pair, and `InvokeProcessData`. Concrete ports
override the abstract `Write`, `Read`, `Open`, `Close`, `HasReadData`, and `RemoveReadData`.

### `PortSettings`
`Name` property + `GetID()`. Subclass for transport-specific configuration.

### `ProcessDataDelegate`
Parameterless delegate invoked via `InvokeProcessData()` after the port processes data.

## Dependencies
- .NET 10.0

## Specialized Communication

Different communication protocols have their own implementations:
- [Birko.Communication.Network](../Birko.Communication.Network/CLAUDE.md) - TCP/UDP network communication
- [Birko.Communication.Hardware](../Birko.Communication.Hardware/CLAUDE.md) - Hardware device communication
- [Birko.Communication.Bluetooth](../Birko.Communication.Bluetooth/CLAUDE.md) - Bluetooth protocol
- [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/CLAUDE.md) - WebSocket protocol
- [Birko.Communication.REST](../Birko.Communication.REST/CLAUDE.md) - REST API client
- [Birko.Communication.SOAP](../Birko.Communication.SOAP/CLAUDE.md) - SOAP client
- [Birko.Communication.SSE](../Birko.Communication.SSE/CLAUDE.md) - Server-Sent Events
- ~~Birko.Communication.Authentication~~ - Moved to [Birko.Security](../Birko.Security/CLAUDE.md)

## Implementation Example

```csharp
using Birko.Communication.Ports;

public class MyPort : AbstractPort
{
    public override void Open() { /* open the transport */ }
    public override void Close() { /* close the transport */ }
    public override bool IsOpen() => /* transport state */;

    public override void Write(byte[] data) { /* send bytes */ }
    public override byte[] Read(int size) { /* read up to size (size < 0 = all) */ return Array.Empty<byte>(); }

    public override bool HasReadData(int size) => ReadData.Count >= size;

    public override byte[] RemoveReadData(int size)
    {
        var data = Read(size);
        ReadData.RemoveRange(0, data.Length);
        return data;
    }
}
```

## Best Practices

1. **Resource cleanup** - Ports holding OS/device handles should implement `IDisposable` (dispose → `Close`)
2. **Thread safety** - Guard the shared `ReadData` buffer when reading/writing from multiple threads
3. **Error handling** - Surface transport errors from `Open`/`Read`/`Write`
4. **Negative size = drain all** - Honour `size < 0` consistently across `Read`/`HasReadData`/`RemoveReadData`

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
