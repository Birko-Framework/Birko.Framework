---
id: FEATURE-020
created: 2026-10-26
---

# Birko.Xaml — JSON/XML comparison, a diff viewer, a tag input and an accordion look — Decisions

> The decision ledger for stakeholders. Every idea-branch is a row with exactly one **state**. Rows are never deleted — `removed` is a state, not a deletion — so the ledger stays auditable.

## Decisions

| ID | Decision | State | Rationale | Date | By | → Tasks |
|----|----------|-------|-----------|------|----|---------|
| D1 | Port the web comparison engine to the desktop, giving the same answers: the same paths, change kinds, tree statuses, counts, ignore / normalize / array-pairing options and path wildcards; XML converted the same way | approved | The desktop tester needs the comparison page, and it is the tester's blocker. A port of settled behaviour, not a redesign | 2026-10-26 | František Bereň (maintainer) | TASK-770, TASK-771 |
| D2 | The engine lives in the desktop core library, with no UI in it | approved | Chosen in the grill: the tester already references that library, so nothing new to wire, and the tester is unblocked soonest | 2026-10-26 | František Bereň (maintainer) | TASK-770, TASK-771 |
| D3 | Move the engine into a library with no UI, for server-side use | deferred | Unblock condition: a non-desktop consumer needs the comparison. Moving it now would add a second way to include it and risk duplicate types for apps that pull in both | 2026-10-26 | František Bereň (maintainer) | — |
| D4 | Share one set of test examples (two inputs, options, expected differences and counts) between the web and desktop engines | approved | Chosen in the grill over desktop-only tests: the web engine's checks live outside its repo, and a shared set guards against drift in both directions | 2026-10-26 | František Bereň (maintainer) | TASK-769 |
| D5 | The shared examples live with the web engine, the engine of record; the desktop suite reads them from the neighbouring web checkout and skips with a stated reason when it is absent; on the web side they run in the web playground's browser checks, beside the comparison checks already there | changed | Changed from "the web side gains a small runner": the web engine already has browser checks in the playground (missed by the grill), and turning XML into a tree needs a browser, so the examples run there with no new tool | 2026-10-26 | František Bereň (maintainer) | TASK-769, TASK-770 |
| D6 | Diff viewer — tree view, with "only changes", side labels, and JSON or XML mode; the whole viewer shown in the web's card frame (a bordered, rounded card, a header bar with a line under it, a padded body; in side by side the two panes sit in that body a gap apart, with no frame of their own), on by default and switchable off | changed | Chosen in the grill: both views, as two pieces of work, tree view first (it shows the engine's merged tree directly). Changed after sign-off: the desktop viewer had no frame, so in the desktop tester its blocks did not read as distinct; the web viewer has one | 2026-10-26 | František Bereň (maintainer) | TASK-772, TASK-776 |
| D7 | Diff viewer — side-by-side view as on the web: two labelled panes, each the whole body laid out line by line (in XML mode the original XML), with added, removed, changed and ignored lines tinted; the two sides are not lined up, and "only changes" and open/close-all stay with the tree view. One addition the web does not have: scrolling one pane scrolls the other | changed | Changed from "with the same options": the web view shows each body whole and does not line the sides up, so the port follows it; scrolling together added for the desktop, where both panes are usually tall | 2026-10-26 | František Bereň (maintainer) | TASK-773 |
| D8 | Tag input with the web one's behaviour exactly: separators, duplicates, maximum count, and the reasons a tag is rejected | approved | Chosen in the grill: strict parity | 2026-10-26 | František Bereň (maintainer) | TASK-774 |
| D9 | A per-tag validation hook on the tag input (e.g. checking a company ID's format) | removed | Strict parity with the web tag input; company-ID checking stays in the tester | 2026-10-26 | František Bereň (maintainer) | — |
| D10 | The accordion is the Birko look for the desktop's built-in expandable section, matching the web accordion | approved | Chosen in the grill: the desktop already has the behaviour, only the look is missing | 2026-10-26 | František Bereň (maintainer) | TASK-775 |
| D11 | An accordion group where opening one section closes the others | deferred | Unblock condition: a desktop screen needs exclusive sections | 2026-10-26 | František Bereň (maintainer) | — |
| D12 | Each new control is shown in the desktop Gallery, and the change is recorded for consumers | approved | Requested with the feature: the Gallery is where a stakeholder sees the desktop controls | 2026-10-26 | František Bereň (maintainer) | TASK-772, TASK-773, TASK-774, TASK-775 |

_`Date` and `By` stay `—` until `/feature decide` stamps a verdict — they record **when/who decided**, not when the row was created (creation is in the History log)._

**States:** `proposed` (fresh from grill, awaiting decision) · `approved` (build it) · `deferred` (not now — note unblock condition) · `changed` (approved but altered — record the delta) · `removed` (rejected / out of scope).

Only `approved` and `changed` rows generate tasks at `/feature decompose`. No row is terminal: a `deferred`/`removed` decision overturned by later evidence (incl. production feedback) is **reopened** by adding a *new* `proposed` row that links the superseded one — the old row is never deleted.

## History log

> Append-only. Every state change gets a dated line with the reason — this is the "why it changed", not just the current value.

- 2026-10-26 — feature created; decisions seeded as `proposed` from the grill-me interview. Requested by the user in the
  ClientApi.CSharp (desktop tester) session and handed to this repo for the full lifecycle. The grill's answers came from
  the user in this session: engine in the desktop core library, shared test examples owned by the web repo, both viewer
  views as two pieces of work, strict tag-input parity, accordion as a look for the built-in section. The password-field
  bug was split out as TASK-768.
- 2026-10-26 — stamped as the grill proposed, by the maintainer in one batch: D1, D2, D4, D5, D6, D7, D8, D10, D12
  proposed → approved; D3 proposed → deferred (until a non-desktop consumer needs the comparison); D11 proposed →
  deferred (until a desktop screen needs exclusive sections); D9 proposed → removed (strict parity with the web tag
  input; company-ID checking stays in the tester).
- 2026-10-26 — decomposed under STORY-068 (EPIC-015): D1, D2 → TASK-770, TASK-771; D4 → TASK-769; D5 → TASK-769,
  TASK-770; D6 → TASK-772; D7 → TASK-773; D8 → TASK-774; D10 → TASK-775; D12 → TASK-772..775 (a Gallery page and a
  changelog entry in each).
- 2026-10-26 — D5 approved → changed: the web side runs the shared examples in the web playground's existing browser
  checks instead of a new small runner. Found while starting TASK-769: the web engine is already checked there (76 JSON
  and the XML comparison checks, from TASK-725) — the grill's lookup missed them — and the XML conversion needs a
  browser, which a plain runner does not have. Decided by the maintainer.
- 2026-10-26 — D7 approved → changed, decided by the maintainer when TASK-773 started: the side-by-side view follows
  the web's exactly (two whole bodies, changed lines tinted, not lined up; "only changes" stays a tree-view switch),
  plus one desktop addition — the two panes scroll together. The task's first draft had asked for lined-up rows with
  blank partners, which the web view does not do.
- 2026-10-26 — Review. Gate A: all seven tasks built on `main` (TASK-769–775), every approved and changed decision
  built (D1, D2 → TASK-770/771; D4, D5 → TASK-769/770; D6 → TASK-772; D7 → TASK-773; D8 → TASK-774; D10 → TASK-775;
  D12 → the four Gallery pages and the changelog entry). The fidelity pass was done inline, against each decision.
  **no spec surface** (by decision, not omission): `Birko.Xaml.*` is excluded from this repo's spec map ("own tree",
  TASK-689), and the web engine and its shared examples live in the Birko.Web repo.
- 2026-10-26 — Gate B: the Human test plans of TASK-772–775 run by František Bereň in the desktop Gallery, every step
  passed; the accordion look was checked in the Gallery only, not side by side with the web playground.
- 2026-10-26 — FEATURE-020 signed off by František Bereň (maintainer) after review. TASK-768 (the password-field
  defect filed with this request, not part of the feature) stays at verify until it is checked in the desktop tester.
- 2026-10-26 — D6 approved → changed, after sign-off: the viewer takes the web viewer's card frame. Asked for by the
  desktop tester's user, through its session, because the viewer's blocks did not read as distinct. The maintainer was
  shown three ways to separate the side-by-side panes and chose the web's (a gap, no frame per pane) over a divider
  or a frame per pane. Not built yet, so the feature went back to idea and the change became TASK-776.
- 2026-10-26 — TASK-776 built and checked headlessly: the feature is back at review until the frame is seen in the
  Gallery and in the desktop tester.
- 2026-10-27 — TASK-776's test plan passed: the user saw the framed viewer in the desktop tester's Compare window and
  in the desktop Gallery, both views, every theme. FEATURE-020 signed off again by František Bereň (maintainer).
