# Birko.Communication.SSE.AspNetCore.Tests

xUnit + FluentAssertions over a TestServer (`Microsoft.AspNetCore.TestHost`). Frames are compared with
`SseEvent.ToString()`, never with hand-written frame text, so the tests hold the one-producer rule.

Covers: frames and headers, the stream ending with its source, a terminal item, heartbeats while idle (the pending item is
kept), a `retry:` frame, a client disconnect ending the loop and releasing the source and `owns`, feeding from
`KeyedEventBroadcaster` (imports Birko.EventBus for that), and refused options.
