# AI / LLM Agent Framework

Reusable infrastructure for building AI-powered applications with multi-provider LLM support, agent orchestration, and production resilience.

## Architecture

```
Birko.AI.Contracts (zero deps)
  -> Birko.AI (provider base, agent base, tools)
    -> Birko.AI.Providers (11 LLM providers)
    -> Birko.AI.Agents (coding, media, task agents, factory)
    -> Birko.AI.Orchestration (task dispatch, plans, dependency analysis)
  -> Birko.AI.Resilience (rate limiting, circuit breaker, cost tracking)
```

## Projects

### Birko.AI.Contracts

Core interfaces and models with zero dependencies:

- **ILlmProvider** — Interface for all LLM providers (`SendMessageAsync`, `SendMessageStreamingAsync`)
- **Message** — Conversation message with role and content
- **ContentBlock** — Content types: text, tool_use (with Id, Name, Input)
- **TokenUsage** — PromptTokens, CompletionTokens, TotalTokens
- **LlmResponse** — StopReason, Content blocks, Usage, ErrorMessage
- **LlmStreamingResponse** — Async stream with FinalResponse, AccumulatedText
- **Tool** — Abstract base: Name, Description, InputSchema, Execute/ExecuteAsync
- **AgentOptions** — MaxIterations, ModelDepth, WorkingDirectory, EnableStreaming, etc.

### Birko.AI

Provider base class and agent framework:

- **LlmProviderBase** — Retry logic (uses `Birko.RetryPolicy`), SSE parsing, OpenAI-compatible message/tool builders, streaming helpers
- **Agent** — Run loop with streaming/sync modes, tool execution, multi-turn conversations, depth guidance
- **Default Tools** — ListFiles, ReadFile, WriteFile, EditFile, AppendToFile, SearchCode, RunCommand, DisplayText, AskUser (all use `Birko.Helpers.PathHelper` for path safety)

### Birko.AI.Providers

11 LLM provider implementations:

| Provider | API | Features |
|----------|-----|----------|
| ClaudeProvider | Anthropic Messages API | Streaming with tool capture, input_tokens/output_tokens |
| OpenAiProvider | OpenAI Chat Completions | Streaming, function calling, 16K output |
| AzureOpenAiProvider | Azure deployments | Same as OpenAI with Azure auth |
| GeminiProvider | Google Generative AI | functionCall/functionResponse format, SSE streaming |
| OllamaProvider | Ollama /api/chat | NDJSON streaming, local models |
| OpenAiCompatibleProviderBase | Any OpenAI-compatible server | Base for local inference servers |
| LlamaCppProvider | llama.cpp server | Extends OpenAiCompatible, unlimited tokens |
| VllmProvider | vLLM server | Extends OpenAiCompatible |
| SglangProvider | SGLang server | Extends OpenAiCompatible |
| GitHubCopilotProvider | GitHub Copilot API | OAuth via IOAuthClient, auto-refresh |
| ZAiProvider | Zhipu AI GLM models | Deep thinking, coding endpoint, model validation |

### Birko.AI.Agents

22 specialized agents + factory:

- **CodingAgent** — General coding assistant base
- **Language agents** — CSharp, Python, JS/TS, Cpp, React, Angular, CSS, HTML, PHP, Assembler
- **Task agents** — DebugAgent, RefactorAgent, TestAgent, DocumentationAgent
- **Media agents** — DiagrammingAgent, MediaAgent, ImageAgent, SvgAgent, BitmapAgent
- **OrchestratorAgent** — Base for multi-agent coordination with JSON extraction helpers
- **AgentFactory** — Static factory: `CreateLlmProvider()`, `Create()` with provider/agent type routing

### Birko.AI.Resilience

Production-grade resilience for LLM API calls:

- **ProviderRateLimiter** — Sliding window counters (per-minute, per-day), thread-safe with ConcurrentDictionary
- **ProviderCircuitBreaker** — Closed/Open/HalfOpen states, configurable threshold, optional persistence via `ICircuitBreakerStore`
- **CostTrackingService** — Token cost calculation, daily/monthly/project budget enforcement, usage persistence via `IUsageRepository`
- **TrackedLlmProvider** — Decorator wrapping any ILlmProvider with rate limiting + cost tracking

### Birko.AI.Orchestration

Multi-agent task orchestration abstractions:

- **ITaskDispatcher** / **DirectTaskDispatcher** — Dispatch task assignments (in-process or distributed)
- **AgentTaskRecord** — Task with status, dependencies, retry tracking, error categorization
- **ImplementationPlan** / **ImplementationStep** — Step-based plans with file-level dependencies
- **StepDependencyAnalyzer** — Groups steps for parallel execution, topological sort, deadlock detection
- **EscalationAlert** — Structured escalations (TaskInfeasible, MissingDependency, NeedsSplit, WrongApproach, WrongAgentType)

### Birko.Communication.OAuth.Providers

Pre-configured OAuth factories for specific services:

- **GitHubOAuthProvider** — `CreateDeviceFlowClient(clientId)` returns IOAuthClient configured for GitHub device flow

## Usage

```csharp
// Basic agent usage
var provider = AgentFactory.CreateLlmProvider("claude", new() { ["apiKey"] = "sk-..." });
var agent = AgentFactory.Create(provider, agentType: "csharp");
var result = await agent.RunAsync("Add unit tests for UserService");

// With resilience
var rateLimiter = new ProviderRateLimiter(rateLimitConfig);
var costTracker = new CostTrackingService(costConfig, logger);
var tracked = new TrackedLlmProvider(provider, rateLimiter, costTracker);
var agent = AgentFactory.Create(tracked, agentType: "csharp");

// GitHub Copilot with OAuth
var oauth = GitHubOAuthProvider.CreateDeviceFlowClient("your-client-id");
var copilot = new GitHubCopilotProvider(oauth);

// Parallel step execution
var analyzer = new StepDependencyAnalyzer();
var groups = analyzer.AnalyzeParallelGroups(plan);
foreach (var group in groups)
{
    await Task.WhenAll(group.Select(step => ExecuteStepAsync(step)));
}
```
