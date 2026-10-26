---
id: FEATURE-020
created: 2026-10-26
owner: both
# status — one of: idea, review (built, sign-off pending), done, dropped, superseded
status: idea
---

# Birko.Xaml — JSON/XML comparison, a diff viewer, a tag input and an accordion look

> Stakeholder-readable. A project manager or end user should understand the problem and the proposed shape without reading any code.

## Problem

The FinStat API desktop tester (Avalonia, built on Birko.Xaml) is being brought up to the web tester. The web tester has a
**compare across environment profiles** page: one request goes to a baseline profile and to several other profiles,
optionally for a list of company IDs (IČO), and each response is compared with the baseline. It is built from four web
pieces that the desktop framework does not have:

- the **comparison engine** that diffs two JSON documents (or two XML documents turned into the same shape) and reports
  each difference by path, a merged tree of both sides, and counts;
- the **diff viewer** that shows that result as a tree or side by side;
- the **tag input** used to enter the list of company IDs;
- the **accordion** that groups results.

Without them the desktop tester cannot offer the comparison page, and a desktop app would have to reimplement an engine
whose behaviour is already settled on the web — two engines that drift apart and report different "fields that differ".

The comparison engine is the blocker: the tester's comparison window is being built against a placeholder until it lands.

## Proposed shape

A port, not a redesign: the web behaviour is the specification.

1. **The comparison engine** in the desktop core library (no UI in it), giving the same answers as the web engine:
   the same paths, the same change kinds, the same tree statuses and counts, the same ignore / normalize / array-pairing
   options and the same path wildcards. XML is converted the same way (a repeated element becomes a list, a nil element
   becomes empty).
2. **One shared set of test examples**, kept with the web engine (the engine of record), that both the web and the
   desktop test suites run — so the two engines cannot quietly disagree.
3. **The diff viewer** with both views — the tree view first, then side by side — each with "only changes", the two
   side labels, and JSON or XML mode (in XML mode the original XML is what is shown side by side).
4. **The tag input**, with the web one's behaviour exactly: separators, duplicates, a maximum count, and the reasons it
   rejects a tag.
5. **The accordion** as the Birko look for the desktop's built-in expandable section.
6. Each new control shown in the desktop Gallery, and the change recorded for consumers.

As soon as the engine is usable — before the viewer and before sign-off — its public shape and a usage example go to
the desktop tester's maintainers so they can swap out their placeholder.

**Related, tracked separately:** the desktop form's password field shows its value in plain text under the Birko theme.
It is a defect, not a decision, so it is its own task ([TASK-768](../../../tasks/EPIC-015-birko-xaml-ui-framework/TASK-768-avalonia-textbox-theme-ignores-passwordchar.md)) with a regression test.

## Open questions distilled from the grill

| id | question | type | blocked-by | state | claimed-by |
|----|----------|------|------------|-------|------------|
| Q1 | Should the comparison engine be ported to the desktop at all, or should the tester call out to the web one? | decision | — | resolved → D1 | — |
| Q2 | Where does the engine live — the desktop core library, or a new library with no UI? | decision | Q1 | resolved → D2 | — |
| Q3 | Should the engine later move into a library with no UI, so server-side code can use it? | decision | Q2 | resolved → D3 | — |
| Q4 | Does the web engine already have tests the port could reuse? | research | — | resolved — yes: browser checks in the web playground (corrected 2026-10-26; the grill first answered "none" — see D5's history) | — |
| Q5 | How are the web and desktop engines kept in agreement? | decision | Q4 | resolved → D4 | — |
| Q6 | Where do the shared test examples live, and how does the web side run them? | decision | Q5 | resolved → D5 | — |
| Q7 | How much of the diff viewer comes in the first version? | decision | Q1 | resolved → D6, D7 | — |
| Q8 | Does the tag input add anything beyond the web one, such as checking each company ID? | decision | — | resolved → D8, D9 | — |
| Q9 | Is the accordion a new control or the Birko look for the built-in expandable section? | decision | — | resolved → D10, D11 | — |
| Q10 | Is the password-field bug part of this feature? | decision | — | resolved — no: a standalone defect task, TASK-768 | — |

The grill resolved every question it raised — see decisions.md.

**Fog** — concerns not yet precise enough to state as a question:

- How the side-by-side view lines the two sides up for very large responses on the desktop (the web view can lean on the browser; the desktop list may need virtualising). Surface it at the side-by-side task.

## Out of scope (initial)

- A per-tag validation hook on the tag input (company-ID checking stays in the tester) — recorded as D9.
- An accordion group where opening one section closes the others — recorded as D11.
- Moving the engine to a library with no UI — recorded as D3.

## Prototype

Skipped — a port of controls and an engine whose behaviour and look are already settled on the web; the shared test
examples are the proof for the engine, and the Gallery demos (D12) are the visual check for the controls.
