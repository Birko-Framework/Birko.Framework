---
id: TASK-034
feature: FEATURE-012
parent: EPIC-012
status: todo
priority: P2
assignee: ai
created: 2026-05-28
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# MQTT v5 topic aliases + user properties

## Context

Add MQTT v5 protocol features on top of the existing MQTT v3.1.1 adapter — topic aliases (broker-coordinated short codes for repeated topic strings) and user properties (key-value metadata on messages). Low priority unless high-frequency IoT sensors need bandwidth optimization.

## Acceptance criteria

- [ ] `MqttExtensions` exposes a topic-alias API (assign / lookup / clear)
- [ ] Publish / subscribe APIs accept user-properties dictionary
- [ ] v3.1.1 path is the default; v5 features are opt-in
- [ ] Falls back gracefully when broker doesn't advertise v5 capabilities
- [ ] Tests against an MQTT v5–capable broker (Mosquitto in Testcontainers)

## Out of scope

- Other v5 features (request/response, shared subscriptions) — separate follow-ups if needed
