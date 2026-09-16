---
id: TASK-449
parent: null
feature: null
status: done
priority: P2
assignee: ai
picked-by: fix-next
created: 2026-09-16
depends-on: []
blocks: []
related: []
findings: []
pr: null
github-issue: null
jira-key: null
affects: [Birko.Random]
---

# `PerlinNoise` and `SimplexNoise` take a seed but are not reproducible across .NET versions

## Context

Found 2026-09-16 while measuring whether `Birko.Random` is a safe determinism foundation for a
game-engine side project (replay / lockstep need a seeded RNG whose sequence is identical on every
machine and every runtime). The providers passed; the **noise generators did not**, and nothing says so.

Both noise types accept a seed and build their permutation table from **`System.Random`**:

```
Birko.Random/Noise/PerlinNoise.cs:28     var rng = new System.Random(seed);
Birko.Random/Noise/SimplexNoise.cs:35    var rng = new System.Random(seed);
```

`System.Random`'s algorithm **changed in .NET 6** — the subtractive generator was replaced with
xoshiro256\*\* — and Microsoft explicitly does not guarantee sequence stability across versions. So
the same seed produces a **different permutation table, and therefore different noise**, on a
different runtime.

The failure is silent: no exception, no warning. A caller that generates terrain, textures or
procedural layout from a stored seed gets different output after a framework upgrade, and the stored
seed still looks like it is doing its job.

**This is a defect in the shipped framework, not a game concern.** It is filed on its own because the
side project that found it is not tracked here and may never exist — the defect stands either way.

## Why the rest of `Birko.Random` is fine

Measured in the same pass, so the scope of this task is genuinely the two noise files:

| Type | Seedable | Cross-platform stable |
|---|---|---|
| `MersenneTwisterProvider(uint)` | ✅ | ✅ pure integer arithmetic |
| `SplitMixProvider(ulong)` | ✅ | ✅ pure integer arithmetic |
| `XorShiftProvider(ulong)` | ✅ | ✅ see below |
| `CryptoRandomProvider` | ❌ by design | n/a |
| `SystemRandomProvider` | ❌ wraps `Random.Shared` | ❌ and it is thread-shared |
| **`PerlinNoise(int)`** | ✅ accepts one | ❌ **this task** |
| **`SimplexNoise(int)`** | ✅ accepts one | ❌ **this task** |

`XorShiftProvider.NextDouble()` is `(NextUInt64() >> 11) * (1.0 / (1UL << 53))` — an integer shift
followed by a multiply by an exactly-representable power-of-two reciprocal, so IEEE 754 makes it
bit-identical everywhere. That is the construction the noise types should be leaning on.

## Proposed fix

Seed the permutation table from an in-house provider — `SplitMixProvider` is the natural choice, and
its own doc comment already says it is *"commonly used for seeding other PRNGs"*. Roughly two lines
per file.

