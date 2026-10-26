---
id: STORY-068
parent: EPIC-015
status: in-progress
created: 2026-10-26
feature: FEATURE-020
affects: [Birko.Xaml.Core, Birko.Xaml.Avalonia, Birko.Web.Core]
---

# Birko.Xaml parity for the web tester's compare page — diff engine, diff viewer, tag input, Expander look

## User story

As the maintainer of the FinStat desktop API tester (ClientApi.CSharp, Avalonia on Birko.Xaml), I want the pieces the web
tester's "compare across environment profiles" page is built from — `diffJson` / `xmlToTree`, `b-json-diff`,
`b-tag-input` and `b-accordion` — so the desktop tester can offer the same page with the same answers.

Decisions: [FEATURE-020](../../../docs/features/FEATURE-020-xaml-diff-and-parity-controls/decisions.md).

## Tasks (build order)

1. [TASK-769](TASK-769-shared-json-diff-vectors.md) — shared diff test vectors in Birko.Web, run by the Playground (D4, D5) — **done**
2. [TASK-770](TASK-770-xaml-core-json-diff-engine.md) — the JSON diff engine in Birko.Xaml.Core (D1, D2) — **done**, API sent to clientapi-csharp-d1
3. [TASK-771](TASK-771-xaml-core-xml-tree.md) — `xmlToTree` / `xmlToLines` in Birko.Xaml.Core (D1, D2) — **done**
4. [TASK-772](TASK-772-xaml-json-diff-viewer-tree.md) — diff viewer control, tree view (D6, D12) — **built, awaiting verification**
5. [TASK-773](TASK-773-xaml-json-diff-viewer-side-by-side.md) — diff viewer, side-by-side view (D7, D12) — **built, awaiting verification**
6. [TASK-774](TASK-774-xaml-tag-input.md) — tag input control (D8, D12)
7. [TASK-775](TASK-775-xaml-expander-theme.md) — Birko Expander theme (D10, D12)

TASK-774 and TASK-775 depend on nothing and can run in any order. TASK-768 (the `PasswordChar` defect) came from the same
request but is not part of this story.

## Success criteria

- The C# engine passes every shared vector the web engine passes.
- The desktop tester replaces its placeholder diff with the engine.
- Each control has a Gallery page and passes its Human test plan under all four themes.
