---
id: TASK-101
parent: EPIC-015
feature: null
status: todo  # todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
priority: P2
assignee: ai
created: 2026-07-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Avalonia `Ribbon`: pinned vs temporary-reveal collapse, to match `b-ribbon` and Office

## Context

Found while hand-reviewing TASK-097 in `Birko.Xaml.Gallery`: the Avalonia `Ribbon` has **no pinned
concept at all**, and collapsing it then clicking a tab **permanently re-expands** the ribbon. Neither
matches Office, and neither matches the web `b-ribbon` — which already implements the Office model.

**What Office does.** A collapsed ribbon ("Show Tabs") is not a mode you leave by clicking a tab.
Clicking a tab reveals the ribbon **temporarily, as an overlay over the page content**, and it
auto-collapses again as soon as you invoke a command or click away. Staying expanded is a separate,
explicit act — the pin, `Ctrl+F1`, or a double-click on a tab.

**What Avalonia does today** (`Ribbon.cs:60-64`):

```csharp
tabButton.Click += (_, _) =>
{
    if (index == SelectedIndex) IsCollapsed = !IsCollapsed;
    else { SelectedIndex = index; IsCollapsed = false; }   // ← permanent, and in-flow
};
```

So switching tabs while collapsed is wrong on two counts: the reveal is **permanent** rather than
temporary, and the body is **in flow** (a `DockPanel` child), so it pushes page content down instead of
overlaying it. There is no way to express "I want the ribbon minimised most of the time."

**What the web already does right**, and is the reference for this task:
- `pinned` attribute; unpinned `.ribbon-panel` is `position: absolute; top: 100%` with a shadow — an
  overlay, not a layout participant (`b-ribbon.ts` § Panel Row).
- Hovering a tab flies the panel out (`_expandTimer`, 100ms) and leaving retracts it
  (`_collapseTimer`, 300ms).
- Invoking an item auto-collapses when unpinned:
  `if (!this.boolAttr('pinned')) { this._hoverTabId = null; this.collapse(); }` (`_bindPanelItems`).
- A pin/unpin control in the tab strip, with `aria-pressed`.

So this is a **parity gap in the XAML skin only** — the behaviour is already specified and shipped on
the web side, which makes it a port rather than a design question. Worth doing before TASK-100, since a
`Popup` group flyout inside a temporarily-revealed overlay ribbon is the interaction most likely to
expose ordering/dismissal bugs, and it is easier to get right if the overlay already exists.

Note the Avalonia control is built imperatively in `Rebuild()` and the body is a `DockPanel` child, so
"overlay" means restructuring the chrome (e.g. the groups row in a `Popup`/adorner layer, or the whole
control in a `Panel` with the body z-above the page) rather than flipping a property.

## Acceptance criteria

- [ ] `Ribbon` gains an `IsPinned` styled property (default **true**, so existing consumers are
      unaffected — today's behaviour is effectively "always pinned").
- [ ] When **pinned**, behaviour is exactly as today: the body is in flow, and the collapse chevron
      toggles it.
- [ ] When **unpinned and collapsed**, clicking a tab reveals the body **temporarily as an overlay**
      over the page content — it does not push content down and does not change `IsCollapsed`
      permanently.
- [ ] Invoking an item from a temporarily-revealed ribbon **runs the command and re-collapses**.
- [ ] Clicking away / losing focus re-collapses a temporarily-revealed ribbon.
- [ ] Clicking the **already-active** tab still toggles collapse (today's Office-style behaviour, kept).
- [ ] A pin/unpin control sits in the tab strip alongside the collapse chevron, mirroring `b-ribbon`'s,
      with a tooltip and the correct toggled state.
- [ ] `Ctrl+F1` toggles collapse, matching Office. (Cheap here and it is the shortcut users try.)
- [ ] Overflow from TASK-097 still works in the overlay: the tab strip and groups row keep their
      chevrons and stay reachable while temporarily revealed.
- [ ] Tests in `RibbonTests.cs`: pinned keeps the body in the layout; unpinned + collapsed + tab click
      reveals without clearing `IsCollapsed`; item invoke re-collapses; active-tab click still toggles.
- [ ] `Birko.Xaml.Gallery` demonstrates both modes so the difference is reviewable by hand.

## Out of scope

- **Progressive group scaling / group-to-popup** — STORY-049 (TASK-099, TASK-100). This task is about
  *when the body is shown*, not *how groups are laid out inside it*.
- KeyTips / Alt-accelerators — separate shell-wide keyboard story.
- Any change to `b-ribbon`; the web side is the reference and stays as it is.
- WPF (deferred; its native `Ribbon` provides this).

## Human test plan

- [ ] `Birko.Xaml.Gallery` → Chrome tab. **Pinned** (default): collapse with the chevron, click a
      different tab. Expected: today's behaviour — the ribbon expands and stays expanded.
- [ ] Switch to **unpinned**, collapse, then click a tab. Expected: the body appears *over* the content
      below it (content does not shift down), and the ribbon is still logically collapsed.
- [ ] From that temporarily-revealed state, click a command. Expected: it runs and the ribbon
      re-collapses by itself.
- [ ] Temporarily reveal again, then click somewhere else in the app. Expected: it re-collapses.
- [ ] Press `Ctrl+F1`. Expected: collapse toggles.
- [ ] While temporarily revealed on a **narrow** window: expected the TASK-097 chevrons still appear and
      still scroll — the overlay must not clip or swallow them.
- [ ] Compare side by side with `b-ribbon` unpinned in `Birko.Web.Playground`: the two should now *feel*
      the same. That comparison is the actual point of the task.

## Implementation plan

_Populated by `/tasks plan TASK-101` — leave empty until then._
