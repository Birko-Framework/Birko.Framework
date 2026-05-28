---
id: EPIC-012
status: planned
created: 2026-05-28
owner: ai
affects: [Birko.MessageQueue.MQTT]
---

# Birko.MessageQueue.MQTT — v5 features

## Area of concern

MQTT v5 protocol features on top of the existing MQTT v3.1.1 adapter — topic aliases and user properties. Driven by bandwidth optimization for high-frequency IoT sensors where repeated full topic strings cost real money.

## Success criteria

- `MqttExtensions` exposes topic-alias + user-properties API
- Consumers opt into v5 features when the broker supports them; v3.1.1 path stays default
- Falls back gracefully when v5 capabilities are absent
