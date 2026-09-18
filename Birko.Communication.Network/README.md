# Birko.Communication.Network

TCP and UDP **ports** for the Birko Framework, built on `Birko.Communication`'s `AbstractPort`
abstraction — the same byte-stream port model used by the serial and hardware ports, over sockets.

## Features

- TCP outbound client (`TcpIp`)
- UDP peer — send to a configured endpoint, receive on a bound local port (`Udp`)
- UDP **multicast receive** with group join, TTL and address reuse for shared discovery ports
- Event-driven reads via `SubscribeProcessData`, served by a background thread
- Thread-safe read buffer with peek (`Read`) and consume (`RemoveReadData`) semantics

### ⚠ Not included

| | |
|---|---|
| TCP server / listener | ❌ use `Birko.Communication.WebSocket`, `.REST.Server` or `.gRPC.Server` |
| `Task`-based async API | ❌ the surface is synchronous; reading happens on a background `Thread` |
| IPv6 multicast | ❌ same mechanism, untested — no use case named |

## Installation

Shared project — import the `.projitems` from your aggregator, using `$(BirkoSrc)`:

```xml
<Import Project="$(BirkoSrc)\Birko.Communication.Network\Birko.Communication.Network.projitems"
        Label="Shared" />
```

## Dependencies

- `Birko.Communication` — `AbstractPort`, `PortSettings`, `ProcessDataDelegate`
- BCL `System.Net.Sockets`

## Usage

### TCP client

```csharp
using System.Text;
using Birko.Communication.Network.Ports;

var port = new TcpIp(new TcpIpSettings
{
    Name    = "telemetry",
    Address = "127.0.0.1",
    Port    = 8080,
});

port.SubscribeProcessData(() =>
{
    var payload = port.RemoveReadData(-1);   // -1 = everything buffered
    Console.WriteLine(Encoding.UTF8.GetString(payload));
});

port.Open();
port.Write(Encoding.UTF8.GetBytes("Hello"));

port.Close();
```

### UDP multicast (LAN discovery)

```csharp
var port = new Udp(new UdpSettings
{
    Name           = "ssdp",
    Address        = "239.255.255.250",  // Write() sends to the group
    Port           = 1900,
    LocalPort      = 1900,               // bind the group's port to receive
    MulticastGroup = "239.255.255.250",  // join is required to RECEIVE
    ReuseAddress   = true,               // the OS resolver is already bound here
});

port.Open();
```

⚠ **Joining is required to receive, never to send.** Sending to a group works without membership.

### UDP unicast

```csharp
var port = new Udp(new UdpSettings
{
    Name      = "discovery",
    Address   = "192.168.1.50",   // where Write() sends
    Port      = 9000,             // remote port
    LocalPort = 9001,             // bind here to receive; 0 = any available
});

port.Open();
port.Write(Encoding.UTF8.GetBytes("ping"));

if (port.HasReadData(4))
{
    byte[] four = port.RemoveReadData(4);
}
```

`Write()` opens the port automatically if it is closed.

## API Reference

`namespace Birko.Communication.Network.Ports`

### `TcpIpSettings : PortSettings`
| Member | |
|---|---|
| `Address` | remote host |
| `Port` | remote port |
| `GetID()` | `TcpIp\|{Name}\|{Address}\|{Port}` |

### `UdpSettings : PortSettings`
| Member | |
|---|---|
| `Address` | remote host that `Write()` targets |
| `Port` | remote port |
| `LocalPort` | local bind port for receiving (0 = any) |
| `MulticastGroup` | group to join on `Open()`, e.g. `239.255.255.250`; null = no join |
| `ReuseAddress` | allow other sockets to bind the same `LocalPort` — required for mDNS/SSDP |
| `MulticastTtl` | hop limit; null = OS default of 1 (link-local) |
| `GetID()` | `Udp\|{Name}\|{Address}\|{Port}\|{LocalPort}`, with `\|mc=…` / `\|reuse` / `\|ttl=…` appended only when set |

### `TcpIp : AbstractPort` · `Udp : AbstractPort`

Both implement `Write(byte[])`, `Read(int)`, `Open()`, `Close()`, `HasReadData(int)`,
`RemoveReadData(int)`, and inherit `Settings`, `ReadData`, `IsOpen()`, `Clear()`, `IsEmpty()`,
`GetData()`, `SubscribeProcessData(…)`, `UnSubscribeProcessData(…)` and `Dispose()` from
`AbstractPort`.

**Buffer semantics:** `Read(size)` *peeks* without consuming; `RemoveReadData(size)` reads and
removes. A negative `size` means "everything currently buffered", so `HasReadData(-1)` answers
"is there any data at all".

## Notes

- **TCP is a stream.** One `Write` does not equal one `ProcessData` callback. Frame your protocol with
  a length prefix or a delimiter.
- **Consume with `RemoveReadData`**, not `Read` — otherwise the next call sees the same bytes again.
- ⚠ **Not suited to lockstep game networking as-is.** UDP here has no sequencing, acknowledgement or
  retransmit; adding them is reimplementing TCP. Use `TcpIp` or
  [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/) where reliable ordered delivery
  is needed.

## Related Projects

- [Birko.Communication](../Birko.Communication/) — `AbstractPort` / `IPort` base abstractions
- [Birko.Communication.WebSocket](../Birko.Communication.WebSocket/) — WebSocket client **and server**

## License

Part of the Birko Framework.
