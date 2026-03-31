# Birko.AI.Contracts

## Overview
Zero-dependency shared project with LLM provider interface, message models, tool base class, and agent options.

## Project Location
`C:\Source\Birko.AI.Contracts\`

## Namespace
`Birko.AI`, `Birko.AI.Models`, `Birko.AI.Providers`, `Birko.AI.Tools`

## Components

### Models/Message.cs
- `Message` — Chat message model (role, content blocks)

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

## Dependencies
None. This is a zero-dependency project.

## Consumers
- **Birko.AI** — core agent framework
- **Birko.AI.Providers** — provider implementations
- **Birko.AI.Agents** — specialized agents
- **Birko.AI.Resilience** — resilience wrappers
