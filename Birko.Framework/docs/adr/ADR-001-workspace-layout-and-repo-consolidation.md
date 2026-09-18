# ADR-001 — Workspace layout and repository consolidation

- **Status:** Superseded in part, 2026-09-18
- **Original:** 2026-06-17/18 working session (was `C:\Source\Birko\WORKSPACE-STRUCTURE.md`, untracked)
- **Supersedes:** nothing
- **Superseded by:** the monorepo migration recorded in this same ADR (see *Outcome* below)

## Outcome (2026-09-18)

The June session left **open question #1** — *"Framework: stay separate (manifest-only) or eventually
monorepo?"* — recorded as *"Decision so far = stay separate. Monorepo remains a possible future win
(233→1)."* **That question is now closed: the framework was consolidated into one repository.**

What changed the answer:

1. **§3 of the original already argued for it.** It concluded the framework is *"effectively
   all-or-nothing… you can't hand-pick 60 of 233 module repos and get the dependency closure right"*,
   and noted in passing that this *"is also the argument for an eventual framework monorepo"*.
2. **[[TASK-131]] measured a defect caused by the polyrepo.** Every one of the aggregator's 25 spec
   areas globbed *out* of its own repo while `generated-at` stamped only that repo's HEAD, so
   `/specs verify`'s staleness primitive could never observe a source change — the guard was
   **decorative**, and `roadmap`'s DV7/DV8 inherited that. One repo fixes it outright.
3. **A fix and its test could not be atomic.** Under the three-repo model `git bisect` ran tests from
   the test repo's HEAD — a tree from a different day — and `git revert` of a fix left its test behind.
4. **The size argument was measured away.** All 178 framework repos totalled ~47 MB of history; the
   consolidated repo packs to **14 MB**. There was nothing to make smaller by splitting.

**What was NOT adopted:** the proposed `birko-workspace` repo with `workspace.manifest.json` +
`bootstrap.ps1`. It existed to answer *"how do I reproduce 245 checkouts on another machine"*, which
is now `git clone` × 2. The bucket layout it prescribed (`Birko\{Framework, Consumers}`, FinStat and
`WhMan`/`EventSourcing` flat at the root, `aicode\` for scratch) **stands unchanged** — the buckets
became repositories rather than being reorganised.

**Still open from the original:** the data-loss gap. Of the folders it flagged as untracked,
`Birko.Sandbox` is now a repo in the `Birko-Framework` org; **WorkoutTracker (298 commits), Presenter
(72), BardStudio (50) and Latent (13) remain local-only with no remote** — 433 commits that exist on
one disk. Deliberately left that way on 2026-09-18.

---

## Original document (2026-06-18), verbatim

# Birko Workspace — Structure Analysis & Improvement Plan

> Draft analysis from a working session on 2026-06-18. Goal: make `C:\Source\Birko`
> easy to maintain, correctly stored in git, and easily reproducible on another
> machine — while pulling only the parts a given machine actually needs.
>
> **Decision so far:** keep the repos **separate** (polyrepo). No monorepo merge.
> The work is to make the polyrepo *reproducible and selectable*, not to consolidate it.

---

## 1. What's true today (findings)

- **245 git repos** under `C:\Source\Birko`. **All 245 have a remote** (nothing is local-only):
  - 236 on `github.com` (the `birko` org)
  - 6 on Azure DevOps (FisData product)
- Breakdown of the 245:
  - **~233 = the framework itself** — `Framework\` modules + `Framework.Tests\` + the 3 `Web\` libs.
  - **12 = consumers** (Affiliate, BardStudio, DraCode, FisData.Stock + .Core/.API/.Web/.Angular, gameshow-app, Presenter, Symbio, WorkoutTracker).
- **4 consumer folders are NOT in git** (have real content, 8–12 items each, would be *lost* on a move):
  - `Consumers\Birko.Sandbox`
  - `Consumers\Birko.Web.Playground`
  - `Consumers\Symbio.Core`
  - `Consumers\Symbio.Monitor`
- **`C:\Source\Birko` is not a repo** and has **no manifest and no clone script**.
  Reproducing the workspace today = manually cloning 245 repos into exactly the right
  relative folders.
- **The folder layout is load-bearing.** Consumers resolve the framework via
  `$(BirkoSrc)` which defaults to `..\..\Framework`; the TypeScript build (esbuild)
  walks up to find `Birko\Web`. If the relative positions of `Framework` / `Web` /
  `Consumers` aren't reproduced, nothing builds.
- **Known mis-wiring found:** `Consumers\BardStudio` remote points at
  `https://birko@dev.azure.com/birko/Affiliate/_git/Affiliate` — i.e. the **same URL as
  Affiliate**. BardStudio is either wrongly configured or not really its own repo.
  There may be more like this; today nobody can see them because there's no inventory.

### The architecture (for context)
- Framework modules are **MSBuild Shared Projects** (`.shproj` + `.projitems`), *not*
  packable libraries. **Source-only, no NuGet, no per-module versioning** (documented in
  `Framework\Birko.Framework\CLAUDE.md`).
- Consumers compile the framework from source by importing `.projitems` into one (or a
  few) **aggregator** `.csproj` (e.g. `Symbio.Birko`), located via `$(BirkoSrc)` /
  `BIRKO_SRC`.
- Two sibling buckets resolved by two toolchains:
  `Birko\Framework` for MSBuild, `Birko\Web` for esbuild.

---

## 2. The core problem

Two very different kinds of thing are currently treated the same way:

1. **The framework (~233 repos)** — source-only, share one `.slnx`, **always travel
   together**, and real changes routinely span many modules at once (e.g. the
   `Birko.Data.InMemory` consolidation touched 4 test repos + several framework repos in
   one logical change). Splitting them into 233 separate repos buys nothing and costs:
   233 clones, 233 histories, no atomic cross-module commit, and no single source of
   truth for "what exists."
2. **The consumers (12+ repos)** — genuinely independent products, own remotes/teams,
   and **must be selectable per machine**.

---

## 3. Key insight — selective cloning works at the consumer level, not the module level

- **Consumers** are the right unit to be selective about (grab Symbio but not Affiliate, etc.).
- **The framework is effectively all-or-nothing.** A single consumer like Symbio imports
  ~60 `Birko.*` modules, and those modules depend on each other
  (Contracts → Data.Core → Stores → …). "The parts of the framework I need" is, in
  practice, *almost the whole framework*. You can't hand-pick 60 of 233 module repos and
  get the dependency closure right — the aggregator `.csproj` fails the moment one
  imported `.projitems` is missing.

**Mental model to adopt:**

```
framework  = ONE group, always pulled together   (Framework + Framework.Tests + Web)
consumers  = MANY groups, pulled à la carte       (symbio, fisdata, affiliate, …)
```

i.e. keep everything as separate repos (as desired), but **stop treating the ~233
framework repos as individually selectable** — they're one logical unit.
(This is also the argument for an eventual framework monorepo, but merging is *not*
required to get the benefit — just treat them as one group.)

---

## 4. Recommended target model (keep polyrepo)

```
C:\Source\Birko\
├── birko-workspace/            ← small new repo: the bootstrap + manifest + this doc
│   ├── workspace.manifest.json ← every repo: remote URL + target path + group
│   ├── bootstrap.ps1           ← clones the requested groups into the right folders
│   └── WORKSPACE-STRUCTURE.md  ← (this file)
├── Framework/                  ← group "framework" (mandatory)
├── Framework.Tests/            ← group "framework"
├── Web/                        ← group "framework"
└── Consumers/
    ├── Symbio/                 ← group "symbio"  (+ Symbio.Core, Symbio.Monitor)
    ├── FisData.Stock*/         ← group "fisdata"
    └── …                       ← only the groups this machine asked for
```

Reproduce a machine in one command:

```powershell
.\bootstrap.ps1 -Groups framework,symbio      # framework is mandatory; add consumers you want
.\bootstrap.ps1 -Groups framework,fisdata
.\bootstrap.ps1 -All
```

The manifest records, per repo: **remote URL, target relative path, group**. Groups can
bundle a product's repos (e.g. `fisdata` = `FisData.Stock` + `.Core` + `.API` + `.Web` +
`.Angular`; `symbio` = `Symbio` + `Symbio.Core` + `Symbio.Monitor`).

