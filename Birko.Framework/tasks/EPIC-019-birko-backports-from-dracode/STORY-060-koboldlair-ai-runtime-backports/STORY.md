---
id: STORY-060
parent: EPIC-019
status: planned
created: 2026-10-08
---

# KoboldLair agent-runtime and data backports from DraCode

## User story

As a **Birko consumer building agents on `Birko.AI`**, I want hooks in the agent loop, a plan model with behaviour,
working event-sourcing and resilience stores, structured LLM failures, a local secret provider, a live event broadcaster,
git tooling and conversation checkpoints **in the framework**, so that the next agent host does not fork the `Agent`
loop and rewrite these pieces the way KoboldLair did.

## Where it came from

A 2026-10-08 review of `Consumers/DraCode/DraCode.KoboldLair` (~37.5k lines). Headline: DraCode imports
`Birko.AI.Orchestration` — which was extracted *from* it — and uses none of it. It keeps its own forks of the
dispatcher, `StepDependencyAnalyzer`, plan model and alert types, and `Kobold.cs` carries a ~375-line copy of `Agent`'s
loop that reads the protected `SystemPrompt` by reflection.

Kept in DraCode as policy: agent personas and prompts (Dragon, Wyrm, Wyvern, Kobold planner, sub-agents), the
Dragon→Wyrm→Wyvern→Drake→Kobold orchestrators, the ~40 project / feature / spec / plan tools, the domain models and
their persistence, provider-per-agent-type configuration, `RunRegistry`, `TaskTracker`.

## Done when

Each task's DraCode adoption can delete its local copy.
