# Birko.AI.Contracts

## Overview
Zero-dependency shared project with LLM provider interface, message models, tool base class, and agent options.

## Project Location
`C:\Source\Birko.AI.Contracts\`

## Namespace
`Birko.AI`, `Birko.AI.Models`, `Birko.AI.Providers`, `Birko.AI.Tools`

## Components

### Models/Message.cs
- `Message` — Chat message model (role, content). `Content` is `object?` — a `string` for user turns, a `List<ContentBlock>` for assistant turns. Use `message.GetText()` to read the text regardless of shape; do NOT cast `Content` to `string` (an assistant turn's block list would stringify to a CLR type name).

### Models/MessageText.cs
- `MessageText.From(object? content)` — canonical text extractor backing `Message.GetText()`: returns a string directly, concatenates the `Text` of `type == "text"` blocks for a block list (or single block), and yields `string.Empty` for null/unrecognized content.

### Models/ContentBlock.cs
- `ContentBlock` — Typed content block (text, tool use, tool result)

### Models/TokenUsage.cs
- `TokenUsage` — Input/output token counts

### Models/LlmResponse.cs
- `LlmResponse` — Complete LLM response with message, usage, stop reason

### Models/LlmStreamingResponse.cs
- `LlmStreamingResponse` — Streaming response chunk

### Providers/ILlmProvider.cs
- `ILlmProvider` — Interface for LLM provider implementations

### Tools/Tool.cs
- `Tool` — Base class for agent tools (name, description, input schema, execution)

### AgentOptions.cs
- `AgentOptions` — Configuration options for agent behavior

### LlmProviderFactory.cs
- `LlmProviderFactory` — Registration-based factory for creating LLM provider instances. Consumers register provider factory delegates (e.g., `LlmProviderFactory.Register("claude", settings => new ClaudeProvider(settings))`) to avoid transitive dependencies

## Dependencies
None. This is a zero-dependency project.

## Consumers
- **Birko.AI** — core agent framework
- **Birko.AI.Providers** — provider implementations
- **Birko.AI.Agents** — specialized agents
- **Birko.AI.Resilience** — resilience wrappers
