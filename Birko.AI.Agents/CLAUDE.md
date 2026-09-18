# Birko.AI.Agents

## Overview
22 specialized AI agent implementations (no factory - factory has been moved to `Birko.AI`).

## Project Location
`C:\Source\Birko.AI.Agents\`

## Namespace
`Birko.AI.Agents`, `Birko.AI.Agents.Coding`, `Birko.AI.Agents.Coding.Specialized`, `Birko.AI.Agents.Media`

## Components

### CodingAgent.cs
- `CodingAgent` — General-purpose coding agent

### Coding/Specialized/ (Language Agents)
- `CSharpAgent` — C# / .NET specialized agent
- `TypeScriptAgent` — TypeScript / JavaScript agent
- `PythonAgent` — Python agent
- `RustAgent` — Rust agent
- `GoAgent` — Go agent
- `JavaAgent` — Java / Kotlin agent
- `SwiftAgent` — Swift / iOS agent
- `CppAgent` — C++ agent
- `SqlAgent` — SQL / database agent
- `BashAgent` — Shell scripting agent

### Coding/ (Task Agents)
- `RefactorAgent` — Code refactoring agent
- `ReviewAgent` — Code review agent
- `TestAgent` — Test generation agent
- `DocumentationAgent` — Documentation writing agent

### Media/
- `ImageAgent` — Image analysis and generation agent
- `AudioAgent` — Audio processing agent
- `VideoAgent` — Video processing agent
- `DataVisualizationAgent` — Chart and data visualization agent

### OrchestratorAgent.cs
- `OrchestratorAgent` — Multi-agent task coordination

### Agents/AgentFactory.cs (DEPRECATED)
- **Note:** The old `AgentFactory` with `CreateLlmProvider()` method still exists here for backward compatibility, but new code should use:
  - `Birko.AI.Contracts.LlmProviderFactory` for creating providers (registration-based)
  - `Birko.AI.Factories.AgentFactory` for creating agents

## Dependencies
- **Birko.AI.Contracts** — interfaces and models
- **Birko.AI** — `Agent` base class, tools

## Consumers
- Consumer applications
- **DraCode.KoboldLair** — AI-powered development environment
