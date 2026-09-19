---
id: TASK-468
parent: null
feature: null
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P2
assignee: ai
created: 2026-09-19
depends-on: []
blocks: []
related: [TASK-457]
findings: [FIELD-008]
pr: null
github-issue: null
jira-key: null
---

# `BIRKO_SRC` names the Framework checkout to MSBuild and the Web checkout to esbuild

Found while writing the bring-up instructions for a second machine. One environment variable, two
resolvers, two different directories:

| Reader | Expects `BIRKO_SRC` to be | Evidence |
|---|---|---|
| MSBuild | `Birko\Framework` | every consumer's `Directory.Build.props`; `Birko.Web`'s own `.github/workflows/token-parity.yml:46` sets `BIRKO_SRC: ${{ github.workspace }}/birko/Framework` |
| esbuild | `Birko\Web` | `birko-src.mjs` / `build.js` — `if (env) return env`, then the alias map reads `${src}/Birko.Web.Core/src/index.ts` |

**Measured:** 8 MSBuild readers (`Directory.Build.props` in BardStudio, Birko.Sandbox, DraCode,
gameshow-app, Latent, Presenter, Symbio, WorkoutTracker) and 8 JS readers. **Five repos are in both
lists** — DraCode, gameshow-app, Presenter, Symbio, WorkoutTracker — so in those five a single
exported value breaks one of that repo's *own two* builds, whichever value you choose.

## ⚠ The framework instructs you to create the breakage

The JS resolver's own throw is:

```
Cannot locate Birko\Web. Set the BIRKO_SRC env var to its path.
```

A developer who hits that message does what it says, points `BIRKO_SRC` at the Web checkout, and their
next `dotnet build` fails — `Symbio.Birko.csproj` carries **98 unconditional `<Import>`s** of
`$(BirkoSrc)\Birko.X\Birko.X.projitems` (0 conditional), so that is 98 × MSB4019 naming paths that do
not exist. The documented remedy for one toolchain is the defect of the other: § Conventions' *verify
the escape hatch opens — a guard whose opt-out throws is a wall wearing a door's label*, arriving
through an error message rather than a guard.

## ⚠ And the code asserts the parity that does not hold

- `Symbio/src/Frontend/Symbio.UI/build.js:15` — *"Mirrors `$(BirkoSrc)` in Directory.Build.props for the MSBuild side."*
- `WorkoutTracker/Reps.Web/build.js:16` — *"Mirrors `$(BirkoSrc)` in Directory.Build.props so one convention drives both builds."*

Both are true of the **walk-up** branch, which is the one they were written about, and false of the
**env-var** branch directly above them. So the comment a reader consults before exporting the variable
is the comment that tells them it is safe.

## Why it has not bitten yet, and why that is about to change

`BIRKO_SRC` is unset on the primary dev box — the bucket layout carries it — and every CI job either
sets it for one toolchain or leaves it unset. The variable is only dangerous when it is *exported for
a session*, which is idiomatic on Linux (`~/.bashrc`, `~/.profile`, a devcontainer `ENV`) and rare on
the Windows box this was developed on. A second machine being set up on Linux is precisely the trigger.

Both directions fail **loudly** — MSB4019 names the missing path, esbuild reports unresolved aliases —
which is why this is P2 rather than higher. The cost is a debugging session that starts from a
variable somebody set in order to be helpful.

## Acceptance

1. One name per meaning. Proposal: `BIRKO_WEB_SRC` for the Web bucket, `BIRKO_SRC` stays MSBuild's
   (8 readers including a committed CI workflow, so it is the expensive one to move). Either direction
   is acceptable; what must hold is that **no single exported value can satisfy one resolver while
   breaking another.**
2. Transition rather than a flag day: a JS resolver reads `BIRKO_WEB_SRC` first, then falls back to
   `BIRKO_SRC` **only when that path contains `Birko.Web.Core`**, then the walk-up. A `BIRKO_SRC`
   pointing at the Framework must be *ignored*, not consumed — silently resolving aliases against the
   wrong bucket would be worse than today's loud failure.
3. Every message names the variable **this** caller uses. The current throw sends a web developer to
   the .NET variable, which is how the trap is sprung.
4. The two parity comments above are corrected, and say which branch mirrors `$(BirkoSrc)` and which
   does not.
5. `CLAUDE.md` § *Usage in Consumer Solutions* states both names and that they point at **different
   checkouts**. It currently describes both mechanisms in one paragraph — `$(BirkoSrc)` … *"then the
   `BIRKO_SRC` environment variable"* and *"their `build.js` walks up to find `Birko\Web` (or honors
   `BIRKO_SRC`)"* — without ever saying the two values differ.
6. Decide where the fix lands before writing it. `birko-src.mjs` is the Playground's extracted resolver
   and exists **only** there; the other five consumers each carry a copy-pasted `resolveBirkoSrc`.
   Editing six copies of one rule is the shape § Conventions keeps recording as the cause — resolve
   whether the shared resolver moves into `Birko.Web` (importable by every consumer, one producer) or
   whether six edits are genuinely the smaller change, and record which and why.

## Out of scope

- The bucket **casing** rule (`Birko/Web` must be capital-B on a case-sensitive filesystem). Real, and
  already documented in `Birko.Web.Playground/.github/workflows/pages.yml`; it is a property of the
  walk-up branch, not of the variable.
- The walk-up and layout convention itself. Both toolchains agree there and it needs no change — this
  task is only about the env-var branch that overrides it.
- Rolling the fix out to the consumer repos' own commits. They are separate repositories, so the
  framework-side change and each consumer's adoption land separately; list them here when the shape is
  settled rather than assuming one commit covers it.
