---
id: TASK-483
parent: null
feature: null
# status — one of: todo, in-progress, review (code done, sign-off pending), blocked, done, cancelled
status: done
priority: P2
assignee: ai
created: 2026-09-23
depends-on: []
blocks: []
findings: [FIELD-012]
pr: null
github-issue: null
jira-key: null
---

# `Tool.ExecuteAsync` keeps its abstract signature — the break was right, the silence was not

## Context

`6b4e374b` (2026-07-09) changed `Birko.AI.Contracts/Tools/Tool.cs:15` from a two-argument abstract
`ExecuteAsync` to

```csharp
public abstract Task<string> ExecuteAsync(string workingDirectory,
    Dictionary<string, object> input, CancellationToken cancellationToken = default);
```

An override of an abstract method must match the full signature, so the default value helps nobody:
every implementer had to change. Found while clearing an unrelated advisory in DraCode — its build
fails with **82 errors**, `41 × CS0534` + `41 × CS0115`, and has since 2026-07-09.

The question this task answers, posed in DraCode's TASK-077: should the token have arrived as a new
**virtual overload** instead, keeping consumers compiling?

## Decision: NO. Keep the abstract signature.

**A virtual three-argument overload forwarding to a two-argument abstract would compile everywhere
and cancel nothing.** Any tool that implemented only the two-argument version would accept a
`CancellationToken` and silently discard it. The agent run loop passes a token believing execution
can be cancelled; it could not. Tools do file, network and database I/O — un-cancellable tool
execution hangs the loop, and it would hang it *quietly*.

That is § Conventions rule 51's shape exactly — **"a silent no-op wearing a parameter's name"** — and
it is the failure this codebase keeps paying for: something that compiles, runs, reports success, and
is wrong. A compile error naming all 41 files is the **cheapest** failure mode available here.

**Keeping consumers compiling is not the goal when the price is a parameter that does nothing.**
DraCode's TASK-077 framed the overload as the better option because it "would have kept every
consumer compiling". That framing is wrong and is corrected there.

**Measured evidence that the migration is workable:** of the two consumers implementing `Tool`,
**BardStudio already did it** — `src/BardStudio.AI/Tools/DJTools.cs` carries the three-argument
signature throughout. The framework migrated its own 10 implementers in the sibling commit
`47955695`. One of two consumers followed without difficulty; the other never learned it had to.

## ⚠ The real defect is the SILENCE, and it is structural

A `.projitems` shared project is compiled into the consumer's own assembly and **has no package
identity** — so there is no version to bump and no `NU1605`-style signal. [[TASK-473]] established
this from the security side: NuGet's downgrade guard is blind to the shape Birko ships in. The same
property means **a breaking API change reaches consumers with no signal at all.**

So the framework made a correct change through a channel that cannot carry a warning, and nothing
noticed for **2½ months**:

- The framework's own CI builds one consumer, `Birko.Sandbox`, which implements no `Tool`.
- The change is **not in `CHANGELOG.md`** — verified, no entry mentions it.
- `CLAUDE-maintenance.md` said only *"update its README.md … includes breaking changes"*, which is
  advice about a file consumers do not read, not a protocol that reaches them.

## What changed here

1. **`Tool.cs` is untouched.** The decision is to keep it, and a decision to keep something is worth
   recording precisely because nothing in the diff will show it.
2. **`CLAUDE-maintenance.md` gains § Breaking changes in a shared project** — the protocol that was
   missing: a breaking change to a shared project's public or abstract surface requires a
   `CHANGELOG.md` entry naming the type, the old and new signatures, and the migration, because there
   is no package version to carry it.
3. **`CHANGELOG.md` gains the entry `6b4e374b` should have had**, with the migration for the 41
   classes, so the consumer that has been broken since July has something to follow.

## Out of scope

- **Fixing DraCode's 41 tool classes.** Consumer-owned and mechanical now that the migration is
  written down. DraCode's TASK-077 § Out of scope holds it.
- **Building more consumers in framework CI.** It would have caught this, and it is a much larger
  decision — DraCode does not currently build at all, so it cannot be a gate. Worth its own task if
  the silence recurs.
- **Reverting or re-litigating `47955695`.** Threading the token through the run loop was correct.

## Human test plan

N/A — this is a decision record plus documentation. The migration it documents is verified by
BardStudio already having completed it.
