---
id: TASK-535
parent: STORY-060
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Conversation checkpoints: serialize / restore, and a trim that keeps tool_use and tool_result together

## Context

DraCode's `Models/Agents/ConversationCheckpoint.cs` (55) and `KoboldPlanService.cs` lines 621–770 save and restore a
conversation. Two of its defects are Birko gaps:

- restored `Content` is a `JsonElement`, and `MessageText.From` (`Birko.AI.Contracts/Models/MessageText.cs`) has no
  `JsonElement` case, so `Message.GetText()` returns `""` for every restored turn
- trimming to the last 50 messages can drop a `tool_use` and keep its `tool_result`, which providers reject

Adopted in the consumer by DraCode TASK-133 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] Serialize / restore of a conversation in `Birko.AI.Contracts` that round-trips text, tool_use and tool_result blocks
- [ ] `MessageText.From` handles `JsonElement`, or restore never produces one
- [ ] A trim that never splits a tool pair and keeps the first user message
- [ ] Tests: round-trip each block type; trim at every boundary position
- [ ] [[TASK-527]]'s resume accepts a restored conversation

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
