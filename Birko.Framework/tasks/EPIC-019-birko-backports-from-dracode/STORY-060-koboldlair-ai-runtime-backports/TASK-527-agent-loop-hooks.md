---
id: TASK-527
parent: STORY-060
feature: null
status: done
priority: P1
assignee: ai
created: 2026-10-08
depends-on: []
blocks: [TASK-544, TASK-546]
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

- [x] A per-iteration hook on `Agent` (`Birko.AI/Agents/Agent.cs`) that can append a message to the conversation and stop the loop
- [x] Tool-call started / finished hooks with structured data (tool name, input, result, error, duration), not a string callback
- [x] The system prompt is readable without reflection (public read-only, or handed to the hook)
- [x] A run can resume from a supplied conversation (pairs with [[TASK-535]])
- [x] `CheckpointInterval` is honoured by the base loop or removed — rule 51, no option that does nothing
- [x] Hooks fire on both the streaming and non-streaming paths; the token reaches provider and tools on both
- [x] Tests: hook ordering, early stop, an injected message reaches the provider, a throwing hook's behaviour is defined

## Out of scope

- Kobold's step detection itself — DraCode policy, rebuilt on the hooks
- Deferred to TASK-544 — the streaming fallback re-runs already-executed tools (and re-fires every hook) when a later iteration fails
- Deferred to TASK-546 — a hook-injected message after a tool-result turn makes two consecutive user turns; providers unchecked
- **Decided not to do here:** an `OnIterationCompletedAsync` hook after the final tool batch. Nothing in Birko needs it,
  and whether DraCode's auto-advance does is only known once its adoption (DraCode TASK-125) is rebuilt on these hooks —
  that adoption files it upstream if so.

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan

> **Settled 2026-10-09 (user):** "finished hooks with … error" reports **both** a tool exception and an
> `Error:` result (`IsError` + `Exception`); a tool exception **still propagates** — the loop does not start
> catching tool exceptions. `CheckpointInterval` is **removed** (decision below). The streaming-fallback
> re-run defect is TASK-544.

### What the fork needs (Kobold.cs)
- **Before each model call:** inject a reflection prompt every N step-iterations (1418), stop cleanly on cancel
  after its checkpoint (1386), read the system prompt (1443 — reflection today).
- **Per tool call:** structured start/result events (1462–1477; `RunEventPublishingTool` on the plain path,
  837–860) and spotting `update_plan_step` (1480).
- **Resume:** feed a restored conversation into a run (1344). Step counters, reflection text and checkpoint
  saving stay DraCode policy, rebuilt on the hooks (out of scope).

### API shape — new `Birko.AI/Agents/AgentHooks.cs` (namespace `Birko.AI.Agents`)
```csharp
public interface IAgentHooks {
    ValueTask OnIterationStartingAsync(AgentIterationContext context);
    ValueTask OnToolCallStartingAsync(ToolCallStarted call, CancellationToken cancellationToken);
    ValueTask OnToolCallFinishedAsync(ToolCallFinished call, CancellationToken cancellationToken);
}
public abstract class AgentHooks : IAgentHooks { /* virtual no-op defaults */ }

public sealed class AgentIterationContext {
    public int Iteration { get; }  public int MaxIterations { get; }  public bool IsStreaming { get; }
    public string SystemPrompt { get; }                  // exactly what goes to the provider this call
    public IReadOnlyList<Message> Conversation { get; }
    public CancellationToken CancellationToken { get; }
    public void AppendUserMessage(string content);       // non-blank; sent on THIS call
    public void Stop(string? reason = null);  public bool StopRequested { get; }  public string? StopReason { get; }
}
public sealed record ToolCallStarted(int Iteration, string ToolUseId, string ToolName,
    IReadOnlyDictionary<string, object> Input);
public sealed record ToolCallFinished(int Iteration, string ToolUseId, string ToolName,
    IReadOnlyDictionary<string, object> Input, string? Result, bool ToolFound, bool IsError,
    Exception? Exception, TimeSpan Duration);
public sealed class AgentHookException : Exception {   // InnerException = the hook's
    public string HookName { get; }  public int Iteration { get; }  public Exception? ToolException { get; } }
```
On `Agent`:
- `public IAgentHooks? Hooks { get; set; }` — attachable from outside (Kobold wraps, does not subclass), like
  `SetMessageCallback`. Not safe for concurrent runs on one agent; documented, same as `_tools`.