---

## 5. What has to be true for "clone only what's needed" to actually work

Currently **none** of these is fully satisfied:

1. **Everything you might want is pushed.**
   - ✅ for the 245 repos.
   - ❌ the 4 non-git folders → must be `git init`+pushed (or deleted if scratch) before
     they can ever be pulled elsewhere. Note Symbio likely *needs* `Symbio.Core` /
     `Symbio.Monitor`, so those must become repos in the `symbio` group.
2. **A manifest + bootstrap script**, so reproducing a machine is one command, not
   remembering 233 framework URLs + a dozen consumer URLs and placing each in the exact
   relative folder.
3. **Layout reproduced, or `BIRKO_SRC` set.** Either the script drops repos into the
   correct relative folders, or set `BIRKO_SRC` once per machine so build resolution
   doesn't depend on where things were cloned.

---

## 6. Action plan (no monorepo)

1. **Triage + push the 4 non-git folders** (the data-loss gap). Decide per folder:
   real product piece → `git init` + push + add to manifest; scratch → delete or move to
   a personal repo.
2. **Build an inventory** of all 245 repos: remote URL + current path + suggested group.
   Non-destructive; also surfaces mis-wired remotes (e.g. the BardStudio→Affiliate bug).
3. **Turn the inventory into `workspace.manifest.json` + `bootstrap.ps1`**, committed to a
   small top-level `birko-workspace` repo.
4. **Decide on `BIRKO_SRC`** for layout-independence (see open question below).

> **Best non-destructive first step:** the inventory (step 2). It's the thing most
> missing, and it tells us whether any of the 245 remotes are mis-wired before we trust
> them on another machine.

---

## 7. Open decisions (to revisit)

1. **Framework: stay separate (manifest-only) or eventually monorepo?**
   Decision so far = **stay separate**. Monorepo remains a possible future win (233→1,
   atomic cross-module commits) but is *not* needed for clonability.
2. **The 4 non-git folders** — which are keepers (init + push) vs throwaway?
   Need to confirm what `Symbio.Core` / `Symbio.Monitor` are.
3. **Hosts** — leave the github (236) / Azure DevOps (6, FisData) split, or unify?
   Splitting FisData out is probably correct (separate product).
4. **`Web/`** — its own repo (different TS/esbuild toolchain/cadence) vs folded into a
   framework unit. It always travels with the framework and consumers always need it.
5. **`BIRKO_SRC` env-var contract needs cleanup.** The docs imply MSBuild reads
   `BIRKO_SRC` as the *Framework* root while esbuild reads it as the *Web* root — those
   can't both be one value. Verify and reconcile before relying on the env var.

---

## 8. Status

- [ ] Triage + push the 4 non-git folders
- [ ] Build repo inventory (245 repos: URL + path + group)
- [ ] Generate `workspace.manifest.json`
- [ ] Write `bootstrap.ps1` (clone by group, correct relative paths)
- [ ] Create `birko-workspace` repo and commit manifest + script + this doc
- [ ] Decide/standardize `BIRKO_SRC`
- [ ] Fix mis-wired remotes found during inventory (e.g. BardStudio)
