---
id: TASK-104
parent: STORY-052
feature: null
status: review
priority: P3
assignee: ai
created: 2026-07-30
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# `b-chart`: axis polish for small charts (tick density, nice scale, latest-value overlay, threshold labels)

## Context

Reported by **Reps** while adopting `b-chart` under its `TASK-088`, and written up in full at
`Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-009-frontend-web-backports/TASK-092-b-chart-mini-chart-polish.md`
— that file holds the original analysis, acceptance criteria and human test plan; this one holds the
framework-side work and is where the fix lives.

`b-chart` is tuned for a full-size canvas and gets busy at 90–150px, which is what Reps' Progress
surface is made of at phone width. Four defects, all general and all in the component:

1. **`yTicks` hard-coded to 5** — written out twice (`_renderBar`, `_renderLinePath`, and a third
   copy in `_renderCanvas`). A 90px chart prints six labels in ~78px.
2. **Tick values not rounded** — the band is split into equal fractions and printed raw, so a steps
   chart reads `0, 2271, 4543, 6814, 9086, 11357`.
3. **The latest-value overlay could only be disabled via `realTime.showLatestValue === false`** —
   opting into a completely different (canvas, windowed) mode to turn off a label.
4. **Threshold label at `x = ml + 4` overlaps the leftmost bars** when they reach the line. Minor and
   pre-existing, same class of problem, same pass.

## Acceptance criteria

- [x] A consumer can ask for fewer y ticks **and** the count adapts to the plot height — a 90px chart
      no longer prints six. `tickIntervalsForHeight(plotPx)` derives it (~one gridline per 50px,
      **capped at 5** so the default 300px chart keeps the axis it always had); `yAxis.ticks` overrides
      it with a target label count.
- [x] Tick values are rounded to friendly numbers by default, with the band extended to the rounded
      bounds — `niceScale()` snaps to 1/2/2.5/5×10ⁿ. `yAxis: { nice: false }` restores the old axis.
- [x] The latest-value overlay switches off without opting into `realTime` — top-level
      `ChartOptions.showLatestValue`. The old spelling still works; the new one wins over it.
- [x] Threshold labels no longer swallow the data at either edge — **paint order** was the real cause
      (see below), plus a background-coloured `paint-order: stroke` halo (SVG) / `strokeText` (canvas)
      and a flip below the line when the line runs along the top of the plot.
- [x] Playground verifier green — 121/121 backport-smoke, 68 components, no empties, no page errors.
      29 of those checks are new: the exported scale maths and the rendered tick/overlay/threshold
      behaviour, plus back-compat guards for the default overlay label and for TASK-093 overlay bars.
- [x] Reps' Progress surface re-checked at phone width in light + dark (the case that decides it) —
      390×844, both themes, no page errors. Weight/waist (90px) read `80.00 / 80.25` and `88 / 89`;
      steps (130px) reads `0 / 5000 / 10000` on a 0–12000 band with `cieľ 10000` legible over the
      bars; no latest-value label duplicating the card captions. **Not covered:** the 150px exercise
      chart — it needs a picked exercise with logged sessions, which the dev data did not have. Same
      code path as the body charts, so it is the one surface left for a by-hand look.

## What landed

`Birko.Web.Components/src/data/b-chart.ts`, plus `data/index.ts` exports, `API.md`, `README.md`,
`CLAUDE.md` (Recent Updates). Playground coverage in `Birko.Web.Playground/src/backport-smoke.ts`.

New exports, deliberately public so the maths is assertable without a DOM (the framework has no
in-tree JS unit runner) and so a consumer can align its own scale to a chart's:
`niceScale(min, max, targetIntervals, { extendMin?, extendMax? }) => AxisScale`,
`tickIntervalsForHeight(plotHeightPx)`, `formatTick(value, decimals)`, `type AxisScale`.

Two design decisions inside `niceScale` that are the reason it behaves, and that any future change
has to preserve:

