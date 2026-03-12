# Birko.Communication.SSE

## Overview
Server-Sent Events (SSE) client implementation for Birko.Communication.

## Project Location
`C:\Source\Birko.Communication.SSE\`

## Purpose
- Server-Sent Events client
- Real-time server push
- Event streaming
- One-way communication

## Components

### Client
- `SSEClient` - SSE client
- `AsyncSSEClient` - Async SSE client

### Models
- `SSEEvent` - SSE event
- `SSESettings` - Client settings

## Basic Usage

```csharp
using Birko.Communication.SSE;

var client = new SSEClient("https://api.example.com/events");

client.MessageReceived += (sender, e) =>
{
    Console.WriteLine($"Event: {e.EventType}");
    Console.WriteLine($"Data: {e.Data}");
    Console.WriteLine($"ID: {e.Id}");
};

await client.ConnectAsync();
```

## Event Format

SSE events follow the format:
```
id: 123
event: message
data: Hello World

```

## Reconnection

```csharp
client.AutoReconnect = true;
client.LastEventId = "123"; // Resume from last event
```

## Custom Headers

```csharp
client.AddHeader("Authorization", "Bearer token");
client.AddHeader("Accept", "text/event-stream");
```

## Dependencies
- Birko.Communication
- System.Net.Http

## Use Cases
- Real-time notifications
- Live feeds
- Server push updates
- Stock tickers
- Chat notifications
- Progress updates

## Best Practices

1. **Reconnection** - Implement exponential backoff
2. **Event ID** - Use Last-Event-ID for resume
3. **Connection state** - Monitor connection state
4. **Error handling** - Handle connection errors
5. **Disposal** - Always dispose the client

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
