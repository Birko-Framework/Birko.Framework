# Birko.Communication

Base port abstraction for the Birko Framework communication layer — a byte-buffer
`IPort` interface plus an `AbstractPort` base that manages a read buffer and process-data
notifications. Transport-specific projects (Network, Hardware, Bluetooth, …) implement `IPort`.

## Features

- Byte-oriented port interface (`Write` / `Read` / `Open` / `Close`)
- Buffered reads with `HasReadData` / `RemoveReadData` / `GetData` / `Clear`
- Process-data notification via `ProcessDataDelegate` (`SubscribeProcessData` / `UnSubscribeProcessData`)
- `AbstractPort` base implementing the buffer bookkeeping; concrete ports override the four transport members

## Installation

```bash
dotnet add package Birko.Communication
```

## Dependencies

- .NET 10.0

## Usage

```csharp
using Birko.Communication.Ports;

public class MyPort : AbstractPort
{
    public override void Open() { /* open the transport */ }
    public override void Close() { /* close the transport */ }
    public override bool IsOpen() => /* transport state */;

    public override void Write(byte[] data) { /* send bytes */ }

    // Read up to `size` bytes (size < 0 = read all buffered data).
    public override byte[] Read(int size) { /* ... */ return Array.Empty<byte>(); }

    public override bool HasReadData(int size) => ReadData.Count >= size;

    public override byte[] RemoveReadData(int size)
    {
        var data = Read(size);
        ReadData.RemoveRange(0, data.Length);
        return data;
    }
}
```

## API Reference

### `IPort`

- **Transport:** `Open()`, `Close()`, `IsOpen()`, `Write(byte[])`, `Read(int size)`
- **Buffered reads:** `HasReadData(int size)`, `RemoveReadData(int size)`, `GetData()`, `Clear()`, `IsEmpty()`
- **Notifications:** `SubscribeProcessData(ProcessDataDelegate)`, `UnSubscribeProcessData(ProcessDataDelegate)`

### `AbstractPort : IPort`

Base class holding the `ReadData` buffer and `PortSettings`. It implements `IsOpen`, `Clear`,
`IsEmpty`, `GetData`, the subscribe/unsubscribe pair, and `InvokeProcessData`; concrete ports
implement the abstract `Write`, `Read`, `Open`, `Close`, `HasReadData`, and `RemoveReadData`.

### `PortSettings`

`Name` + `GetID()`. Subclass for transport-specific configuration.

### `ProcessDataDelegate`

Parameterless delegate invoked (via `InvokeProcessData`) after the port has processed data.

## Related Projects

- [Birko.Communication.Network](../Birko.Communication.Network/) - TCP/UDP
- [Birko.Communication.Hardware](../Birko.Communication.Hardware/) - Serial/USB
- [Birko.Communication.Bluetooth](../Birko.Communication.Bluetooth/) - Bluetooth/BLE
- [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/) - WebSocket
- [Birko.Communication.REST](../Birko.Communication.REST/) - REST API client
- [Birko.Communication.SOAP](../Birko.Communication.SOAP/) - SOAP client
- [Birko.Communication.SSE](../Birko.Communication.SSE/) - Server-Sent Events

## License

Part of the Birko Framework.
