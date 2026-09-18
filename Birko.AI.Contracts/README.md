# Birko.AI.Contracts

Zero-dependency contracts for the Birko AI framework — LLM provider interface, message models, tool base class, and agent options.

## Overview

Birko.AI.Contracts defines the shared interfaces and models used across all Birko.AI projects. It has no dependencies, making it the foundation layer for the AI stack.

## Components

| Type | Namespace | Description |
|------|-----------|-------------|
| `Message` | `Birko.AI.Models` | Chat message model (role, content blocks) |
| `ContentBlock` | `Birko.AI.Models` | Typed content block (text, tool use, tool result) |
| `TokenUsage` | `Birko.AI.Models` | Input/output token counts |
| `LlmResponse` | `Birko.AI.Models` | Complete LLM response with message, usage, stop reason |
| `LlmStreamingResponse` | `Birko.AI.Models` | Streaming response chunk |
| `ILlmProvider` | `Birko.AI.Providers` | Interface for LLM provider implementations |
| `Tool` | `Birko.AI.Tools` | Base class for agent tools |
| `AgentOptions` | `Birko.AI` | Configuration options for agent behavior |

## Dependencies

None. This is a zero-dependency project.

## Usage

```xml
<Import Project="..\Birko.AI.Contracts\Birko.AI.Contracts.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Providers;

public class MyProvider : ILlmProvider
{
    // Implement LLM provider interface
}
```

## License

MIT License - see [License.md](License.md)
