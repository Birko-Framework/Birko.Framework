---
name: design-agent
description: Interview the user about an agent they want built, then synthesize an optimal agent definition — either a `Birko.AI.Agents` C# `Agent` subclass (with `SystemPrompt` override, depth guidance, file-op guidelines) or a Claude Code subagent markdown file. Use when the user says "design an agent", "create an agent prompt", "novy AI agent", "navrhni agenta", "optimalny agent prompt", "scaffold an agent for X", or asks how to structure a system prompt for a new specialized agent. Applies the patterns and principles from Anthropic's "Building Effective Agents" (simplicity first, transparency, well-documented tools; pattern ladder from single-call → prompt-chain → routing → parallelization → orchestrator-workers → evaluator-optimizer → autonomous loop) and mirrors the conventions used by the existing `Birko.AI.Agents` catalogue (CodingAgent, RefactorAgent, CSharpCodingAgent, MediaAgent, OrchestratorAgent, etc.).
---

# Design an Agent

Produces an *optimal* agent definition by interviewing the user first — never invents the agent's purpose. Anchored on two sources of truth:

- **Anthropic — Building Effective Agents**: start simple, add complexity only when a simpler shape demonstrably fails; show planning steps; invest in tool documentation; keep formats close to natural patterns.
- **`Birko.AI.Agents` catalogue**: every concrete agent overrides `protected override string SystemPrompt` on a base like `CodingAgent` / `MediaAgent` / `OrchestratorAgent`, interpolates `{WorkingDirectory}`, and calls `GetDepthGuidance()`, `GetFileOperationGuidelines()`, `GetCommonBestPractices()` so behavior scales with `Options.ModelDepth` (1–10).

## Workflow

1. **Interview** — run AskUserQuestion rounds (max 4 questions per call). Do not skip; do not guess.
2. **Pick the pattern** — match the task shape to the smallest Anthropic pattern that works (see ladder below).
3. **Draft** — fill the matching template (C# class OR subagent markdown).
4. **Review with user** — show the draft, ask for corrections, iterate.
5. **Write the file** at the agreed path. For C#, also remind the user to register it in `AgentRegistration.cs`.

## Interview rounds

**Round 1 — target & shape** (one AskUserQuestion call, single-select each):
- *Target*: `Birko.AI` C# agent class · Claude Code subagent (`.claude/agents/*.md`) · raw system prompt only
- *Deliverable*: what one artifact does this agent produce? (open answer — code, refactored file, SVG, plan JSON, review comments, …)
- *Base class* (only if C#): `CodingAgent` · `MediaAgent` · `OrchestratorAgent` · `DiagrammingAgent` · standalone `Agent`

**Round 2 — capability**:
- *Inputs*: files? prior agent output? user prompt only?
- *Tools needed*: file r/w/edit · shell · code search · web fetch · custom (describe)
- *Specialization*: language / domain / format the agent is expert in (used to build the "You are an expert in:" bullet list)

**Round 3 — only if non-obvious**:
- *Hard constraints*: what must the agent NEVER do?
- *Stop condition*: when is the task "done"?

For C# target, also ask: directory (default `C:\Source\Birko\Framework\Birko.AI.Agents\Agents\<Category>\`), namespace (default `Birko.AI.Agents.<Category>`), class name (must end with `Agent`).

## Pattern ladder — pick the smallest that fits

| Task shape | Anthropic pattern | Birko.AI manifestation |
|---|---|---|
| One transformation, clear input/output | **Single LLM + tools** | Plain `Agent` subclass — *the default, use unless something is clearly missing* |
| Linear pipeline, each step depends on prior | **Prompt chain** | Sequence of `agent.RunAsync` calls in a coordinator |
| Distinct branches handled differently | **Routing** | Coordinator picks the specialized agent (`CSharpCodingAgent` vs `PythonCodingAgent`) |
| Independent subtasks, faster in parallel | **Parallelization (sectioning)** | `Task.WhenAll` over multiple `RunAsync` calls |
| Unpredictable decomposition | **Orchestrator-workers** | Subclass `OrchestratorAgent`, delegate to workers via tools |
| Output needs measurable refinement | **Evaluator-optimizer** | Two-agent loop: generator + reviewer, stop on score |
| Open-ended, step count unknown | **Autonomous loop** | Existing `Agent.RunAsync` with `maxIterations` — already this shape |

If the user can't articulate why a fancier pattern is needed, ship the single-LLM version first.

## C# template (`Birko.AI.Agents` style)

```csharp
using Birko.AI.Providers;

namespace Birko.AI.Agents.<Category>
{
    public class <Name>Agent : <BaseClass>      // CodingAgent | MediaAgent | OrchestratorAgent | Agent
    {
        public <Name>Agent(ILlmProvider llmProvider, AgentOptions? options = null)
            : base(llmProvider, options) { }

        protected override string SystemPrompt
        {
            get
            {
                return $@"You are a <one-line role> working in a sandboxed workspace at {WorkingDirectory}.

You are an expert in:
- <specific skill 1 — be concrete, not 'good at code'>
- <specific skill 2>
- <…3–8 bullets total>

When given a task:
1. <first concrete action — usually 'read/explore'>
2. <second action>
3. <…>
N. Continue iterating until <explicit stop condition>.

{GetDepthGuidance()}

Important guidelines:
{GetFileOperationGuidelines()}        // omit if no file ops
- <domain-specific DO 1>
- <domain-specific DO 2>
{GetCommonBestPractices()}

<closing instruction — what to say when done>";
            }
        }
    }
}
```

Then register in `C:\Source\Birko\Framework\Birko.AI.Agents\Agents\AgentRegistration.cs` so `AgentFactory` can resolve it by key.

## Subagent template (Claude Code `.claude/agents/*.md`)

```md
---
name: <kebab-case-name>
description: <one sentence: what it does>. Use when <specific triggers/keywords>.
tools: <optional whitelist, e.g. Read, Edit, Grep — omit to inherit all>
---

You are <one-line role>.

You are an expert in:
- <…>

When invoked:
1. <first action>
2. <…>
N. Stop and report when <stop condition>.

Guidelines:
- <DO 1>
- <DO 2>
```

## Review checklist (run before showing the user)

- [ ] Pattern picked is the *smallest* that fits — no orchestrator for a one-shot transform
- [ ] System prompt under ~80 lines (longer = diluted attention)
- [ ] Expertise list is *specific*, not generic ("idiomatic LINQ with `System.Linq.Async`", not "good at C#")
- [ ] Task steps are numbered AND include an explicit stop condition
- [ ] Guidelines lead with what to DO; nevers come last
- [ ] No embedded example dialogue — document the real tools instead (Anthropic principle)
- [ ] For C#: `{WorkingDirectory}` + `{GetDepthGuidance()}` interpolated; `{GetFileOperationGuidelines()}` present iff agent touches files; class name ends with `Agent`
- [ ] For subagent: description ends with "Use when …"; tools whitelist if intentionally narrow

## Companions

- [[write-a-skill]] — when the user actually wants a *skill*, not an agent
- [[new-birko-subproject]] — if the agent needs a brand-new project (rare; usually goes in `Birko.AI.Agents`)
- [[verify-birko-conventions]] — post-scaffold lint for the C# path
