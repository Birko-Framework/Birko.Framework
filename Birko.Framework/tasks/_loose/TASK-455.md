---
id: TASK-455
parent: null
feature: null
status: done
priority: P3
picked-by: user-instruction
assignee: ai
created: 2026-09-17
depends-on: []
blocks: []
related: []
findings: []
pr: "Birko.Communication.Network d62e6cd · tests be5b330"
github-issue: null
jira-key: null
affects: [Birko.Communication.Network]
---

# `Udp` cannot receive multicast, and binds exclusively — LAN device discovery is not expressible

## Context

Found 2026-09-17 while auditing `Birko.Communication.Network` (its `CLAUDE.md` / `README.md` drift was
fixed separately). The framework has a device-facing story — `Birko.Communication.Camera`, `.Hardware`,
`.Modbus`, `.Bluetooth`, `.NFC` — and the root `CLAUDE.md` names **IoT** as a target domain. LAN device
discovery (mDNS, SSDP) is the standard mechanism there, and it is **multicast receive**, which `Udp`
cannot do.

⚠ **This is a missing capability, not a defect.** Nothing today asks for it: **0** consumer `.cs`
references to `Udp`/`UdpSettings` across the 16 repos. Filed so the gap is on record with its
measurements rather than rediscovered; **do not build it speculatively** — the framework's own
blast-radius discipline says wait for a caller.

## Measured, not assumed

Probed on this machine 2026-09-17 (.NET 10, Windows), loopback, real `UdpClient`:

| Behaviour | Result |
|---|---|
| Broadcast send **without** `EnableBroadcast` | ✅ **delivered** (`GOT 3B`) |
| Broadcast send **with** `EnableBroadcast = true` | ✅ delivered — **no difference** |
| Multicast **send** (no join) | ✅ works — sending needs no group membership |
| Multicast **receive without** `JoinMulticastGroup` | ❌ **nothing received** |
| Multicast **receive with** `JoinMulticastGroup` | ✅ `GOT 2B` |

### ⚠ Two things this corrected

1. **Broadcast is NOT broken.** The initial hypothesis was that `Udp` cannot broadcast because it never
   sets `EnableBroadcast` and that the send would throw `SocketException`. **Measured false** — it
   neither throws nor fails to deliver. Do not file or "fix" broadcast on that premise.
2. **"No exception" is not "delivered."** The first probe only checked that `Send` did not throw, which
   proved nothing about a fire-and-forget protocol. The table above is a real receiver on a real
   socket. Any future probe here must assert **receipt**, not the absence of a throw.

**Limit of the measurement:** loopback on one host. `EnableBroadcast` may still matter on a routed
network or a particular NIC/driver, so the broadcast row is "not reproducible here", not "the flag is
useless everywhere". Re-measure on real hardware before removing broadcast from scope entirely.

## The two gaps

### 1. No `JoinMulticastGroup` — confirmed blocking

`Udp.Open()` does `new UdpClient(settings.LocalPort)` and never joins a group, so a multicast listener
is not expressible at any binding of the existing settings. This is the gap that matters.

### 2. Exclusive bind — blocks the shared discovery ports

`new UdpClient(port)` binds exclusively on Windows. mDNS (`224.0.0.251:5353`) and SSDP
(`239.255.255.250:1900`) are **shared by design** — the OS resolver and other applications are already
listening. Without

```csharp
client.ExclusiveAddressUse = false;
client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
```

a Birko `Udp` port cannot coexist with them, so even a correct multicast join would fail to bind in the
exact scenario that motivates it. **The probe above needed both options to work** — that is how this
was found. Fixing (1) without (2) ships something that still does not work for its only real use case.

## Proposed shape

Extend `UdpSettings` rather than adding a port type — the read loop, buffering and close ordering are
identical, and a second type would duplicate `ReadWorker` (the duplication this codebase repeatedly
records as the cause of drift):

```csharp
public string? MulticastGroup { get; set; }   // join on Open() when set
public bool ReuseAddress { get; set; }        // default false = today's behaviour
public int? MulticastTtl { get; set; }        // default 1 = link-local
```

