# AI / LLM Agent Framework

Reusable infrastructure for building AI-powered applications with multi-provider LLM support, agent orchestration, and production resilience.

## Architecture

```
Birko.AI.Contracts (zero deps)
  ├── LlmProviderFactory (registration-based, Birko.AI.Factories namespace)
  └── ILlmProvider, Message, AgentOptions, Tool
  ↓
Birko.AI (base classes, tools)
  ├── LlmProviderBase
  ├── Agent (run loop)
  ├── AgentFactory (registration-based, Birko.AI.Factories namespace)
  └── Default Tools
  ↓
├── Birko.AI.Providers (11 LLM providers + ProviderRegistration)
└── Birko.AI.Agents (22 agents + AgentRegistration)
  ↓
Birko.AI.Orchestration (task dispatch, plans, dependency analysis)
  ↓
Birko.AI.Resilience (rate limiting, circuit breaker, cost tracking)
```

**Dependency Flow:**
- `Birko.AI.Contracts` — `LlmProviderFactory` lives here (only uses `ILlmProvider` interface)
- `Birko.AI` — `AgentFactory` lives here (only uses `Agent` base class and `ILlmProvider` — no concrete agent references)
- `Birko.AI.Providers` — Concrete providers + `ProviderRegistration` (registers all providers with `LlmProviderFactory`)
- `Birko.AI.Agents` — Concrete agents + `AgentRegistration` (registers all agents with `AgentFactory`, convenience `Create` method)
- Both factories use registration pattern to avoid transitive dependencies

## Projects

### Birko.AI.Contracts

Core interfaces, models, and provider factory with zero dependencies:

- **ILlmProvider** — Interface for all LLM providers (`SendMessageAsync`, `SendMessageStreamingAsync`)
- **LlmProviderFactory** — Registration-based factory for creating provider instances: `Register()`, `Create()`, `IsRegistered()`, `GetRegisteredProviders()`
- **Message** — Conversation message with role and content
- **ContentBlock** — Content types: text, tool_use (with Id, Name, Input)
- **TokenUsage** — PromptTokens, CompletionTokens, TotalTokens
- **LlmResponse** — StopReason, Content blocks, Usage, ErrorMessage
- **LlmStreamingResponse** — Async stream with FinalResponse, AccumulatedText
- **Tool** — Abstract base: Name, Description, InputSchema, Execute/ExecuteAsync
- **AgentOptions** — MaxIterations, ModelDepth, WorkingDirectory, EnableStreaming, etc.

### Birko.AI

Base classes, agent factory, and shared utilities:

- **LlmProviderBase** — Retry logic (uses `Birko.RetryPolicy`), SSE parsing, OpenAI-compatible message/tool builders, streaming helpers
- **Agent** — Run loop with streaming/sync modes, tool execution, multi-turn conversations, depth guidance
- **AgentFactory** — Registration-based factory: `Register()`, `RegisterAlias()`, `Create(provider, agentType)`, `IsRegistered()`, `GetRegisteredAgentTypes()`
- **Default Tools** — ListFiles, ReadFile, WriteFile, EditFile, AppendToFile, SearchCode, RunCommand, DisplayText, AskUser (all use `Birko.Helpers.PathHelper` for path safety)

### Birko.AI.Providers

11 LLM provider implementations + registration:

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

- **ProviderRegistration** — `RegisterAll()` registers all 11 providers (+ zhipu/zhipuai aliases) with `LlmProviderFactory`. Safe to call multiple times.

### Birko.AI.Agents

22 specialized agent implementations + registration:

- **CodingAgent** — General coding assistant base
- **Language agents** — CSharp, Python, JS/TS, Cpp, React, Angular, CSS, HTML, PHP, Assembler
- **Task agents** — DebugAgent, RefactorAgent, TestAgent, DocumentationAgent
- **Media agents** — DiagrammingAgent, MediaAgent, ImageAgent, SvgAgent, BitmapAgent
- **OrchestratorAgent** — Base for multi-agent coordination with JSON extraction helpers

- **AgentRegistration** — `RegisterAll()` registers all agents + aliases with `AgentFactory`. Also provides convenience `Create(provider, options, config, agentType)` that uses both `LlmProviderFactory` and `AgentFactory`. Includes `IsCodingAgent()` helper for Z.AI coding endpoint optimization.

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

### Provider and Agent Registration

```csharp
using Birko.AI.Agents;
using Birko.AI.Factories;
using Birko.AI.Providers;

// Option 1: Use built-in registrations (registers all providers and agents)
ProviderRegistration.RegisterAll();
AgentRegistration.RegisterAll();

// Option 2: Register only specific providers/agents
LlmProviderFactory.Register("claude", config =>
{
    var apiKey = config?["apiKey"] ?? "";
    var model = config?.GetValueOrDefault("model", "claude-3-5-sonnet-latest") ?? "claude-3-5-sonnet-latest";
    return new ClaudeProvider(apiKey, model);
});

AgentFactory.Register("csharp", (llm, opts) => new CSharpCodingAgent(llm, opts));
AgentFactory.RegisterAlias("cs", "csharp");
```

### Creating Agents

```csharp
// Via factories (low-level control)
var provider = LlmProviderFactory.Create("claude", new() { ["apiKey"] = "sk-..." });
var agent = AgentFactory.Create(provider, agentType: "csharp");
var result = await agent.RunAsync("Add unit tests for UserService");

// Via AgentRegistration convenience method (handles both factory calls)
var agent = AgentRegistration.Create("claude",
    config: new() { ["apiKey"] = "sk-..." },
    agentType: "csharp");

// With resilience decorators
var rateLimiter = new ProviderRateLimiter(rateLimitConfig);
var costTracker = new CostTrackingService(costConfig, logger);
var tracked = new TrackedLlmProvider(provider, rateLimiter, costTracker);
var agent = AgentFactory.Create(tracked, agentType: "csharp");

// GitHub Copilot with OAuth
var oauth = GitHubOAuthProvider.CreateDeviceFlowClient("your-client-id");
var copilot = new GitHubCopilotProvider(oauth);
var agent = AgentFactory.Create(copilot, agentType: "coding");

// Parallel step execution
var analyzer = new StepDependencyAnalyzer();
var groups = analyzer.AnalyzeParallelGroups(plan);
foreach (var group in groups)
{
    await Task.WhenAll(group.Select(step => ExecuteStepAsync(step)));
}
```

### Why This Pattern?

- **No transitive dependencies** — `AgentFactory` (in Birko.AI) has no references to concrete agents or providers. `LlmProviderFactory` (in Birko.AI.Contracts) has no references to concrete providers.
- **Consumer controls references** — Add only the provider/agent packages you use; register only what you need
- **Registration at edges** — `ProviderRegistration` and `AgentRegistration` are the only places that know about concrete types
- **Custom extensibility** — Register your own agents/providers alongside built-in ones using the same `Register()` API
- **Namespace consistency** — Both factories live in `Birko.AI.Factories` namespace regardless of which project defines them
