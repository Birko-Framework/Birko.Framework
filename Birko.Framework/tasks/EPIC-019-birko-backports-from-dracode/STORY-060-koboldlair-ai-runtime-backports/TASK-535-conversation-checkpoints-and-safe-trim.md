---
id: TASK-535
parent: STORY-060
feature: null
status: done
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

- [x] Serialize / restore of a conversation in `Birko.AI.Contracts` that round-trips text, tool_use and tool_result blocks
- [x] `MessageText.From` handles `JsonElement`, or restore never produces one
- [x] A trim that never splits a tool pair and keeps the first user message
- [x] Tests: round-trip each block type; trim at every boundary position
- [x] [[TASK-527]]'s resume accepts a restored conversation

## Human test plan

N/A — library behaviour, covered by unit tests.

## Resolution (2026-10-18)

**Measured first:** a `Message` read back by a plain `JsonSerializer` holds its content as a `JsonElement`, and
`MessageText.From` had no case for one, so `GetText()` returned "" for every such turn. DraCode's restore produced
exactly that.

**`ConversationCheckpoint`** (`Birko.AI.Contracts`):
- `Serialize` writes a versioned JSON; `Deserialize` restores the shapes the agent builds in memory. A string stays a
  string; text and tool_use blocks become `List<ContentBlock>` (tool input values as `JsonElement`); tool_result blocks
  become the agent's anonymous `{ type, tool_use_id, content }` objects, the same type the agent creates.
- `Agent.ResumeAsync` takes the restored conversation, and each provider sends it as it would a live one. This is
  pinned end to end on an OpenAI-compatible provider (`tool_calls[t1]`, then a `tool` message) and on Claude
  (`tool_use` and `tool_result` blocks).
- `ContentFromJson` is public, and `MessageText.From` now reads a `JsonElement` content through it.
- A checkpoint of another version is refused.

**`ConversationTrimmer.Trim(messages, max)`:** keeps the first user message and a tail that starts at an assistant turn,
so a tool_result never loses its tool_use and two user turns never meet. The limit must be at least 2.

**Tests:**
- `ConversationCheckpointTests` (29): round trip of every block type, the restored tool_result type, restored text,
  refused versions, JsonElement text, the trim at every size 2–23 over a 22-message conversation, fits, limits.
- `RestoredConversationResumeTests` (2).
- Mutations, each caught: no JsonElement case (1 red, and red on HEAD), no advance to an assistant turn (10 red).
- Regression: all 7 importers of `Birko.AI.Contracts` (Agents 58, Orchestration 42, Providers 220, Resilience 13,
  Resilience.Data 24, AI 57, VersionControl.Git 29).
