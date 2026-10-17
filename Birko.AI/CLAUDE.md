# Birko.AI

## Overview
Core AI agent framework with LLM provider base class, agent run loop, factories, and default tools.

## Project Location
`Birko.AI/`

## Namespace
`Birko.AI.Providers`, `Birko.AI.Agents`, `Birko.AI.Factories`, `Birko.AI.Tools`

## Components

### Providers/LlmProviderBase.cs
- `LlmProviderBase` — Base class for LLM providers with retry logic, SSE parsing, and OpenAI-compatible helpers
- Failure classification (TASK-531), the one producer: `ClassifyResponse(response, body)` (status, Retry-After, a 429
  `insufficient_quota` is Billing), `ClassifyException(ex)`, `IsCallerCancellation(ex, ct)`, `StreamingFailure(message,
  failure)`, and `ConnectStreamingAsync` (the streaming connect with its failure; `SendStreamingWithRetryAsync` wraps
  it). **A provider's catch-all is `catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))`**: the
  caller's cancellation propagates, it is not an error response.

### Agents/Agent.cs
- `Agent` — Core agent run loop with streaming support and tool execution. **One** private loop (`RunLoopAsync`)
  serves the streaming and non-streaming paths (only iteration 1 streams) — never reintroduce a second copy, it is
  how hooks or token flow drift on one path. `CurrentSystemPrompt` exposes the protected `SystemPrompt`;
  `ResumeAsync` continues a saved conversation without adding a user turn.

### Agents/AgentHooks.cs
- `IAgentHooks` / `AgentHooks` (no-op base) — attached via `Agent.Hooks`. `OnIterationStartingAsync(AgentIterationContext)`
  fires before each model call and **before** the cancellation check (so it can `Stop()` a cancelled run cleanly),
  and can `AppendUserMessage`. `OnToolCallStartingAsync` / `OnToolCallFinishedAsync` carry `ToolCallStarted` /
  `ToolCallFinished` (name, id, input, result, found, `IsError`, `Exception`, duration).
- A throwing hook ends the run as `AgentHookException` (OCE passes through) and is excluded from the streaming
  sync-fallback filter. A tool's own exception still propagates after the finished hook sees it (TASK-527).

### Factories/AgentFactory.cs
- `AgentFactory` — Factory for creating agent instances from providers (does NOT create providers)

**Note:** `LlmProviderFactory` is located in the `Birko.AI.Contracts` project (namespace `Birko.AI.Contracts`) since it only depends on contracts/interfaces.

### Tools/ListFilesTool.cs
- `ListFilesTool` — List files in a directory

### Tools/ReadFileTool.cs
- `ReadFileTool` — Read file contents

### Tools/WriteFileTool.cs
- `WriteFileTool` — Write content to a file

### Tools/EditFileTool.cs
- `EditFileTool` — Edit file with string replacement

### Tools/AppendToFileTool.cs
- `AppendToFileTool` — Append content to a file

### Tools/SearchCodeTool.cs
- `SearchCodeTool` — Search code with pattern matching

### Tools/RunCommandTool.cs
- `RunCommandTool` — Execute shell commands, on `Birko.Helpers.ProcessRunner` (TASK-534): timeout kills the tree, the caller's cancellation propagates

### Tools/DisplayTextTool.cs
- `DisplayTextTool` — Display text output to the user

### Tools/AskUserTool.cs
- `AskUserTool` — Prompt user for input

## Dependencies
- **Birko.AI.Contracts** — interfaces and models
- **Birko.Contracts** — `RetryPolicy`
- **Birko.Helpers** — `PathHelper`
- **Birko.AI.Agents** — concrete agent implementations (used by AgentFactory)

## Consumers
- **Birko.AI.Providers** — extends `LlmProviderBase`
- **Birko.AI.Agents** — extends `Agent`

## Factory Pattern

This project contains both `LlmProviderFactory` and `AgentFactory`:

- **LlmProviderFactory** — Registration-based, consumers register provider delegates (e.g., `LlmProviderFactory.Register("claude", settings => new ClaudeProvider(settings))`)
- **AgentFactory** — Direct instantiation, creates agents from providers (e.g., `AgentFactory.Create(provider, agentType: "csharp")`)

This design avoids transitive dependencies — Birko.AI doesn't reference Birko.AI.Providers or Birko.Communication.OAuth.
