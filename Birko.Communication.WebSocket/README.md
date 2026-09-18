# Birko.Communication.WebSocket

WebSocket communication library providing a client port, standalone server, and ASP.NET Core middleware for the Birko Framework.

## Features

- WebSocket client port with threaded read loop and cancellation support
- Standalone WebSocket server using `HttpListener` with client tracking
- ASP.NET Core middleware for WebSocket endpoint integration
- Authentication services and configuration
- Event-driven message reception (binary data)
- Connection management with connect/disconnect events

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.WebSocket\Birko.Communication.WebSocket.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** - Base communication interfaces (`AbstractPort`, `PortSettings`)
- **System.Net.WebSockets** - .NET WebSocket APIs
- **Microsoft.AspNetCore.Http** - ASP.NET Core middleware support
- **Microsoft.Extensions.Logging** - Logging for the server

## Usage

### WebSocket Client

```csharp
using Birko.Communication.WebSocket.Ports;

var settings = new WebSocketSettings
{
    Name = "MyWebSocket",
    Uri = "ws://localhost:8080/ws"
};

var ws = new WebSocketPort(settings);
ws.OnDataReceived += (sender, data) =>
{
    Console.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
};

ws.Open();
ws.Write(Encoding.UTF8.GetBytes("Hello Server"));
ws.Close();
```

### Standalone WebSocket Server

```csharp
using Birko.Communication.WebSocket.Servers;

var server = new WebSocketServer("http://localhost:8080/");
server.OnDataReceived += (sender, data) =>
{
    // Handle received data from any client
};
server.OnClientConnected += (sender, clientId) =>
{
    Console.WriteLine($"Client connected: {clientId}");
};

await server.StartAsync();
```

### ASP.NET Core Middleware

```csharp
using Birko.Communication.WebSocket.Middleware;

app.UseWebSockets();
app.UseMiddleware<WebSocketMiddleware>();
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `WebSocketPort` | WebSocket client extending `AbstractPort` |
| `WebSocketSettings` | Client settings (Uri) extending `PortSettings` |
| `WebSocketServer` | Standalone server using `HttpListener`, implements `IDisposable` |
| `WebSocketMiddleware` | ASP.NET Core middleware for WebSocket connections |
| `WebSocketEndpointExtensions` | Extension methods for endpoint routing |
| `WebSocketAuthenticationService` | Authentication service for WebSocket connections |
| `WebSocketAuthenticationConfiguration` | Authentication configuration |

### Namespaces

- `Birko.Communication.WebSocket.Ports` - Client port and settings
- `Birko.Communication.WebSocket.Servers` - Standalone server
- `Birko.Communication.WebSocket.Middleware` - ASP.NET Core middleware
- `Birko.Communication.WebSocket.Services` - Authentication services

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.SSE](../Birko.Communication.SSE/) - Server-Sent Events (one-way push)
- [Birko.Communication.REST](../Birko.Communication.REST/) - REST API client/server

## License

Part of the Birko Framework.
