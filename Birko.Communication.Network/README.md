# Birko.Communication.Network

Network communication implementation for TCP/UDP protocols in the Birko Framework.

## Features

- TCP client/server communication
- UDP client/server communication
- Socket-based communication with event-driven architecture
- Async support for all operations

## Installation

```bash
dotnet add package Birko.Communication.Network
```

## Dependencies

- Birko.Communication
- System.Net.Sockets

## Usage

### TCP Client

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

### TCP Server

```csharp
var server = new TcpServer(8080);
server.ClientConnected += (sender, client) =>
{
    // Handle new client
};
server.Start();
```

### UDP Client/Server

```csharp
var client = new UdpCommunicator(new NetworkSettings { Host = "localhost", Port = 8080 });
client.Send(Encoding.UTF8.GetBytes("Hello"));

var server = new UdpServer(8080);
server.DataReceived += (sender, data) => { /* Handle data */ };
server.Start();
```

## API Reference

### Classes

- **TcpCommunicator** - TCP client
- **TcpServer** - TCP server with client accept
- **TcpClientCommunicator** - Async TCP client
- **UdpCommunicator** - UDP client
- **UdpServer** - UDP server
- **UdpClientCommunicator** - Async UDP client
- **NetworkSettings** - Connection settings (Host, Port)
- **NetworkEndpoint** - Endpoint information

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base interfaces
- [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/) - WebSocket protocol

## License

Part of the Birko Framework.
