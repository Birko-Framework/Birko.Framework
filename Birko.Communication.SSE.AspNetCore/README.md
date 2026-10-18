# Birko.Communication.SSE.AspNetCore

Server-Sent Events as an ASP.NET Core minimal-API result, with a heartbeat, a terminal stop and proxy-friendly headers.

## Features
- `SseResults.Stream` over a `ChannelReader<T>` or `IAsyncEnumerable<T>`
- `: heartbeat` comment when idle (configurable, can be turned off)
- stops after a terminal item; a client disconnect ends the loop; an owned resource (a subscription) is always disposed
- `Cache-Control: no-cache`, `X-Accel-Buffering: no`, buffering off
- frames formatted by `Birko.Communication.SSE`'s `SseEvent`

## Usage
```csharp
app.MapGet("/runs/{id}/events", (Guid id, KeyedEventBroadcaster<Guid, RunEvent> live) =>
{
    var subscription = live.Subscribe(id);
    return SseResults.Stream(subscription.Reader,
        e => SseEvent.FromJson(e.Event, e.Event.Kind, e.Sequence.ToString()),
        isTerminal: e => e.Event.IsFinal,
        options: new SseStreamOptions { HeartbeatInterval = TimeSpan.FromSeconds(20) },
        owns: subscription);
});
```

## Dependencies
Birko.Communication.SSE; the host supplies `Microsoft.AspNetCore.App`.

## Running tests
`dotnet test tests/Birko.Communication.SSE.AspNetCore.Tests`

## License
See the repository root `LICENSE`.
