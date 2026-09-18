# Birko.Communication.Network

## Overview
TCP and UDP **ports** built on `Birko.Communication`'s `AbstractPort` abstraction — the same
byte-stream port model used by the serial / hardware ports, over sockets instead of a cable.

## Project Location
Shared project (`.projitems`) — `$(BirkoSrc)\Birko.Communication.Network\`

## ⚠ Scope — read before designing against this

| | Supported |
|---|---|
| TCP **outbound client** | ✅ `TcpIp` |
| TCP **server / listener** | ❌ none — there is no `TcpListener` anywhere in the framework |
| UDP send to a configured peer | ✅ `Udp` |
| UDP receive (bind a local port) | ✅ `Udp` via `UdpSettings.LocalPort` |
| UDP **multicast receive** | ✅ `UdpSettings.MulticastGroup` + `ReuseAddress` (TASK-455) |
| UDP **broadcast** | ✅ works — measured, needs no `EnableBroadcast` on this stack |
| **Async** API (`Task`-returning) | ❌ synchronous surface; reads run on a background **thread** |

For a listening server, use `Birko.Communication.WebSocket` (which ships `Servers/`) or
`Birko.Communication.REST.Server` / `.gRPC.Server`.

## Components

`namespace Birko.Communication.Network.Ports`

### `Ports/TcpIp.cs`
- **`TcpIpSettings : PortSettings`** — `Address`, `Port`; `GetID()` → `TcpIp|{Name}|{Address}|{Port}`
- **`TcpIp : AbstractPort`** — outbound TCP client over `TcpClient` + `NetworkStream`

### `Ports/Udp.cs`
- **`UdpSettings : PortSettings`** — `Address`, `Port`, `LocalPort` (0 = any available);
  plus `MulticastGroup`, `ReuseAddress`, `MulticastTtl`.
  `GetID()` → `Udp|{Name}|{Address}|{Port}|{LocalPort}` with `|mc=…`, `|reuse`, `|ttl=…` **appended
  only when set**, so an id that uses none of them is byte-identical to the pre-TASK-455 format
- **`Udp : AbstractPort`** — sends to the single configured remote endpoint, receives from
  `IPAddress.Any` on `LocalPort`; joins `MulticastGroup` on `Open()` and drops it on `Close()`

Both override the same six abstract members: `Write`, `Read`, `Open`, `Close`, `HasReadData`,
`RemoveReadData`.

## Inherited from `AbstractPort` (in `Birko.Communication`)

`Settings`, `ReadData` (`List<byte>`), `IsOpen()`, `Clear()`, `IsEmpty()`, `GetData()`,
`SubscribeProcessData(ProcessDataDelegate)` / `UnSubscribeProcessData(…)`, `Dispose()`.

## Usage — TCP client

```csharp
using Birko.Communication.Network.Ports;

var port = new TcpIp(new TcpIpSettings
{
    Name = "telemetry",
    Address = "127.0.0.1",
    Port = 8080,
});

port.SubscribeProcessData(() =>
{
    // fired by the background read thread when bytes arrive
    var payload = port.RemoveReadData(-1);   // -1 = take everything buffered
});

port.Open();
port.Write(Encoding.UTF8.GetBytes("Hello"));
// …
port.Close();
```

## Usage — UDP

```csharp
var port = new Udp(new UdpSettings
{
    Name = "discovery",
    Address = "192.168.1.50",  // where Write() sends
    Port = 9000,               // remote port
    LocalPort = 9001,          // bind here to receive; 0 = any
});

port.Open();
port.Write(Encoding.UTF8.GetBytes("ping"));

if (port.HasReadData(4))
{
    var four = port.RemoveReadData(4);
}
```

`Write()` auto-opens the port if it is closed.

## Key Patterns

- **Buffer semantics.** `Read(size)` **peeks** — it does not consume. `RemoveReadData(size)` reads
  *and* removes. `size < 0` means "everything currently buffered"; `HasReadData(-1)` is therefore
  "is there any data at all" (CR-H027).
- **`ReadData` is locked** on every access, because a background thread appends to it while callers
  read (CR-H026). Do not touch `ReadData` directly from outside — use `Read` / `RemoveReadData` /
  `GetData`.
- **Reads are a background `Thread`, not a `Task`.** `TcpIp` polls `NetworkStream.DataAvailable` with
  a 50 ms sleep; `Udp` blocks in `UdpClient.Receive`.
- **Close ordering differs per port and is deliberate.** `Udp` disposes the client **first** to unblock
  the blocking `Receive`, *then* joins the thread. `TcpIp` signals and joins first, because its worker
  is poll-gated and never blocks indefinitely (CR-L069). Do not "unify" these.
- **Settings are validated at `Open()`**, via `Settings as XSettings` → `InvalidOperationException`.
- **⚠ Multicast: joining is required to RECEIVE, never to send.** Measured 2026-09-17 — a datagram
  sent to a group arrives only at sockets that called `JoinMulticastGroup`, while sending to a group
  needs no membership at all. `Udp` binds explicitly (rather than via `new UdpClient(port)`) precisely
  because the reuse options must be set *before* the bind.
- **⚠ `ReuseAddress` needs both `ExclusiveAddressUse = false` and `SO_REUSEADDR` on Windows.** It is
  what lets a port coexist with the OS resolver on the shared discovery ports (mDNS 5353, SSDP 1900) —
  without it the second binder gets a `SocketException`, which is the case its test pins.

## Dependencies
- `Birko.Communication` (`AbstractPort`, `PortSettings`, `ProcessDataDelegate`)
- BCL `System.Net.Sockets`

## Use Cases
- Custom byte-oriented network protocols
- IoT / device communication over LAN
- Talking to equipment that exposes a raw TCP or UDP endpoint

⚠ **Not suited to lockstep game networking as-is.** UDP here has no sequencing, acks or retransmit —
building those is reimplementing TCP. Use `TcpIp` or `Birko.Communication.WebSocket` for anything
needing reliable ordered delivery.

## Best Practices

1. **Design a framed protocol** — TCP is a stream, so `Read(n)` may return fewer bytes than a logical
   message. Use a length prefix or a delimiter; do not assume one `Write` equals one `ProcessData`.
2. **Consume with `RemoveReadData`**, not `Read` — `Read` leaves the buffer intact and the next call
   sees the same bytes again.
3. **`Close()` then `Open()`** to change endpoint; settings are only read at open time.
4. **Dispose the port** (or `Close()`) — the read thread is a background thread, but the socket is not
   released until then.

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

⚠ **2026-09-17:** this file and `README.md` both documented an API that never existed —
`TcpCommunicator`, `TcpServer`, `UdpCommunicator`, `UdpServer`, `NetworkSettings`, `NetworkEndpoint`,
all **0 matches** framework-wide — with usage examples that could not compile, plus claims of a TCP
server and async support that are not implemented. Both were rewritten from the source. **Verify
against `Ports/*.cs` before trusting either file.**

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in `Birko.Communication.Network.Tests`
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
