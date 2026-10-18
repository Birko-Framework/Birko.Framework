---
id: TASK-668
parent: STORY-054
feature: FEATURE-014
status: done
priority: P3
assignee: ai
created: 2026-10-18
depends-on: []
blocks: []
findings: [SH-L108, SH-L109, SH-L110, SH-L114]
pr: null
github-issue: null
jira-key: null
---

# Implement the messaging options TASK-174 refuses: TimeToLive, PrefetchCount, retry / dead-letter, Priority

## Context

Spawned by [[TASK-174]]. These options were read by no transport (or by Redis only), so a caller who set one got
nothing. **Decided by the user (2026-10-18): refuse them now (`NotSupportedException`, through
`Birko.MessageQueue.TransportSupport`) and implement them here, lifting each refusal as it lands.** No consumer repo
sets any of them today. ReplyTo / GroupId stay as carried headers and are not part of this task.

- **TimeToLive:** honoured on Redis only. In-memory can drop an expired message at dispatch; MQTT 5 has a message
  expiry interval (`WithMessageExpiryInterval`) — MQTT 3.1.1 would still refuse.
- **PrefetchCount:** maps to Redis's read batch (XREADGROUP COUNT), in-memory concurrency, MQTT 5 receive maximum.
- **Retry / dead-letter (`DistributedEventBusOptions.RetryPolicy` / `DeadLetterOptions`, now null and refused):**
  with the delivery count TASK-174 added (`x-delivery-count` on Redis reclaim and in-memory requeue), "after N
  failed deliveries move the message to `{topic}.dlq`" is implementable on Redis and in-memory. Note the options'
  `RetryPolicy` resolves to `Birko.RetryPolicy` (Contracts), not `Birko.MessageQueue.Retry.RetryPolicy` — pick one.
- **Priority:** the least likely; Redis Streams and MQTT have no ordering by it. Decide whether to drop it.

## Acceptance criteria

- [x] Each implemented option lifts its refusal on the transports that now honour it, with live tests
- [x] What stays unimplemented stays refused, and the docs table says which transport honours what

## Human test plan

N/A — library behaviour, covered by live tests.

## Outcome (2026-10-18)

Each option is honoured where a transport truly can, and refused elsewhere (rule 55).

- **TimeToLive.**
  - In-memory: an expired message (counted from `CreatedAt` on the new `InMemoryMessageQueueOptions.Clock`) is dropped
    at dispatch and by `ReadAsync`.
  - MQTT: the MQTT 5 message expiry interval is sent when the new opt-in `MqttSettings.ProtocolVersion = V500` is set.
    Birko had always connected with 3.1.1, where no expiry exists. Still refused on 3.1.1, and for a TTL that is not a
    whole number of seconds from 1.
  - Redis: unchanged.
- **PrefetchCount** (below 1 throws `ArgumentOutOfRangeException`).
  - Redis: an explicit value is the read and reclaim batch, measured live as the entries pending. The default keeps
    `ReadCount`.
  - In-memory: that many handlers in flight per subscription, measured at a peak of 3. The default runs one at a time,
    as before.
  - MQTT: still refused, because the receive maximum is per connection (protocol spec; no broker to measure against).
- **Retry and dead-letter on `DistributedEventBus`.** `RetryPolicy` is `global::Birko.RetryPolicy` (decided by the user);
  the enclosing namespace had silently been winning the lookup.
  - New `IRedeliveryCapability`: in-memory `Resend`, Redis `Redelivered` (with a consumer group and reclaim on), MQTT
    none, which is refused at construction.
  - A failed delivery is retried while `x-delivery-count` ≤ `MaxRetries`. On in-memory the bus re-sends after
    `GetDelay`; on Redis the transport reclaims, after `PendingRetryMilliseconds`.
  - After that the message is dead-lettered to `{topic}.dlq`, with `x-original-destination` and
    `x-dead-letter-reason`, or dropped through `MessageDropped`.
  - Measured: 3 deliveries then the DLQ in memory; 2 then the DLQ live on Redis.
- **Priority.** Kept and refused everywhere (decided by the user: for a future RabbitMQ/ActiveMQ transport). Documented.
- **Tests.** Red counts are with HEAD production plus stubs for the new members.
  - New: in-memory 5 (4 red), Redis 4 (3 red), MQTT 6 (1 truly red; 2 red only on new text or a new setting; 3 pins),
    bus 5 + 1 inverted (6 red).
  - Inverted: `Task174InMemoryTests` TTL/prefetch, `Task174RedisTests` prefetch, `Task174DistributedBusTests.SH_L114`.
  - A test race was fixed: XPENDING before the group existed returned NOGROUP. Redis then passed 5/5 runs.
- **Regression.** Live, all 8 importers green: EventBus.MessageQueue 20, Outbox.SQL 17, Outbox 13, EventBus 133,
  MQ.InMemory 22, MQ.MQTT 52, MQ.Redis 68, MessageQueue 85. Sandbox builds.
- **Docs and spec.** `message-queue.md` gains a which-transport-honours-what table and a retry/dead-letter section; its
  `Priority = 10` example, which throws, is fixed. The `event-bus-and-messaging` spec requirement is now "Transports
  honour message and consumer options or refuse them", with its scenarios rewritten. The CLAUDE.md of five projects
  are updated.
- **Not done.**
  - That `ConnectAsync` connects with MQTT 5 is checked by inspection only: there is no broker.
  - A second `Birko.MessageQueue.Retry.RetryPolicy` type still exists, and `Birko.MessageQueue/CLAUDE.md` says the
    type comes from Contracts. Both are left as they were.
