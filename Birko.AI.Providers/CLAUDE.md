# Birko.AI.Providers

## Overview
11 LLM provider implementations for cloud and local inference servers.

## Project Location
`Birko.AI.Providers/`

## Namespace
`Birko.AI.Providers`

## Components

### AnthropicProvider.cs
- `AnthropicProvider` — Anthropic Claude API (Messages API with streaming)

### OpenAiProvider.cs
- `OpenAiProvider` — OpenAI GPT models (Chat Completions API)

### AzureOpenAiProvider.cs
- `AzureOpenAiProvider` — Azure-hosted OpenAI models

### GoogleProvider.cs
- `GoogleProvider` — Google Gemini models (Generative Language API)

### MistralProvider.cs
- `MistralProvider` — Mistral AI models

### DeepSeekProvider.cs
- `DeepSeekProvider` — DeepSeek models (OpenAI-compatible API)

### GroqProvider.cs
- `GroqProvider` — Groq inference (OpenAI-compatible API)

### OllamaProvider.cs
- `OllamaProvider` — Ollama local inference server

### LmStudioProvider.cs
- `LmStudioProvider` — LM Studio local inference server

### OpenRouterProvider.cs
- `OpenRouterProvider` — OpenRouter multi-provider gateway

### GitHubCopilotProvider.cs
- `GitHubCopilotProvider` — GitHub Copilot API (OAuth device flow)

## Turn order (TASK-546)

The agent's conversation can hold a text user turn right after a tool-result user turn (a hook's
`AppendUserMessage`). A provider whose API cannot take that fixes **its own request**, never the conversation:

| Provider | Contract | Request |
|---|---|---|
| Gemini | `contents` roles alternate user / model | consecutive same-role messages merged into one content, parts in order |
| Mistral | no `user` right after `tool` ("Unexpected role 'user' after role 'tool'") | placeholder `{ role: "assistant", content: " " }` between them (`BuildMessages` override; empty is refused) |
| Claude | consecutive turns are combined by the API | unchanged |
| OpenAI-style (OpenAI, Azure, Copilot, ZAi, DeepSeek, Groq, OpenRouter) | `user` after `tool` is valid | unchanged |
| Local servers (vLLM, llama.cpp, LM Studio, SGLang, Ollama) | the **model's chat template** decides | unchanged; a strict-alternation template can still refuse |

`OpenAiCompatibleProviderBase.BuildMessages` is the hook for a stricter OpenAI-style provider.

## Dependencies
- **Birko.AI.Contracts** — `ILlmProvider`, models
- **Birko.AI** — `LlmProviderBase`
- **Birko.Communication.OAuth** — OAuth client for `GitHubCopilotProvider`

## Consumers
- **Birko.AI.Agents** — `AgentFactory` creates provider instances
- Consumer applications selecting providers at runtime
