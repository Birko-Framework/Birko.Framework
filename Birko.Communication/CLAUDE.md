# Birko.Communication

## Overview
Base communication interfaces and abstract classes for the Birko Framework communication layer.

## Project Location
`C:\Source\Birko.Communication\`

## Purpose
- Define communication interfaces
- Provide abstract base classes
- Establish patterns for communication protocols
- Support for various communication methods

## Interfaces

### ICommunicator
Base interface for all communicators:
- `Connect()` - Establish connection
- `Disconnect()` - Close connection
- `Send(byte[] data)` - Send data
- `Receive()` - Receive data
- `IsConnected` - Connection status

### IAsyncCommunicator
Asynchronous communication:
- `ConnectAsync()`
- `DisconnectAsync()`
- `SendAsync(byte[] data)`
- `ReceiveAsync()`

### IStreamCommunicator
Stream-based communication:
- `GetStream()` - Get communication stream
- `ReadStream()` - Read from stream
- `WriteStream()` - Write to stream

## Abstract Classes

### AbstractCommunicator
Base class for synchronous communicators:
- Connection management
- Event handling
- Error handling

### AbstractAsyncCommunicator
Base class for asynchronous communicators:
- Async connection management
- Async event handling
- Cancellation token support

## Events

### Common Events
- `Connected` - Raised when connected
- `Disconnected` - Raised when disconnected
- `DataReceived` - Raised when data is received
- `ErrorOccurred` - Raised when an error occurs

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

    protected virtual void OnConnected()
    {
        Connected?.Invoke(this, EventArgs.Empty);
    }
}
```

## Best Practices

1. **Resource cleanup** - Always implement IDisposable
2. **Thread safety** - Use locks for shared resources
3. **Error handling** - Always handle communication errors
4. **Timeouts** - Implement connection and operation timeouts
5. **Reconnection** - Implement automatic reconnection logic

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
