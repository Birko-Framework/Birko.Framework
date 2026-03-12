# Birko.Communication.Network

## Overview
Network communication implementation for TCP/UDP protocols.

## Project Location
`C:\Source\Birko.Communication.Network\`

## Purpose
- TCP client/server communication
- UDP client/server communication
- Socket-based communication
- Network utility functions

## Components

### TCP
- `TcpCommunicator` - TCP client
- `TcpServer` - TCP server
- `TcpClientCommunicator` - Async TCP client

### UDP
- `UdpCommunicator` - UDP client
- `UdpServer` - UDP server
- `UdpClientCommunicator` - Async UDP client

### Models
- `NetworkSettings` - Connection settings
- `NetworkEndpoint` - Endpoint information

## TCP Client

```csharp
using Birko.Communication.Network;

var settings = new NetworkSettings
{
    Host = "localhost",
    Port = 8080
};

var client = new TcpCommunicator(settings);
client.Connect();

client.Send(Encoding.UTF8.GetBytes("Hello"));
var response = client.Receive();

client.Disconnect();
```

## TCP Server

```csharp
var server = new TcpServer(8080);
server.ClientConnected += (sender, client) =>
{
    // Handle new client
};

server.Start();

// Accept clients
var client = await server.AcceptClientAsync();
```

## UDP Client

```csharp
var settings = new NetworkSettings
{
    Host = "localhost",
    Port = 8080
};

var client = new UdpCommunicator(settings);
client.Send(Encoding.UTF8.GetBytes("Hello"));
```

## UDP Server

```csharp
var server = new UdpServer(8080);
server.DataReceived += (sender, data) =>
{
    // Handle received data
};

server.Start();
```

## Dependencies
- Birko.Communication
- System.Net.Sockets

## Use Cases
- Custom network protocols
- Real-time communication
- Game networking
- IoT device communication
- Peer-to-peer communication

## Best Practices

1. **Connection pooling** - Reuse connections when possible
2. **Buffer size** - Use appropriate buffer sizes
3. **Timeouts** - Set reasonable timeouts
4. **Error handling** - Handle network errors gracefully
5. **Protocol design** - Design clear protocols with length prefixes

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
