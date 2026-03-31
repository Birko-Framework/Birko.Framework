# Birko.AI

## Overview
Core AI agent framework with LLM provider base class, agent run loop, and default tools.

## Project Location
`C:\Source\Birko.AI\`

## Namespace
`Birko.AI.Providers`, `Birko.AI.Agents`, `Birko.AI.Tools`

## Components

### Providers/LlmProviderBase.cs
- `LlmProviderBase` — Base class for LLM providers with retry logic, SSE parsing, and OpenAI-compatible helpers

### Agents/Agent.cs
- `Agent` — Core agent run loop with streaming support and tool execution

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

## Consumers
- **Birko.AI.Providers** — extends `LlmProviderBase`
- **Birko.AI.Agents** — extends `Agent`
