# Birko.Communication.SSE

Server-Sent Events (SSE) client and server library for the Birko Framework, implementing the W3C SSE specification for real-time one-way server push.

## Features

- SSE client for receiving event streams from HTTP endpoints
- SSE server for pushing real-time events to connected clients
- W3C-compliant event format (Id, Event, Data, Retry)
- Middleware pipeline (authentication, rate limiting, CORS, logging)
- Client connection tracking and management
- Event-driven architecture with typed events
- Custom header support for authentication

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.SSE\Birko.Communication.SSE.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** - Base communication interfaces
- **System.Net.Http** - HTTP client for SSE connections
- **System.Text.Json** - JSON serialization for event data

## Usage

### SSE Client

```csharp
using Birko.Communication.SSE;

var client = new SseClient("https://api.example.com/events");

client.OnConnected += (sender, e) => Console.WriteLine("Connected");
client.OnMessage += (sender, message) => Console.WriteLine($"Message: {message}");
client.OnEvent += (sender, sseEvent) =>
{
    Console.WriteLine($"Event: {sseEvent.Event}, Data: {sseEvent.Data}, ID: {sseEvent.Id}");
};

await client.ConnectAsync();
```

### SSE Server

```csharp
using Birko.Communication.SSE;

var server = new SseServer();
server.OnClientConnected += (sender, connection) =>
{
    Console.WriteLine("Client connected");
};

// Broadcast an event to all connected clients
var evt = new SseEvent
{
    Event = "update",
    Data = "{\"status\": \"active\"}",
    Id = "1"
};
await server.BroadcastAsync(evt);
```

### Middleware Pipeline

```csharp
using Birko.Communication.SSE.Middleware;

var config = new SseAuthenticationConfiguration { /* ... */ };
var authService = new SseAuthenticationService(config);

// Rate limiting, CORS, and logging middleware are also available
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `SseClient` | Client for receiving SSE streams, implements `IDisposable` |
| `SseServer` | Server for pushing events to clients, implements `IDisposable` |
| `SseEvent` | W3C SSE event model (Id, Event, Data, Retry) |
| `SseMiddleware` | Base middleware pipeline (SseContext, SseRequestDelegate) |
| `SseAuthenticationMiddleware` | Authentication middleware |
| `SseRateLimitMiddleware` | Rate limiting middleware |
| `SseCorsMiddleware` | CORS middleware |
| `SseLoggingMiddleware` | Request/response logging middleware |
| `SseAuthenticationService` | Authentication logic |
| `SseAuthenticationConfiguration` | Authentication settings |

### Namespaces

- `Birko.Communication.SSE` - Client, server, and event model
- `Birko.Communication.SSE.Middleware` - Middleware pipeline components

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/) - Full-duplex WebSocket communication
- [Birko.Communication.REST](../Birko.Communication.REST/) - REST API client/server

## License

Part of the Birko Framework.
