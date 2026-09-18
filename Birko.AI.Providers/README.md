# Birko.AI.Providers

11 LLM provider implementations for cloud and local inference servers.

## Overview

Birko.AI.Providers contains concrete `ILlmProvider` implementations for all major LLM platforms, from cloud APIs (Anthropic, OpenAI, Google, Azure) to local servers (Ollama, LM Studio) and aggregators (OpenRouter, Groq).

## Providers

| Provider | Description |
|----------|-------------|
| `AnthropicProvider` | Anthropic Claude API (Messages API with streaming) |
| `OpenAiProvider` | OpenAI GPT models (Chat Completions API) |
| `AzureOpenAiProvider` | Azure-hosted OpenAI models |
| `GoogleProvider` | Google Gemini models (Generative Language API) |
| `MistralProvider` | Mistral AI models |
| `DeepSeekProvider` | DeepSeek models (OpenAI-compatible API) |
| `GroqProvider` | Groq inference (OpenAI-compatible API) |
| `OllamaProvider` | Ollama local inference server |
| `LmStudioProvider` | LM Studio local inference server |
| `OpenRouterProvider` | OpenRouter multi-provider gateway |
| `GitHubCopilotProvider` | GitHub Copilot API (OAuth device flow) |

## Dependencies

- **Birko.AI.Contracts** — `ILlmProvider`, models
- **Birko.AI** — `LlmProviderBase`
- **Birko.Communication.OAuth** — OAuth client for `GitHubCopilotProvider`

## Usage

```xml
<Import Project="..\Birko.AI.Providers\Birko.AI.Providers.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Providers;

var provider = new AnthropicProvider("api-key", "claude-sonnet-4-20250514");
```

## License

MIT License - see [License.md](License.md)
