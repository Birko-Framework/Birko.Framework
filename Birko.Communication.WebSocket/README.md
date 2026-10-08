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

var server = new WebSocketServer { MaxMessageBytes = 1024 * 1024 }; // default 4 MiB
server.OnDataReceived += (sender, data) =>
{
    // One whole message from any client, reassembled across frames
};
server.OnClientConnected += (sender, clientId) =>
{
    Console.WriteLine($"Client connected: {clientId}");
};

await server.StartAsync("http://localhost:8080/");
```

A client whose message exceeds `MaxMessageBytes` is closed with `MessageTooBig` instead of being buffered without limit.

### Receiving whole messages in a handler

A message can span several frames; one `ReceiveAsync` returns a fragment. `ReceiveMessageAsync` reassembles it, caps it,
and reports a close or an over-cap message as an outcome instead of an exception:

```csharp
using Birko.Communication.WebSocket.Messaging;

app.MapWebSocketEndpoint("/ws", async (socket, context) =>
{
    while (true)
    {
        var message = await socket.ReceiveMessageAsync(maxMessageBytes: 256 * 1024, context.RequestAborted);
        if (!message.IsMessage) break;            // Closed, or TooBig (socket already closed with MessageTooBig)
        await HandleAsync(message.GetText());
    }
});
```

Sending from several tasks at once on one socket is safe on .NET 10: the runtime's `ManagedWebSocket` serializes
concurrent `SendAsync` calls (measured in TASK-537 and pinned by a test), so no send queue is needed.

### ASP.NET Core Middleware

```csharp
using Birko.Communication.WebSocket.Middleware;

app.UseWebSockets();
app.UseMiddleware<WebSocketMiddleware>();
```

### Mapping endpoints and authentication

`MapWebSocketEndpoint(pattern, handler, requireAuthentication: true)` puts the endpoint behind the **static-token**
check of `WebSocketAuthenticationService` (tokens / token bindings from `WebSocketAuthenticationConfiguration`, sent as
`?token=`). It is meant for machine-to-machine and edge clients, **not** per-user sign-in.

It fails closed. With `requireAuthentication: true` and no `WebSocketAuthenticationService` registered, mapping throws
`InvalidOperationException`, and a request that reaches the gate anyway is refused with 401. Before TASK-536 the check
was skipped and anonymous upgrades were accepted.

```csharp
// Static tokens
builder.Services.Configure<WebSocketAuthenticationConfiguration>(builder.Configuration.GetSection("WebSocketAuth"));
builder.Services.AddSingleton<WebSocketAuthenticationService>();
app.MapWebSocketEndpoint("/ws/devices", DeviceHandler.HandleAsync);

// Per-user auth (JWT / cookie): turn the token check off and use ASP.NET Core authorization
app.MapWebSocketEndpoint("/ws/realtime", RealtimeHandler.HandleAsync, requireAuthentication: false)
   .RequireAuthorization();

// Public endpoint
app.MapWebSocketEndpointNoAuth("/ws/display", DisplayHandler.HandleAsync);
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
