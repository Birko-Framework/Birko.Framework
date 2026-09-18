# Birko.AI.Agents

22 specialized AI agents and factory for creating provider+agent combinations.

## Overview

Birko.AI.Agents provides a library of purpose-built agents for coding tasks (with language-specific specializations), media processing, and multi-agent orchestration. The `AgentFactory` creates ready-to-use provider+agent pairs by name.

## Agents

| Agent | Namespace | Description |
|-------|-----------|-------------|
| `CodingAgent` | `Birko.AI.Agents` | General-purpose coding agent |
| `CSharpAgent` | `Coding.Specialized` | C# / .NET specialist |
| `TypeScriptAgent` | `Coding.Specialized` | TypeScript / JavaScript specialist |
| `PythonAgent` | `Coding.Specialized` | Python specialist |
| `RustAgent` | `Coding.Specialized` | Rust specialist |
| `GoAgent` | `Coding.Specialized` | Go specialist |
| `JavaAgent` | `Coding.Specialized` | Java / Kotlin specialist |
| `SwiftAgent` | `Coding.Specialized` | Swift / iOS specialist |
| `CppAgent` | `Coding.Specialized` | C++ specialist |
| `SqlAgent` | `Coding.Specialized` | SQL / database specialist |
| `BashAgent` | `Coding.Specialized` | Shell scripting specialist |
| `RefactorAgent` | `Coding` | Code refactoring |
| `ReviewAgent` | `Coding` | Code review |
| `TestAgent` | `Coding` | Test generation |
| `DocumentationAgent` | `Coding` | Documentation writing |
| `ImageAgent` | `Media` | Image analysis and generation |
| `AudioAgent` | `Media` | Audio processing |
| `VideoAgent` | `Media` | Video processing |
| `DataVisualizationAgent` | `Media` | Chart and data visualization |
| `OrchestratorAgent` | `Birko.AI.Agents` | Multi-agent task coordination |
| `AgentFactory` | `Birko.AI.Agents` | Factory for provider+agent creation |

## Dependencies

- **Birko.AI.Contracts** — interfaces and models
- **Birko.AI** — `Agent` base class, tools
- **Birko.AI.Providers** — provider implementations

## Usage

```xml
<Import Project="..\Birko.AI.Agents\Birko.AI.Agents.projitems" Label="Shared" />
```

```csharp
using Birko.AI.Agents;

var agent = AgentFactory.Create("anthropic", "csharp", apiKey);
await agent.RunAsync("Add null checks to this class");
```

## License

MIT License - see [License.md](License.md)
