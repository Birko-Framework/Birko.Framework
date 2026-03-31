# Birko.AI

Core AI agent framework with LLM provider base class, agent run loop, and default tools.

## Overview

Birko.AI provides the foundational infrastructure for building AI agents — a base class for LLM providers with retry and SSE support, a core agent run loop with streaming and tool execution, and 9 built-in tools for file manipulation, code search, and user interaction.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `LlmProviderBase` | `Birko.AI.Providers` | Base class with retry logic, SSE parsing, OpenAI helpers |
| `Agent` | `Birko.AI.Agents` | Core run loop with streaming and tool execution |
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

## License

MIT License - see [License.md](License.md)