- **The band is rounded at a fixed density, not the height-derived one.** Tie the two together and a
  short chart pays for its sparse axis in plot area: an 11 357 peak asked for one interval rounds up
  to 20 000 and the bars stop at 57% of an otherwise empty plot. Decoupled, every height shares the
  0–12 000 band and only the labels thin out (7 → 3 → 2) — which also keeps a 90px copy of a chart
  comparable with its 300px one.
- **A bound the caller passed is never extended.** `yAxis: { min, max }` is drawn exactly as given
  and the round ticks land *inside* it. Reps' body charts pass a deliberately tight band; rounding
  79.7–81.8 out to 78–82 shows half the movement the chart exists to show. Only bounds `b-chart`
  derived itself may move.

Step choice is **smallest-that-fits**, not nearest-to-target: "nearest" picks a 10 000 step for an
11 357 peak asked for two intervals — exactly two intervals, three quarters of the plot empty. The
requested count is therefore a target, not a promise; expect ±1.

**The threshold-label defect was paint order, and only the screenshot found it.** The write-up reads as
a placement complaint (`x = ml + 4` is too far left), and the halo shipped first on that reading. The
capture of the real surface showed `cieľ 10000` with a green bar running between the `cie` and the `ľ`:
the label was emitted *with* its line, before `${bars}`, so every bar that reached the threshold painted
over its own label — and over the halo with it. Lines and labels now come back from `_thresholdSvg`
**separately** and sit on opposite sides of the data: the line behind (it is a level the series is
measured against), the label in front (it is chrome and has to stay readable). The canvas renderer had
the same ordering bug and defers its labels the same way. Two smoke checks assert the DOM order, because
nothing else would catch a regression that is invisible until a bar happens to be tall enough.

The `x = ml + 4` placement is unchanged and does not need to change — see the deviation note.

Two things fixed in passing, both found while touching this code:

- **Bar charts now include threshold values in their auto range**, as the line renderer already did.
  A goal line above every bar was previously drawn at a negative `y`, and the SVG is
  `overflow: visible` — so it did not clip, it painted on the card above the chart.
- **Threshold `label` / `color` are now escaped**, matching the legend. They are caller data landing
  in a text node and a `fill="…"` attribute.

## Deviations from the original write-up

Called out because a future reader will otherwise take the Reps file as the spec:

- **The nice-scale example does not reproduce, by design.** TASK-092 predicts `0, 3000, 6000, 9000,
  12000` for an 11 357 peak, but `3000` is not in the 1/2/5×10ⁿ family the same sentence asks for.
  The implemented ladder is 1/2/2.5/5, so a full-size chart reads `0, 2000, …, 12000` — same shape,
  round numbers, and internally consistent.
- **Threshold labels are made legible over the data, not moved off it.** There is no gutter wide
  enough for arbitrary threshold text ("cieľ 10000" is ~64px) without stealing that width from every
  chart that has a threshold. Real charting libraries halo plot-line labels rather than reserve a
  margin for them, and so does this. With the paint order corrected the label reads cleanly over the
  bars in both themes, which is what the report was actually asking for. If a future consumer needs
  true geometric separation, the honest fix is an opt-in `thresholdGutter` — not a default that
  narrows every plot.

## Backwards compatibility

Default behaviour changes for **every** chart, deliberately — rounded tick values and an auto band
extended to the rounded bound are the ticket, and cannot be opt-in without the defect staying the
default. `yAxis: { nice: false }` restores the raw equal-split axis.

Held constant on purpose: the tick-count cap of 5 keeps the default 300px chart at the density it
always had; the latest-value overlay still defaults **on**, so no existing chart silently loses its
label; overlay bars (TASK-093) and per-point colour (TASK-088) are unchanged and both are guarded by
new smoke checks.

## Cross-links

- Origin: `Consumers/WorkoutTracker/tasks/EPIC-002-birko-backports/STORY-009-frontend-web-backports/TASK-092-b-chart-mini-chart-polish.md`
- Consumer adoption that found it: Reps `TASK-088` (b-chart swap), `TASK-093` (overlay bars)
