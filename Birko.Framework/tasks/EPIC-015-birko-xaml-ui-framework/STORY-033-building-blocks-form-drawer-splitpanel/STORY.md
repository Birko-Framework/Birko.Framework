---
id: STORY-033
parent: EPIC-015
status: done
created: 2026-06-25
closed: 2026-07-04
---

## Resolution (2026-07-04)

The three building blocks the page layer composes, all token-driven:
- **`Form`** (`Birko.Xaml.Avalonia/Controls/Form.cs`) — schema-driven: binds `Fields`
  (`Birko.Xaml.Core.Forms.FormField[]`) + `Model`, generating labeled two-way inputs
  (Text/TextArea/Number→TextBox, Checkbox→CheckBox, Select→ComboBox) with a required asterisk.
  Pairs with `CrudViewModelBase.EditingItem` / `DetailPageViewModel.Model` — keeps the page bases
  declarative. Verified in the gallery (renders + re-themes, incl. finstat flat corners).
- **`Drawer`** — slide-in overlay (`IsOpen`/`Placement`, backdrop-click closes), token width/bg.
- **`SplitPanel`** — master/detail over `GridSplitter` with responsive `:collapsed` below `CollapseWidth`.

**FormField schema lives in Core** (platform-neutral); the `Form` **control** in `.Avalonia` — same
Core-logic / Avalonia-view split as i18n. **8 headless tests** (Form generate/bind/two-way/required,
Drawer toggle, SplitPanel wide/narrow) + per-theme screenshots. Avalonia suite now 31.

**Deferred:** DataAnnotations-driven validation (only the required asterisk for now); Drawer slide
animation; `GridSplitter` drag precision (pixel master column works, fine for the gate).

# Building blocks — schema-driven Form, Drawer, SplitPanel

## User story

As an app developer, I want the three controls the page layer composes (a schema-driven form,
a drawer, and a responsive split panel), so the CRUD page bases stay drop-in and declarative.

## Behaviour

- **`Form`** — schema-driven form control (the XAML port of `b-form`): a bound field schema generates labeled, validated inputs. This keeps `CrudViewModelBase` *declarative* (`formSchema`) instead of forcing hand-rolled XAML forms per screen — it is therefore a hard dependency of the whole page layer, not an optional widget.
- **`Drawer`** — slide-in panel; built on Avalonia `SplitView` in overlay display mode, skinned to match `b-drawer`.
- **`SplitPanel`** — master-detail layout over `GridSplitter` with responsive collapse (the `b-split-panel` behavior `BaseSplitPage` needs).
- All three consume tokens only (no hard-coded values) and visually match their `b-*` counterparts across themes.
- **Dependency note:** these three land before the page bases (STORY-036) and alongside / after the bulk of Tier-1 (STORY-034), mirroring Birko.Web's shell→components dependency.
