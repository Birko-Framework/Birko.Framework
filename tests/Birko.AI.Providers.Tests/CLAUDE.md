# Birko.AI.Providers.Tests

## Overview
Unit tests for Birko.AI.Providers — the concrete `ILlmProvider` implementations (Ollama, OpenAI,
Claude, etc.).

## Project Location
`C:\Source\Birko\Framework	ests\Birko.AI.Providers.Tests\`

## Test Framework
xUnit + FluentAssertions

## Scope & conventions
- **No live model server.** Providers are exercised by injecting a fake `HttpMessageHandler`
  (constructor test seam) that captures the outgoing request and returns a canned response. This
  closes the CR-H006 "no test project" gap for the provider library.
- `OllamaProviderTests` — regression for CR-C01: the streaming request must POST to `/api/chat`
  (not the server root); also asserts the non-streaming path hits the same endpoint.
