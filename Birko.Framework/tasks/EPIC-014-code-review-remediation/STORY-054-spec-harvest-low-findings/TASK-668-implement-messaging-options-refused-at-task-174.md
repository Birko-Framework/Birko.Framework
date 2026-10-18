---
id: TASK-668
parent: STORY-054
feature: FEATURE-014
status: todo
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

- [ ] Each implemented option lifts its refusal on the transports that now honour it, with live tests
- [ ] What stays unimplemented stays refused, and the docs table says which transport honours what

## Human test plan

N/A — library behaviour, covered by live tests.
