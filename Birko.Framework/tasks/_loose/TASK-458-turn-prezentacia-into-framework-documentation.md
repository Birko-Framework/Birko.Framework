---
id: TASK-458
parent: null
feature: null
status: done
priority: P3
assignee: ai
created: 2026-09-18
depends-on: []
blocks: []
related: [TASK-457]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Turn `PREZENTACIA.md` into published framework documentation

## Context

`PREZENTACIA.md` (56 KB, at the aggregator root) is a draft explaining **how Birko.Framework works**.
Nothing links to it. It is the only hand-written narrative overview in the tree: `docs/` holds 44
per-area references plus generated specs, with no "here is how the whole thing fits together" entry
point — which is exactly what a newcomer (or a consumer evaluating the framework) needs first.

Kept rather than deleted during [[TASK-457]] for that reason.

## Why it is NOT a wiki candidate by default

The standing argument against GitHub Wiki (recorded when the org was set up) is about `docs/specs/`:
they are generated from code, stamped with the generating commit, and reviewed as an
intended-vs-unintended behavioural diff — a wiki is a separate git repo with no branches or PRs, so a
regenerated spec could never appear in the PR diff.

**`PREZENTACIA.md` is the one doc that escapes all three objections**: it is hand-written, not
stamped, and not reviewed as a behavioural diff. So a wiki is defensible here where it is not
elsewhere. The recommendation is still `docs/` published via **GitHub Pages** — it then versions
alongside what it describes and can link into the generated specs — but that is this task's decision
to make, not a foregone one.

## Acceptance

1. Decide the destination: `docs/` + Pages (recommended), wiki, or Outline (where the Birko.Game
   engine design already lives).
2. Bring the content up to date — it predates the monorepo, so any repo-layout claims in it are stale.
3. Link it from `README.md` as the narrative entry point.
4. Either delete `PREZENTACIA.md` or make it the published source; do not leave two copies.


## Done 2026-09-18

Published as the **wiki**: https://github.com/Birko-Framework/Birko.Framework/wiki — 11 pages plus a
sidebar, English, 0 broken links.

**Destination reasoning.** The standing argument against a wiki is about `docs/specs/`: generated,
stamped to a commit, reviewed as a behavioural diff — none of which a wiki can carry. A hand-written
usage guide escapes all three objections, so the wiki is the right home for *this* and remains the
wrong home for specs.

**Ground-up rewrite, not a translation.** `PREZENTACIA.md` was an 818-line Slovak *catalogue* — how
many projects exist per area — and the gap was a *guide*. The counts were also badly stale (it claimed
14 storage projects against 65 under `Birko.Data.*`). It is deleted; the catalogue role stays with
`README.md`, which nothing else replaces.

**Every example was compiled and run before being written down**, via a scratch consumer wired exactly
as the guide tells a reader to wire theirs. That surfaced six things the existing docs get wrong, now
corrected on the pages:

| Finding | Where the old docs were wrong |
|---|---|
| You cannot hand-pick `.projitems` — 10 rounds of adding one at a time never closed | README implied "import what you need" |
| 163 of 178 imports is the proven working set | undocumented |
| `.projitems` bring their own `PackageReference`s | undocumented; declaring them yourself warns `NU1504` |
| `SqLiteSettings` is in `Birko.Data.SQL.SqLite.Stores` | the settings-chain docs imply `Birko.Configuration` |
| Attributes are `PrecisionField` + `ScaleField` | there is no `DecimalField`; both are needed or money truncates |
| `Birko.Data.SQL.SqLite/CLAUDE.md` lists 3 dependencies | it needs at least 8 |

This independently re-proved `docs/adr/ADR-001` §3's conclusion that the framework is "effectively
all-or-nothing", which is more useful to a newcomer than the README's previous framing.

**`README.md` reduced from 635 to 483 lines** — the 171-line consumer-setup walkthrough became a short
pointer, since it now duplicated and partly contradicted the wiki. Its 326-line project index was
**kept**: that is reference material the wiki deliberately does not carry.

## The drift guard shipped too

`wiki-snippets.yml` compiles the wiki's C# on every push touching `Birko.*`, nightly, and on demand.
**Green on its first run: 24 snippets compiled, 4 skipped, 163 projects resolved.**

It earned its place immediately, finding **six API errors that writing-then-reading had not** —
including a migration example written against an `ISchemaBuilder.AlterCollection` that does not
exist, and a set-membership filter using an array, which on .NET 9+ binds to
`MemoryExtensions.Contains(ReadOnlySpan<T>, T)` and therefore cannot appear in an expression tree at
all. Both would have failed for the first reader who tried them.

**Two design points worth carrying.** *Compile every block standalone* fails on 24 of 28, because most
blocks are fragments by design — they use `store` because the prose just introduced it. Forcing them to
be self-contained would mean writing the guide for the compiler rather than for people, so pages carry
`<!-- verify:context -->` and genuinely unverifiable blocks carry `<!-- verify:skip reason -->`; both
are HTML comments and neither renders. And the import list is **generated** from the Sandbox
aggregator, so the guide is checked against the very list it tells readers to copy, with no second
list to drift.

⚠️ **An early measurement of mine was wrong and is worth recording:** "18 of 28 compile" was an
artefact of the build aborting on syntax errors before semantic analysis. The real number was 4. A
compiler that stops early reports a subset, not a result.

**What it does not catch**, stated in `ci/wiki-snippets/README.md` so nobody over-trusts it: whether
the prose is true, whether the code does what the text claims, or anything stale that still compiles.
