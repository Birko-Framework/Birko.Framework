---
id: TASK-672
parent: EPIC-015
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
priority: P2
assignee: unassigned
created: 2026-10-19
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# Promote BardStudio's knob, level meter and fader themes into Birko.Xaml.Avalonia

> **Filed 2026-10-19 from BardStudio.** BardStudio asked for this on 2026-07-14 in
> `C:\Source\Birko\Consumers\BardStudio\docs\framework-backports-prompt.md` Part 2. Its Part 1 bug fixes landed
> that day; Part 2 was never filed here, so the framework's pick never saw it. BardStudio TASK-005 ("Adopt promoted
> Birko.Xaml controls") is the consumer-side half and is blocked on this task.

## Context

Checked 2026-10-19: `Birko.Xaml.Avalonia` has no `BKnob`, no `BLevelMeter` and no fader slider themes. BardStudio
carries them locally in `src\BardStudio.UI\`:

| Local source | Promote as |
|--------------|------------|
| `Controls\KnobControl.axaml(.cs)` | `BKnob` |
| `Controls\VuMeterControl.axaml(.cs)` | `BLevelMeter` |
| `Styles\DjHardware.axaml` fader ControlThemes | `BSliderFader`, `BSliderCrossfader` in `Controls\Inputs.axaml` |

Copy and adapt; never reference the consumer. BardStudio stays untouched here; switching it over is its TASK-005.

## The request, per control

Rules for all three: token-driven (`{DynamicResource B*}`, no hard-coded hex), registered in the right category
file per `Birko.Xaml.Avalonia/CLAUDE.md`, and a Gallery section that shows each across the theme variants. Keep the
web `b-*` naming parity where it makes sense.

- **`BKnob`**: rotary value input with a 270° sweep starting at 7 o'clock.
  - Styled properties `Value` (TwoWay), `Minimum`, `Maximum`, `DefaultValue`.
  - Vertical drag adjusts the value. The mouse wheel takes fine steps (range / 40).
  - Double-click resets to `DefaultValue`. Detect it with `ClickCount == 2` in `PointerPressed`, not the
    `DoubleTapped` event: a handled `PointerPressed` suppresses that event.
  - `PointerCaptureLost` clears the drag flag. Constant brushes and pens are cached as static fields.
  - Replace the local `AccentColor` with token brushes (default `BColorPrimaryBrush`), keeping an override.
  - Consider an `IsCentered` look for symmetric ranges.
  - A plain `Control` subclass, with no UserControl/axaml pair.
- **`BLevelMeter`**: a segmented LED level meter with peak hold.
  - `Level` and `PeakLevel` in [0..1], plus `SegmentCount`.
  - Its green, yellow and red zones map to the `BColorSuccess`, `BColorWarning` and `BColorDanger` tokens.
  - Orientation, if it is cheap.
  - Also useful for telemetry and dashboards, not only audio.
- **`BSliderFader` / `BSliderCrossfader`**: named ControlThemes.
  - The fader is a vertical slot with a rectangular cap, a grip line and no fill. The crossfader is the horizontal
    form with a centre detent mark.
  - Build on the existing `BSliderRepeat` / `BSliderThumb` scaffolding in `Inputs.axaml` instead of duplicating it.
    Tokenize the slot and cap colours.
  - Where feasible, use one thumb theme parameterized by orientation. BardStudio has two near-identical copies.

## Acceptance criteria

- [ ] `BKnob`, `BLevelMeter`, `BSliderFader` and `BSliderCrossfader` exist in `Birko.Xaml.Avalonia`, token-driven,
      with no hard-coded colours.
- [ ] Tests for the knob's value logic: clamping to the range, wheel step, double-click reset and the drag state
      cleared on capture loss. Also tests for the meter's zone and peak mapping.
- [ ] The Gallery shows all four in every theme variant.
- [ ] The accent override on `BKnob` keeps BardStudio's per-deck colours possible.
- [ ] Project `CLAUDE.md` / README and `docs/` list the new controls.
- [ ] BardStudio TASK-005 is told the controls landed (unblock it there).

## Out of scope

- `TurntableControl` and `WaveformControl`. They stay app-specific.
- Changing BardStudio. The adoption is its TASK-005, and it may keep its hard-coded fader colours on purpose.

## Human test plan

- [ ] In the Gallery, drag, wheel and double-click-reset a knob. Animate a meter. Move both faders in every theme
      variant.

## Implementation plan

_Populated when picked up._