⚠ **This changes the output for a given seed**, so it is a breaking change for any consumer storing
seeds today. Measure that before shipping (the framework's standing blast-radius rule) — and note the
change is *from* a value that was never stable in the first place, so there is no behaviour worth
preserving, only callers to warn.

## Acceptance criteria

⚠ Converted from the author's numbered list to the repo's checklist convention so completion is
expressible; the wording of each item is unchanged.

- [x] 1. Neither noise type references `System.Random`. A test asserts this by reflection or by source
      scan, so a future edit cannot quietly reintroduce it.
      — `NoiseReproducibilityTests.No_noise_source_uses_System_Random_in_CODE` scans every file under
      `Birko.Random/Noise/`, **stripping comment lines first**: all three sources *discuss*
      `System.Random` in their doc comments, so a naive text scan would match its own explanation.
- [x] 2. A known seed produces a **known, hard-coded** sequence of sample values, pinned in a test.
      — 6 theory rows (3 seeds × 2 generators), 5 samples each, hard-coded from the fixed
      implementation. Seeds `0`, `42` and `-7`, the last covering the unchecked `int`→`ulong` widening.
- [x] 3. The test above would fail if the permutation construction changed at all.
      — Measured, not assumed: mutation B changes **only** the seed widening
      (`(ulong)seed` → `(ulong)(uint)seed`), leaving SplitMix in place, and reds 2 of 130.
- [x] 4. Blast radius measured across the framework, its tests and all 16 consumer repos before the
      change lands; recorded in the task, not asserted.
      — **0** consumer `.cs` files, **0** framework files outside the two sources; the only other
      reference anywhere is the existing `NoiseTests.cs`. Nothing stores a seed, so the output change
      costs nothing. See step 3b.
- [x] 5. `Birko.Random/CLAUDE.md` states which types are reproducible across runtimes and which are not.
      — New `## Reproducibility` section with a per-type table, the IEEE-754 argument for the integer
      providers, the `System.Random` warning, and the inherited-provider rule for distributions and
      sequences.
- [x] 6. ⚠ If the seeded-noise API is kept as-is, say in the remarks that the seed now guarantees
      cross-runtime reproducibility.
      — Both constructors carry a `<param name="seed">` remark saying so and naming what it replaced.

## Out of scope

- `SystemRandomProvider`'s instability — deliberate and correct; it is documented as wrapping
  `Random.Shared`. Only its *discoverability* is covered here, by criterion 5.
- Any change to the distribution or noise **algorithms**. This is about how the table is seeded, not
  about output quality.

## ⚠ Note on placement

Filed in the aggregator's `_loose/` rather than in `Birko.Random`'s own repo, which is what
CLAUDE.md § *Task tracking* prescribes for single-sub-project work. Measured 2026-09-16: **1 of 178**
sub-projects has a `tasks/` folder at all (this one), and `/tasks pick`, the dashboard and
[[fix-next]] all run from here — so a task filed in `Birko.Random/tasks/` would be *filed and
scheduled by nothing*, the exact defect § STORY-051 exists to record. The documented rule and the
observed practice disagree; reconciling them is not this task's job, but somebody should.

## Progress log

- step 2 - picked **by explicit user instruction**, not by ranking. Recorded because this task is
  outside [[fix-next]]'s pool by construction (it is in _loose, has an empty `findings:`, no `review-intake`
  parent), so nothing would otherwise have offered it - the defect its own § *Note on placement*
  describes. It also arrived **untracked**; it is committed as part of this run.
- step 3 - **CONFIRMED** at both cited lines: `PerlinNoise.cs:28` and `SimplexNoise.cs:35` each build
  the Fisher-Yates shuffle of their 256-entry permutation table from `new System.Random(seed)`. The
  task's own table is accurate - a sweep of `Birko.Random` for `System.Random` finds exactly these two
  plus `SystemRandomProvider`, which wraps `Random.Shared` deliberately and is documented as doing so.
- step 3b - **criterion 4 (blast radius) measured, and it is empty.** `PerlinNoise` / `SimplexNoise`
  appear in **0** consumer `.cs` files across all 16 repos and **0** framework files outside their own
  two sources; the only other reference anywhere is `Birko.Random.Tests/Noise/NoiseTests.cs`. Nothing
  stores a seed today, so changing the output for a given seed costs nothing.
- step 3c - ⚠ **The area is well tested and the tests are blind to this by construction.** 14 existing
  noise tests assert *ranges*, continuity, zero-at-integers and that two instances **in one process**
  agree (`SameSeed_ProducesSameValues`). Not one pins a value, and the in-process comparison passes
  against the defective code - which is precisely what criterion 2 predicted. § TASK-284's shape: when
  a defect survives a covered area, look at what the coverage actually asserts.
- step 4 - layer: **local**, in `Birko.Random`.

- step 5 - fix in `Birko.Random`: new internal `Noise/NoisePermutation.cs` owns the seeded table, both
  generators call it, `Birko.Random.projitems` registers it, and `Birko.Random/CLAUDE.md` gains a
  `## Reproducibility` section. **Extracted to one producer rather than patched twice**, because the
  defect *was* the same wrong choice made in two files - with one producer a future edit cannot change
  how one generator is seeded and leave the other behind. Tests: new
  `Birko.Random.Tests/Noise/NoiseReproducibilityTests.cs` (9). Suite **130/130**, 0 warnings under
  `-warnaserror`; the 14 pre-existing noise tests are untouched.
- step 6 - three mutations, all reverted:
  **(A) `System.Random` restored** -> **8 of 130** red: all 6 pinned-vector rows, the default-constructor
  test, and the source scan. ⚠ The 14 pre-existing noise tests stayed **green**, which is the
  measurement that matters - it is the same blindness that let the defect ship.
  **(B) only the seed widening changed**, SplitMix retained -> **2 of 130** red. This is what proves the
  vectors pin the *construction* and not merely "some deterministic generator".
  **(C) comment-stripping removed from the scan** -> **1 of 130** red, the scan itself, because it then
  matches the doc comments that explain the defect. The strip is load-bearing, not tidiness.