- `public string CurrentSystemPrompt => SystemPrompt;` — non-virtual. `SystemPrompt` itself stays `protected`:
  making it public breaks ~30 `protected override` declarations (CS0507).
- `public Task<List<Message>> ResumeAsync(IEnumerable<Message> conversation, int? maxIterations = null,
  CancellationToken cancellationToken = default)` — copies the list and appends **no** user turn (`ContinueAsync`
  always does; a restored conversation ending in `tool_result` must not get one). Guards: non-empty, last message
  not `assistant`. TASK-535's restore only has to produce `List<Message>`.

### Steps
1. **One loop, not two.** Fold `RunWithHistoryStreamingAsync` / `RunWithHistorySyncAsync` into one private loop
   with `GetResponseAsync(iteration, streaming, systemPrompt, ct)` (only iteration 1 streams, as now) — hooks
   added to two copies is how one path drifts. Per iteration: read `SystemPrompt` once → fire
   `OnIterationStartingAsync` **before** `ThrowIfCancellationRequested` (so a hook can stop cleanly on cancel) →
   append queued messages → return if `StopRequested` → cancellation check → provider call with the same prompt
   and token. `RunAsync` / `ContinueAsync` / `ResumeAsync` all funnel in.
2. **Tool hooks in `HandleToolUse`:** started → `Stopwatch.GetTimestamp` → tool with token → finished.
   `IsError` = unknown tool, `Error:` result, or exception. On a throwing tool, finished fires with `Exception`,
   then rethrow via `ExceptionDispatchInfo`. A stop requested mid-batch takes effect after all tool_results are
   appended (the fork's early return at 1527 leaves a dangling tool_use; this does not).
3. **One non-virtual `InvokeHookAsync(name, iteration, Func<ValueTask>)`** holds append/stop/wrapping (rule 12).
   `AgentHookException` is excluded from the streaming-fallback `catch` filter, so a hook failure is never retried.
4. **Remove `AgentOptions.CheckpointInterval`** with its Clone / Merge / FromDictionary / ToDictionary lines.
5. **Tests** (below); update `tests/Birko.AI.Tests/AgentOptionsTests.cs`.
6. **Docs:** CHANGELOG breaking entry; `Birko.AI/CLAUDE.md` + README hooks section; `/specs` regen of
   `docs/specs/llm-provider-and-agents.md` (its "CheckpointInterval has no effect" requirement goes; hooks and
   resume come in); DraCode adoption pointer in the close note.

### Decision — `CheckpointInterval` is removed, not honoured
Honouring it means the base loop injects a reflection prompt naming DraCode's `reflect` / `update_plan_step`
tools — DraCode policy that would, at default 3, silently change every Birko agent's conversation. A base
`IsCheckpointIteration` flag does not help: Kobold counts iterations **per step**, which resets, so it could not
use a run counter — the option would still do nothing for everyone (rule 51). Its only reader is
`Kobold.cs:1418`, which gets a compile error and moves the interval into its own hook. `FromDictionary` ignores
unknown keys, so a stored `checkpointInterval` is now dropped silently — the migration note says so.

### Decision — a throwing hook fails the run
`OperationCanceledException` propagates unchanged. Anything else → `AgentHookException` (inner, hook name,
iteration), ending the run: never swallowed (rule 51), never sent down the sync fallback (which would re-run
tools). Tool hooks fire before the batch's tool_result turn is appended, so an aborting hook leaves the same
state a throwing tool does today. A finished hook throwing while a tool exception is in flight:
`AgentHookException` wins and carries the tool's in `ToolException`.

### Token flow on both paths
The base already passes the token to provider and tools; the drift is in the fork. Tests pin it: a
`CapturingProvider` records the token on `SendMessageAsync` **and** `SendMessageStreamingAsync` (a real streaming
fake — `ScriptedProvider` throws `NotSupportedException` there) and a `CapturingTool` records its token; each a
Theory over `EnableStreaming` true/false, all equal to the run's `cts.Token` and to `context.CancellationToken`.

### Tests — `tests/Birko.AI.Agents.Tests/AgentHooksTests.cs` (Theory over streaming on/off)
1. Ordering: iteration(1) → provider → started → tool → finished → iteration(2) → provider → end.
2. Early stop at iteration 2: provider called once, no tool re-runs.
3. Stop on a cancelled token returns normally.
4. Injected message is the provider's `messages[^1]`; its `systemPrompt` == `context.SystemPrompt` == `CurrentSystemPrompt`.
5. Finished data: name, id, input, result, positive duration; unknown tool → `ToolFound=false`, `IsError=true`;
   throwing tool → `Exception` set, finished still fires, original exception propagates.
6. Throwing hooks: iteration hook → `AgentHookException`, provider not called, no fallback even with
   `StreamingFallbackToSync=true`; finished hook → `AgentHookException`; OCE from a hook → OCE.
7. Token on both paths (above).
8. Resume: a conversation ending in `tool_result` is sent unchanged and the caller's list is not mutated; empty /
   trailing assistant → `ArgumentException`.
9. No hooks: existing `AgentLoopTests` stay green.

### Risks
- **Consecutive user turns** → TASK-546. An injected message after a tool_result turn gives two user messages in a row (the
  fork already does it). Check each provider with a provider-level test, or merge in `AppendUserMessage`.
- **No hook after the final tool batch** on the last iteration — add `OnIterationCompletedAsync` if DraCode's
  auto-advance needs it.

### Split signals
- **The streaming fallback reruns tools** (`Agent.cs:174–181` wraps the whole streaming run, not just the
  streaming request; a failure in iteration ≥ 2 replays every iteration in sync mode) — a defect of its own,
  not this task's. → deferred to TASK-544.
- DraCode adoption (delete the fork, `RunEventPublishingTool`, the reflection read) is DraCode TASK-125.

## Close note (2026-10-09)

- **Shipped:** `Agent.Hooks` / `IAgentHooks` / `AgentHooks` (`Birko.AI/Agents/AgentHooks.cs`), `CurrentSystemPrompt`,
  `ResumeAsync`, one merged run loop; `AgentOptions.CheckpointInterval` removed (CHANGELOG `BREAKING` entry). 45 new
  test cases in `tests/Birko.AI.Agents.Tests/AgentHooksTests.cs`, each loop test a Theory over streaming on/off.
- **Proved:** dropping `AgentHookException` from the fallback filter turns 2 tests red; dropping the appended-message
  write turns the injected-message test red on both paths.
- **Gate:** standards pass (docs/ai.md added on its finding); intent pass (the streaming `OnLlmResponseReceived`
  timing shift it found was restored and is pinned by `OnLlmResponseReceived_FiresOncePerModelCall`); correctness:
  hook input is now a read-only view, live-`Conversation` / fallback re-fire / mid-batch-throw documented; comments
  trimmed. Security: not applicable — no auth, input-handling, secret or endpoint surface.
- **DraCode adoption (its TASK-125):** delete `RunWithStepDetectionAsync`, `RunEventPublishingTool` /
  `WrapToolsForRunEvents` and the reflection read of `SystemPrompt`; move the checkpoint interval into Kobold's own
  settings and inject the reflection prompt from `OnIterationStartingAsync`; resume via `ResumeAsync`.
- **Spec:** `docs/specs/llm-provider-and-agents.md` regenerated in the follow-up commit (its stamp must name a commit
  carrying this code).
