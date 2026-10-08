---
id: TASK-527
parent: STORY-060
feature: null
status: todo
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Agent loop: per-iteration and tool-call hooks, a readable system prompt, resume from a conversation

## Context

`DraCode.KoboldLair/Models/Agents/Kobold.cs` (2,825 lines) forks `Birko.AI`'s agent loop. `RunWithStepDetectionAsync`
(lines 1300–1675) is a copy of `Agent.RunAsync`, made so it can inject a reflection reminder every N iterations and
stop early. Line 1443 reads the `protected` `SystemPrompt` **by reflection**. To observe tool calls it strips every
tool, wraps it in `RunEventPublishingTool` and puts it back (`WrapToolsForRunEvents`, lines 837–860). DraCode's
`_loose/TASK-106` close note already says a structured tool-call hook on `Agent` would make the wrapper unnecessary; it
was never filed here.

The fork has drifted: it does not pass the `CancellationToken` to `SendMessageAsync` or `tool.ExecuteAsync`
(line 1480), and it has no streaming path and none of `EnsureErrorContent`. `AgentOptions.CheckpointInterval` is read
only by the fork.

Adopted in the consumer by DraCode TASK-125 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] A per-iteration hook on `Agent` (`Birko.AI/Agents/Agent.cs`) that can append a message to the conversation and stop the loop
- [ ] Tool-call started / finished hooks with structured data (tool name, input, result, error, duration), not a string callback
- [ ] The system prompt is readable without reflection (public read-only, or handed to the hook)
- [ ] A run can resume from a supplied conversation (pairs with [[TASK-535]])
- [ ] `CheckpointInterval` is honoured by the base loop or removed — rule 51, no option that does nothing
- [ ] Hooks fire on both the streaming and non-streaming paths; the token reaches provider and tools on both
- [ ] Tests: hook ordering, early stop, an injected message reaches the provider, a throwing hook's behaviour is defined

## Out of scope

- Kobold's step detection itself — DraCode policy, rebuilt on the hooks

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
