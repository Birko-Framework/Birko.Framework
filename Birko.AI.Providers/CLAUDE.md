# Birko.AI.Providers

## Overview
11 LLM provider implementations for cloud and local inference servers.

## Project Location
`C:\Source\Birko.AI.Providers\`

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

## Dependencies
- **Birko.AI.Contracts** — `ILlmProvider`, models
- **Birko.AI** — `LlmProviderBase`
- **Birko.Communication.OAuth** — OAuth client for `GitHubCopilotProvider`

## Consumers
- **Birko.AI.Agents** — `AgentFactory` creates provider instances
- Consumer applications selecting providers at runtime