- `GetID()` must include any new field that changes socket behaviour, or two different configurations
  collide on one id.
- `DropMulticastGroup` on `Close()`, before the client is disposed.
- **TTL defaults to 1** (link-local), which is correct for discovery; a knob is only needed for routed
  multicast.

## Acceptance criteria

- [x] 1. A `Udp` port with `MulticastGroup` set **receives** a datagram sent to that group. Asserted by
      **receipt of bytes**, never by "no exception was thrown" — see the correction above.
- [x] 2. Two `Udp` ports on the same machine can both bind the same port with `ReuseAddress`, and
      **both** receive the same multicast datagram. This is the case (2) exists for; one listener does
      not prove it.
- [x] 3. Existing behaviour is byte-identical when the new fields are unset — a test pins unicast
      send/receive unchanged, since the default must remain exclusive bind and no join.
- [x] 4. `GetID()` distinguishes two settings differing only in a new field.
- [x] 5. `Close()` drops the group before disposing, and a second `Open()`/`Close()` cycle still works.
- [x] 6. Docs updated in the same change — `CLAUDE.md` and `README.md` both currently state multicast
      and broadcast are **not** supported (accurate as of 2026-09-17), and those lines must move with
      the code.

## Out of scope

- **Broadcast.** Measured working; no change justified without a reproduction on real hardware.
- IPv6 multicast — same mechanism, but no use case named and untested here.
- Any reliability layer (sequencing, acks, retransmit). If that is ever wanted, it is TCP's job —
  see the game-engine notes on why lockstep does not want UDP.
- A TCP listener. Separate, larger gap: **`TcpListener` appears nowhere in the framework**, so
  `Birko.Communication.Network` is outbound-only. Worth its own task if a consumer ever needs it.

## Progress log

- **Picked by explicit user instruction**, overriding this task's own *"do not build it speculatively"*
  guidance. Recorded because the guidance was right on its own terms (0 consumers) and the decision to
  build anyway was the user's, not a re-reading of the evidence.
- **Implemented** on `UdpSettings` rather than as a new port type: the read loop, buffering and close
  ordering are identical, and a second type would duplicate `ReadWorker`.
- ⚠ **`new UdpClient(localPort)` had to go.** It binds inside its own constructor, so there is no
  window in which to set the reuse options — they must be set *before* the bind. `Open()` now creates
  an unbound client, sets options, then `Bind`s explicitly.
- **Mutations, disjoint, both measured:**
  - disable the `JoinMulticastGroup` branch → **3 of 34 fail** (both multicast receive tests + the
    close/reopen cycle)
  - disable the `ReuseAddress` branch → **exactly 1 of 34 fails**, and it is
    `TwoPortsWithReuseAddress_BothReceiveTheSameDatagram`
- **Final: 34 passed, 0 failed** (25 pre-existing + 9 new).
- ⚠ **Each new capability has a CONTROL test**, because a single positive proves nothing here:
  `WithoutMulticastGroup_DoesNotReceiveGroupDatagram` shows the join is what delivers, and
  `WithoutReuseAddress_SecondBindOnSamePortThrows` shows the reuse flag is load-bearing rather than
  decorative. Without those, mutation A and B could both have been explained away.
- **Tests assert bytes received, never "no exception was thrown"** — the mistake made while first
  measuring this defect, recorded above under *Measured, not assumed*.
- **Criterion 3 held without a second code path**: `GetID()` appends only when a field is set, so the
  pre-existing `UdpSettings_GetID_FormatsAsExpected` test passes **unchanged**.
- Docs updated in the same change (criterion 6) — both files had just been rewritten from source after
  documenting a fictional API, and the multicast/broadcast rows moved with the code.

### Residue

- **Broadcast row now reads "works — measured"**, which is true on loopback on this machine and was
  *not* re-measured on real hardware. Out of scope as filed; the limit is stated on both the task and
  in `CLAUDE.md`.
- **IPv6 multicast** is listed as not included in `README.md` rather than silently absent, so the gap
  reads as a decision.

