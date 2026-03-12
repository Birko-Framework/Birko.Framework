# Birko.Communication

Base communication interfaces and abstract classes for the Birko Framework communication layer.

## Features

- Synchronous and asynchronous communication interfaces
- Stream-based communication support
- Event-driven architecture (Connected, Disconnected, DataReceived, ErrorOccurred)
- Abstract base classes with connection management and error handling
- Cancellation token support for async operations

## Installation

```bash
dotnet add package Birko.Communication
```

## Dependencies

- .NET 10.0

## Usage

```csharp
using Birko.Communication;

public class MyCommunicator : AbstractCommunicator, ICommunicator
{
    public override void Connect()
    {
        // Implementation
        OnConnected();
    }

    public override void Disconnect()
    {
        // Implementation
        OnDisconnected();
    }

    public override void Send(byte[] data)
    {
        // Send implementation
    }

    public override byte[] Receive()
    {
        // Receive implementation
        return new byte[0];
    }
}
```

## API Reference

### Interfaces

- **ICommunicator** - Base synchronous interface: `Connect()`, `Disconnect()`, `Send(byte[])`, `Receive()`, `IsConnected`
- **IAsyncCommunicator** - Async interface: `ConnectAsync()`, `DisconnectAsync()`, `SendAsync(byte[])`, `ReceiveAsync()`
- **IStreamCommunicator** - Stream-based: `GetStream()`, `ReadStream()`, `WriteStream()`

### Abstract Classes

- **AbstractCommunicator** - Base for sync communicators with connection management and event handling
- **AbstractAsyncCommunicator** - Base for async communicators with cancellation token support

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