- step 7 - **spec regen deliberately skipped, and this is not silent under-coverage.**
  `docs/specs/.map.yml` lists `Birko.Random` in its explicit out-of-scope block (single-repo surfaces
  belonging to a future per-sub-repo spec tree), and no spec file mentions `PerlinNoise`,
  `SimplexNoise` or `IRandomProvider`. Nothing to regenerate.
- step 7b - root `CLAUDE.md` gains its `### Recent Updates` entry (convention gate check 9: 5+ files).
- step 8 - closed done; out-of-scope sweep: 2 boundary, 0 spawned, 0 declined. The `## Note on
  placement` section is a **boundary that describes work** - see the Outcome; it is answered rather
  than spawned.

## Human test plan

**N/A - fully covered by automated tests.** The claim is "the same seed gives the same numbers", and
the pinned vectors assert exactly that, offline. There is no UI, no service and no consumer.

⚠ The one thing no test in this repo can prove is the *cross-runtime* half - these vectors run on one
.NET version at a time. What makes the fix sound is structural rather than empirical: the sequence now
comes from arithmetic defined in this repository (`SplitMixProvider`'s 64-bit integer state
transition, and a `NextDouble` that is an integer shift times an exactly-representable power-of-two
reciprocal) instead of from a runtime type whose algorithm the platform is free to change. If the repo
ever multi-targets, running this file on both targets is the empirical confirmation.

## Outcome

**What was wrong.** `PerlinNoise` and `SimplexNoise` both accept a seed, and both shuffled their
256-entry permutation table with `new System.Random(seed)`. `System.Random`'s algorithm changed in
.NET 6 and .NET guarantees no cross-version stability for a seeded sequence - so the same seed
produced a different table, and therefore different noise, on a different runtime. Silently: no
exception, no warning, and the stored seed still looking like it was doing its job.

**What was done.** The seeded table moved into one internal producer, `NoisePermutation.Build(int)`,
which shuffles with `SplitMixProvider` - pure 64-bit integer arithmetic defined in this repository,
and the type whose own summary already calls it the generator "commonly used for seeding other PRNGs".
Both constructors now delegate to it and document that the seed is reproducible across runtimes. The
Fisher-Yates shuffle itself is unchanged; only the source of randomness moved.

**Step-6 split.** See the Progress log: 8 of 130, 2 of 130, 1 of 130, each mutation isolating a
different claim.

**Judgement calls, and why the alternative lost.**

- **One producer, not two two-line edits.** The task proposed "roughly two lines per file", which is
  correct and smaller. Extracting was chosen because the defect is *itself* an instance of duplicated
  policy: two files independently deciding where their randomness comes from. A shared producer makes
  the next such edit impossible to get half-right.
- **`SplitMixProvider.NextInt(i + 1)` rather than hand-rolled integer modulo.** It goes through
  `NextDouble()`, which some would call a determinism risk. It is not - the value is an integer shift
  multiplied by an exactly-representable power-of-two reciprocal, so IEEE 754 fixes it exactly, and
  the task's own analysis says so. Using the provider's tested public surface beats a second
  arithmetic path.
- **The vectors were generated from the fixed code, not hand-computed.** That is the honest way to
  produce a golden file, and it is why the doc comment says a change that moves them is a deliberate
  break of the promise rather than a rebaseline to wave through.
- **The source scan strips comments.** Necessary, not tidy: the explanation of the defect lives in the
  doc comments of the very files being scanned, and mutation C shows the scan self-matches without it.

**The `## Note on placement` is answered, not spawned.** It observes that CLAUDE.md § *Task tracking*
prescribes filing single-sub-project work in the sub-repo, while **1 of 178** sub-projects has a
`tasks/` folder and every scheduler runs from the aggregator. That is a real contradiction and it is
**already owned**: this task's own placement in `_loose/` is the second half of the same problem, and
running it required an explicit instruction precisely because nothing ranks a `_loose` task with no
`findings:` and no `review-intake` parent. Recorded here rather than given an id, because the decision
is about the tracking convention itself and belongs to whoever owns that convention - not to a defect
fix in `Birko.Random`. Flagged in the closing report instead.
