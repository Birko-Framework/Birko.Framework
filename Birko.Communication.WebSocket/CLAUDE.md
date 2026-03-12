# Birko.Communication.WebSocket

## Overview
WebSocket communication implementation for real-time bidirectional communication.

## Project Location
`C:\Source\Birko.Communication.WebSocket\`

## Purpose
- WebSocket client communication
- WebSocket server implementation
- Real-time message passing
- Full-duplex communication

## Components

### Client
- `WebSocketCommunicator` - WebSocket client
- `AsyncWebSocketCommunicator` - Async WebSocket client

### Server
- `WebSocketServer` - WebSocket server
- `WebSocketConnection` - Server-side connection

### Middleware
- ASP.NET Core middleware for WebSocket

### Services
- Connection management
- Message routing

## WebSocket Client

```csharp
using Birko.Communication.WebSocket;

var client = new WebSocketCommunicator("ws://localhost:8080/ws");

client.Connected += (sender, args) => Console.WriteLine("Connected");
client.MessageReceived += (sender, message) => Console.WriteLine($"Received: {message}");

await client.ConnectAsync();

await client.SendAsync("Hello Server");
```

## WebSocket Server

```csharp
var server = new WebSocketServer("http://localhost:8080");

server.OnConnection = (connection) =>
{
    connection.MessageReceived += (sender, message) =>
    {
        // Echo back
        connection.SendAsync($"Echo: {message}");
    };
};

await server.StartAsync();
```

## ASP.NET Core Middleware

```csharp
// In Program.cs or Startup.cs
app.UseWebSockets();
app.UseMiddleware<Birko.Communication.WebSocket.Middleware.WebSocketMiddleware>();
```

## Message Types

### Text Messages
```csharp
await client.SendAsync("Text message");
```

### Binary Messages
```csharp
await client.SendAsync(new byte[] { 0x01, 0x02, 0x03 });
```

### JSON Messages
```csharp
var data = new { Name = "John", Age = 30 };
await client.SendJsonAsync(data);
```

## Dependencies
- Birko.Communication
- System.Net.WebSockets
- Microsoft.AspNetCore.WebSockets (for server)

## Features

### Automatic Reconnection
```csharp
client.AutoReconnect = true;
client.ReconnectDelay = TimeSpan.FromSeconds(5);
```

### Heartbeat/Ping-Pong
```csharp
client.KeepAliveInterval = TimeSpan.FromSeconds(30);
```

### Message Queuing
```csharp
client.QueueMessagesWhileDisconnected = true;
```

## Use Cases
- Real-time notifications
- Chat applications
- Live dashboards
- Collaborative editing
- Gaming
- Financial trading
- IoT monitoring

## Best Practices

1. **Connection state** - Always check connection state before sending
2. **Error handling** - Handle WebSocket errors and disconnections
3. **Message size** - Consider message size limits
4. **Reconnection** - Implement exponential backoff for reconnection
5. **Security** - Use WSS (WebSocket Secure) in production
6. **Protocol** - Design a clear message protocol

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
