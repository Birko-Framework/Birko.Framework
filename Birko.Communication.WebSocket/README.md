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

### Reverse proxy

`MapWebSocketProxy(pattern, upstream, configure)` relays each client connection to an upstream WebSocket server (the client's requested sub-protocols are offered upstream, and the client gets the one the upstream selected)
(TASK-542). Frames are forwarded as they arrive, with their type and `EndOfMessage`, so memory stays at one buffer per
direction. A close is forwarded with its status and description; a side that drops without one leaves the other
closed with `EndpointUnavailable`.

- The upstream is connected **before** the client is accepted, so an upstream that refuses or is unreachable answers
  the client **502** without an upgrade. A request that is not an upgrade gets 400.
- TLS certificates are validated. `DangerousAcceptAnyServerCertificate = true` turns that off, for a local
  development certificate only.
- `Credential` is sent as an `Authorization: Bearer …` header by default, or URL-encoded as `?token=` with
  `CredentialTransport = QueryString` (Birko's own static-token gate reads the query).
- The endpoint has no authentication of its own: chain `.RequireAuthorization()`. A constant `Credential` gives every
  admitted client the upstream access of that credential.

```csharp
app.MapWebSocketProxy("/dragon", new Uri("wss://server.example/dragon"), o =>
{
    o.Credential = context => context.Request.Query["token"];
    o.CredentialTransport = WebSocketProxyCredentialTransport.QueryString;
}).RequireAuthorization();
```

## Command router (request / response)

`WebSocketCommandRouter` is the server half of birko-web-core's `WsClient.request` (TASK-539). It answers
`{ type, id, data }` with `{ id, type: "response", data, timestamp }` or `{ id, type: "error", error, timestamp }`:

```csharp
var router = new WebSocketCommandRouter(new WebSocketCommandRouterOptions(), logger)
    .Map("get_stats", (ctx, ct) => stats.GetAsync(ct))
    .Map<ProjectRef>("get_project", (data, ctx, ct) => projects.GetAsync(data!.Id, ct));

app.Map("/ws", async http => { using var socket = await http.WebSockets.AcceptWebSocketAsync(); await router.RunAsync(socket); });
```

- Every reply carries the request id when one was sent, including the error replies (unknown command, unreadable
  `data`, a failing handler), so a correlating client fails at once instead of waiting out its timeout.
- An unexpected exception's message is logged, not sent (`Command 'x' failed.`), unless `ExposeExceptionMessages`; throw
  `WebSocketCommandException` to fail with a message for the client.
- `CommandField` (default `type`, as WsClient sends) and `IdField` (default `id`) are configurable, e.g. `command` for
  a client sending `{ command, id, data }`.
- `RunAsync` reads whole messages with `ReceiveMessageAsync` and handles requests concurrently; it returns once the socket
  closes and every started request has replied.

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
| `WebSocketProxyEndpointExtensions` | `MapWebSocketProxy` — WebSocket reverse-proxy endpoint |
| `WebSocketProxyOptions` | Upstream, credential transport, certificate policy, close timeout |

### Namespaces

- `Birko.Communication.WebSocket.Ports` - Client port and settings
- `Birko.Communication.WebSocket.Servers` - Standalone server
- `Birko.Communication.WebSocket.Middleware` - ASP.NET Core middleware
- `Birko.Communication.WebSocket.Services` - Authentication services
- `Birko.Communication.WebSocket.Proxy` - WebSocket reverse proxy

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.SSE](../Birko.Communication.SSE/) - Server-Sent Events (one-way push)
- [Birko.Communication.REST](../Birko.Communication.REST/) - REST API client/server

## License

Part of the Birko Framework.
