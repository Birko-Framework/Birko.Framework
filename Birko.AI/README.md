# Birko.AI

Core AI agent framework with LLM provider base class, agent run loop, and default tools.

## Overview

Birko.AI provides the foundational infrastructure for building AI agents — a base class for LLM providers with retry and SSE support, a core agent run loop with streaming and tool execution, and 9 built-in tools for file manipulation, code search, and user interaction.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `LlmProviderBase` | `Birko.AI.Providers` | Base class with retry logic, SSE parsing, OpenAI helpers |
| `Agent` | `Birko.AI.Agents` | Core run loop with streaming and tool execution; `ResumeAsync`, `CurrentSystemPrompt` |
| `IAgentHooks` / `AgentHooks` | `Birko.AI.Agents` | Per-iteration and tool-call hooks, attached via `Agent.Hooks` |
| `ListFilesTool` | `Birko.AI.Tools` | List files in a directory |
| `ReadFileTool` | `Birko.AI.Tools` | Read file contents |
| `WriteFileTool` | `Birko.AI.Tools` | Write content to a file |
| `EditFileTool` | `Birko.AI.Tools` | Edit file with string replacement |
| `AppendToFileTool` | `Birko.AI.Tools` | Append content to a file |
| `SearchCodeTool` | `Birko.AI.Tools` | Search code with pattern matching |
| `RunCommandTool` | `Birko.AI.Tools` | Execute shell commands |
| `DisplayTextTool` | `Birko.AI.Tools` | Display text output to the user |
| `AskUserTool` | `Birko.AI.Tools` | Prompt user for input |

## Dependencies

- **Birko.AI.Contracts** — interfaces and models
- **Birko.Contracts** — `RetryPolicy`
- **Birko.Helpers** — `PathHelper`

## Usage

```xml
<Import Project="..\Birko.AI\Birko.AI.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Agents;

var agent = new Agent(provider, options);
await agent.RunAsync("Describe this codebase");
```

### Hooks

```csharp
sealed class ReflectEvery3 : AgentHooks
{
    public override ValueTask OnIterationStartingAsync(AgentIterationContext context)
    {
        if (context.CancellationToken.IsCancellationRequested)
            context.Stop("shutting down");          // a clean stop instead of an OperationCanceledException
        else if (context.Iteration % 3 == 0)
            context.AppendUserMessage("Pause and check your progress against the plan.");
        return ValueTask.CompletedTask;
    }

    public override ValueTask OnToolCallFinishedAsync(ToolCallFinished call, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{call.ToolName} {(call.IsError ? "failed" : "ok")} in {call.Duration.TotalMilliseconds:F0} ms");
        return ValueTask.CompletedTask;
    }
}

agent.Hooks = new ReflectEvery3();
var conversation = await agent.RunAsync("Fix the build");

// After a crash — continue a conversation checkpointed mid-run, exactly as it stands. No user turn is added,
// so it must end in a user or tool-result turn; one ending in an assistant turn is refused (use ContinueAsync).
await agent.ResumeAsync(checkpoint);
```

Hooks fire on the streaming and non-streaming path alike, once per iteration and tool call. A throwing hook ends
the run with `AgentHookException`. `StreamingFallbackToSync` retries only a failed **streaming request** (iteration 1's
model call) without streaming; a later iteration, tool or hook that fails propagates, so no tool runs twice. An agent instance is not safe for concurrent runs.

## License

MIT License - see [License.md](License.md)
