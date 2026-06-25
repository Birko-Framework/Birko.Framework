---
id: STORY-033
parent: EPIC-015
status: planned
created: 2026-06-25
---

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
