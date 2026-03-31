# Birko.AI.Agents

## Overview
22 specialized AI agents and factory for creating provider+agent combinations.

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

### AgentFactory.cs
- `AgentFactory` — Factory for creating provider+agent combinations by name

## Dependencies
- **Birko.AI.Contracts** — interfaces and models
- **Birko.AI** — `Agent` base class, tools
- **Birko.AI.Providers** — provider implementations

## Consumers
- Consumer applications
- **DraCode.KoboldLair** — AI-powered development environment
