---
id: STORY-013
parent: EPIC-006
status: planned
created: 2026-05-28
---

# MassTransit adapter

## User story

As a developer already using MassTransit, I want a Birko.MessageQueue adapter that delegates to it so the rest of my code uses Birko abstractions.

## Behaviour

- Thin adapter implementing `IMessageQueue` backed by MassTransit's `IBus`
- Routes Birko publish/subscribe calls into MassTransit's pipeline
- Doesn't expose MassTransit middleware concepts upward — they stay configured on MassTransit
