---
id: TASK-160
parent: STORY-053
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: done
priority: P2
assignee: ai
created: 2026-08-09
depends-on: []
blocks: []
# findings: ids this task remediates, from a review/audit/spec-harvest pass (CR-* SEC-* SH-* VC-*)
findings: [SH-M152, SH-M153, SH-M154, SH-M155, SH-M156, SH-M157, SH-M158, SH-M159, SH-M160, SH-M161, SH-M162, SH-M163, SH-M164, SH-M165, SH-M166, SH-M167, SH-M168, SH-M169, SH-M170, SH-M171]
pr: null
github-issue: null
jira-key: null
---

# Triage the 20 medium spec-harvest findings in `llm-provider-and-agents`

## Context

Filed by `/tasks intake --epic EPIC-014` on 2026-08-09, decomposing [[STORY-053]] — which held all
421 medium findings as an unscheduled checklist. A checklist line is
filed, not scheduled: only `status: todo` **tasks** are ranked by `/tasks pick`, by the `Next up` snapshot,
or by [[fix-next]], so the whole backlog was invisible to every picker. This task owns the **20** findings
that fall in the `llm-provider-and-agents` area.

**Findings:** `SH-M152`–`SH-M171` (contiguous), detailed in
[`SPEC-HARVEST-FINDINGS-2026-07-30.md`](../SPEC-HARVEST-FINDINGS-2026-07-30.md) § Medium severity →
`### area: llm-provider-and-agents`, lines 1374–1495.

**The contract under review** is specced in [`docs/specs/llm-provider-and-agents.md`](../../../docs/specs/llm-provider-and-agents.md), harvested from
2 source globs — `../Birko.AI.Contracts/**/*.cs`, `../Birko.AI/**/*.cs`. Every one of
them points into a **sibling repo**, so a fix here normally lands as three commits in three repos (production,
regression suite, this file) per CLAUDE.md § Integration model.

**These are unverified harvester claims, not confirmed defects.** Confirm each against the code before fixing.
The prior to carry in comes from the only findings anyone has checked by hand: of 15 high findings verified at
harvest time, **12 held exactly and 3 needed their scope corrected**, 0 were outright refuted — so expect
roughly a quarter to be imprecise rather than wrong. Refuting on the record is a valid close; a finding
silently dropped is one the next sweep re-raises and someone re-litigates from scratch.

**Ordering constraint — the spec currently documents these defects as shipped behaviour.** The harvest specced
what the code *does*, defects included, which is exactly what let it find them. So a behavioural fix leaves
`docs/specs/llm-provider-and-agents.md` lying until `/specs regen llm-provider-and-agents` runs, and **that spec diff is the fix's evidence**.

## Acceptance criteria

- [x] All 20 findings are marked **confirmed**, **confirmed-narrower**, or **refuted** against the code,
      with the verdict and its evidence (`file:line` + the mechanism, not just the rule) written back into
      `SPEC-HARVEST-FINDINGS-2026-07-30.md`. Every `Verdict:` line names the specific code it traced
- [x] Confirmed findings are fixed with a regression test, or explicitly waived with a recorded reason.
      Findings sharing a root cause are fixed **together**, not one edit each
- [x] Each regression test is **red-verified**: reverting the fix fails it. Report the split as numbers, and
      name any test that passes either way as a contract pin rather than as evidence
- [x] Any behavioural fix is followed by `/specs regen llm-provider-and-agents`, with the spec diff reviewed as the change's
      evidence
- [x] A confirmed finding too large for this task is spawned via `/tasks spawn` — never left as a ticked box
      with the work undone
- [x] [[STORY-053]]'s **Progress** line and `finding-count` reflect this area's closed count

## Out of scope

- The other 401 medium findings — they belong to the other 21 per-area tasks under [[STORY-053]].
- High-severity findings in this area — [[STORY-051]] owns those.
- Low-severity findings in this area — [[TASK-173]] owns them. Where a fix closes both, do it once and
  cross-reference; don't split one edit across two tasks.
- **Test gaps.** Test coverage was explicitly out of scope for the harvest sweep, so a missing test is not a
  finding here — only a test a confirmed fix needs.
- Per-sub-repo spec trees, which would make this area's staleness measurable from its own repo — that's
  [[TASK-131]].

## Human test plan

N/A — fully covered by automated tests: a library contract (parsers, message building, the agent loop, tools) with
no UI surface; every fix has a red-verified test.

## Resolution (2026-10-18)

All 20 were reproduced against the current code (after TASK-530–535, which rebuilt part of this area). Verdicts with
the traced code are in the findings doc. **19 confirmed, 1 narrower (SH-M155); 18 fixed here, SH-M152 found fixed by
TASK-544, SH-M169's cancellation half fixed by TASK-534.** None was spawned or waived.

Fixed in groups by root cause:
- **OpenAI-style parsers** (SH-M157..M163, `LlmProviderBase`): every probe is shape-guarded (`"error": null`,
  `"tool_calls": null`, a null `choices`, a missing `delta`). Token counts go through `ReadTokenCount`. One
  `MapFinishReason` serves sync and streaming: `length` is `max_tokens` with no truncated tool call, and
  `content_filter` stays itself.
- **Request and message building** (SH-M164..M166): the streaming request is disposed per attempt; every text block of
  an assistant tool turn is kept; tool results are read from anonymous objects, dictionaries or JsonElements, and
  anything else is refused (rule 41).
- **Agent loop** (SH-M153..M155): every built-in tool's failure starts `Error:`; a tool_use stop with no call ends the
  run; a null callback no longer wipes a shared provider's.
- **Tools and options** (SH-M156, M167..M171): `AgentFactory` is concurrent and returns snapshots. `search_code`
  honours `AllowedExternalPaths`, has a 2-second match timeout, and observes cancellation. `run_command` reports a
  non-zero exit; `ask_user` propagates cancellation; `Merge` copies only what the other instance set.

**Red / pin split:**
- 34 new tests: providers 18, tools/options 9, agent 5, plus the factory and Merge tests counted there.
- 26 + the SH-M168 hang failed on HEAD. On HEAD, `(a+)+$` over a 41-character line hung the test host until it was
  killed by PID.
- 4 pass either way and are contract pins: `stop` maps to `end_turn`; an ungranted path is still refused; a set scalar
  still wins in `Merge`; an explicit callback is still routed to the provider.

Regression: every importer of AI.Contracts / AI / AI.Providers (AI 65, Agents 63, Providers 238, Orchestration 42,
Resilience 13, Resilience.Data 24, VersionControl.Git 29).

The spec `llm-provider-and-agents` was updated by hand, not by a full `/specs regen`, at each requirement that described
a fixed defect as shipped behaviour: parsers, streaming, message building, the agent loop, the factory, `Merge`,
`search_code`, `run_command`, `ask_user`, and the tools' `Error:` contract.
