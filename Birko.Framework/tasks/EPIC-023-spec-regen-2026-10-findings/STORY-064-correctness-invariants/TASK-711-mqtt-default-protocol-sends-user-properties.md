---
id: TASK-711
parent: STORY-064
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
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

- [ ] Reproduce each claim (a test or a live measurement) and record the result; a claim that does not reproduce is
      closed with its evidence, never quietly dropped (rule 54).
- [ ] Each reproduced defect fixed, with a regression test that fails without the fix.
- [ ] The spec area that describes the behaviour updated to the fixed behaviour.

## Out of scope

- Findings of other tasks in EPIC-023.

## Human test plan

N/A — covered by automated tests once reproduced.

## Implementation plan

_Populated by `/tasks plan TASK-711` — leave empty until then._
