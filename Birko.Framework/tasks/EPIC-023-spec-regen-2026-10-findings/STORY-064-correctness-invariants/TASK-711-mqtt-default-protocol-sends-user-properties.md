---
id: TASK-711
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: ai
created: 2026-10-21
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [SH-T696-06]
pr: null
github-issue: null
jira-key: null
---

# MQTT: every publish on the default 3.1.1 connection carries a user property

## Context

Suspected by the TASK-696 spec harvest (22a2bb77), read from the code and **not measured**. `QueueMessage.Headers` is never null, so `MqttProducer` adds a `headers` user property to every publish; MQTTnet refuses user properties on a 3.1.1 connection, the default, so every default send may throw. Unverified: there is no live MQTT suite. Also a `TimeToLive` sent before `ConnectAsync` is refused as 3.1.1 even when the settings say V500, and a null message throws `NullReferenceException`. Related, not a duplicate: TASK-034 (v5 user properties as a feature).

Evidence: Birko.MessageQueue.MQTT/MqttProducer.cs:39-58,77-80,96; QueueMessage.cs:28.

## Acceptance criteria

- [x] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [x] Each reproduced defect fixed, with a regression test that fails without the fix.
- [x] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Measured (2026-11-03, before the fix)

Through `MqttMessageQueue` against MQTTnet's own broker, in process.

| Claim | Result |
|---|---|
| Every publish on the default 3.1.1 connection carries a user property | **reproduced, and worse:** `SendAsync(QueueMessage)` and `SendAsync<T>` both threw `NotSupportedException: Feature UserProperties requires MQTT version 5.0.0`; nothing was received. Only the raw `PublishAsync(topic, string)` worked |
| A `TimeToLive` before `ConnectAsync` is refused as 3.1.1 under V500 settings | **reproduced** (`IMqttClient.Options` is null until connected) |
| A null message throws `NullReferenceException` | **reproduced**, before and after connecting |

Symbio's IoT module sends device commands with `SendAsync<T>` on default (3.1.1) settings, so each one threw.

## Implementation plan

On 3.1.1 send no user property. What only one could carry is refused (rule 41, as SH-M113 and SH-L108 refused
Delay and TimeToLive): `CorrelationId`, `ReplyTo`, `GroupId`, `Custom`, a `ContentType` other than the
serializer's or the default. The `PayloadType` is not refused (a typed subscriber deserializes as its own type,
and every typed send sets it). The protocol is the connection's, or the settings' before one is made. A null
message is `ArgumentNullException`.

## Built (2026-11-03)

`tests/Birko.MessageQueue.MQTT.Tests/Task711MqttTests.cs` (12), one through a real in-process MQTTnet broker on
the default protocol; 10 of them fail against the old producer (the other two guard MQTT 5 and the connected
protocol). `MqttMetadataRoundTripTests`' producer tests now declare V500, which they were about. Re-measured live:
3.1.1 plain and typed sends delivered, V500 unchanged. MQTT 64/64, MessageQueue 85/85. Spec
`event-bus-and-messaging.md` updated (scoped; `generated-at` left).
