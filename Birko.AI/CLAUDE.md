# Birko.AI

## Overview
Core AI agent framework with LLM provider base class, agent run loop, factories, and default tools.

## Project Location
`C:\Source\Birko.AI\`

## Namespace
`Birko.AI.Providers`, `Birko.AI.Agents`, `Birko.AI.Factories`, `Birko.AI.Tools`

## Components

### Providers/LlmProviderBase.cs
- `LlmProviderBase` — Base class for LLM providers with retry logic, SSE parsing, and OpenAI-compatible helpers

### Agents/Agent.cs
- `Agent` — Core agent run loop with streaming support and tool execution

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
- `RunCommandTool` — Execute shell commands

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
