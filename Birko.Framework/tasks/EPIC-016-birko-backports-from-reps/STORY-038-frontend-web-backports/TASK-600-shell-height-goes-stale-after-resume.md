---
id: TASK-600
parent: STORY-038
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: todo
# blocked: <reason> — add this line while the task is blocked, keeping its status; /tasks unblock removes it
priority: P1
assignee: unassigned
created: 2026-10-14
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: [FIELD-038]
pr: null
github-issue: null
jira-key: null
---

# The installed app's bottom nav vanishes after a resume — Chrome leaves the shell's `dvh` height stale

> **Moved from WorkoutTracker TASK-130 on 2026-10-14.** It was filed there as Birko.Web work, where the framework's pick never saw it. Every TASK / STORY / FEATURE id in the body below is **WorkoutTracker's**. It depended on WorkoutTracker TASK-148 (iOS rotation leaves a gap above the top bar, in progress there) and blocks WorkoutTracker TASK-045 (adopt the mobile shell). There, TASK-130 stays open as the Reps-side check, blocked on this task.


## Android symptom does not reproduce — device pass, 2026-09-07

Re-measured on the **Xiaomi Mi A2 Lite** (Android 10, Chrome 150, **3-button** nav — the harsher case and the
one the original reading used), driven over USB (`adb` + CDP), on the shell **exactly as committed**: this task
reverted, `hostInlineHeight` reading **`(none)`**, so nothing is writing the shell's height.

Seven readings — baseline, three home+resume cycles, three pull-to-refresh reloads — all identical:
`navBottom − visualViewport.height` = **0.000**, `maxScroll` = **0**, 4/4 nav items inside the viewport. The
reloads were proved (sentinel cleared, `performance.timeOrigin` changed, `navigation.type === 'reload'`) rather
than assumed.

**Confirmed by a human finger, 2026-09-08.** The owner ran pull-to-refresh by hand in the installed app —
*"pull to refresh seem to be working"* — with the nav in place afterwards, and separately confirmed on the panel
that the bottom nav is fully visible above the 3-button bar on a clean launch. TASK-045 step 8 is closed on that
basis.

**What this does and does not mean.** It does *not* close this task — the iOS half is untouched. What it settles
is that the **Android** symptom does not appear on the shipped shell, by instrument or by finger.

**A caveat on the instrumented half, worth carrying:** the CDP readings were taken with Playwright attached to
the device's Chrome, and attaching **changes what the device renders** — it forced `prefers-color-scheme: light`
on a device in dark mode, and a screencap taken while attached showed the nav *entirely absent*, which was
briefly written up as a defect before a clean `force-stop` capture disproved it. Numbers from CDP describe the
computed layout; only an unattached screencap describes the panel. See the method warning on TASK-045. What it does mean is that the Android half of this defect could not
be made to appear on the untouched shell across seven attempts, which is the mirror image of **TASK-148**: there,
a symptom blamed on this task's fixes turned out to be present without them; here, a symptom this task is meant
to fix is absent without it. Both readings point the same way — **the attribution of the four reverted attempts
is unsafe**, and any fifth attempt should be judged against measured baselines on both engines rather than
against a remembered symptom.

Note the iOS half is untouched by this: TASK-148's landscape gap **did** reproduce on the iPhone the same day.

## Context

Found on the **Android device pass, 2026-08-03** (TASK-045's walk, step 8), Xiaomi Mi A2 Lite / Android 10 /
Chrome 150, in the **installed WebAPK**. This is the defect the original report described — *"the bottom tabs
are hidden below the system Android 3 buttons"* — and it is **not** the same bug as TASK-129, which affected a
browser tab. Both were real; they are two different failures that produce the same visible symptom.

**P1, not P2:** the casualty is the app's *primary navigation*. All four surfaces become unreachable, and
nothing on screen explains why. A user's only recourse is to guess that force-quitting and relaunching fixes it.

## Reproduction, measured on the device

The app is launched, correct, and clean. The screen sleeps and wakes (a phone doing what phones do). The bottom
nav is then **gone**, and the page has 56px of scroll it should not have. Measured over adb-forwarded CDP:

| measure | on screen |
|---|---|
| `innerHeight` | 597 |
| `100dvh` **unit** resolves to | **597.63 — correct** |
| body computed `min-height` | **653.63px — stale** |
| shell computed height | **653.63 — stale** |
| bottom-nav bottom edge | 653.63 (i.e. 56px below the visible viewport) |
| nav visible | **false** |
| `scrollHeight − clientHeight` | 56 |

`653.63 = 597.63 + 56` — the viewport height *including* the system navigation-bar strip. So the page was laid
out at some moment when Chrome reported the taller, edge-to-edge viewport, and when the nav bar came back the
lengths derived from `dvh` were **never recomputed**.

## Root cause — SUPERSEDED, see "ATTEMPT 4" below

> **This whole section was overturned on 2026-08-03 by the measurements in
> [ATTEMPT 4](#attempt-4-2026-08-03--fixed-and-verified-on-android-awaiting-the-iphone-check), correction 1.**
> Its conclusion — *"`dvh` is not stale, it is wrong"* — is **wrong**, and the diagnosis it replaced (a missed
> style invalidation) was right all along. A fresh element resolving `100dvh` at 600ms reads the correct 597.63
> while the shell is still 653.63; the "dvh is wrong" reading came from a probe created at 143ms and therefore
> measured a cached resolution rather than the engine's current answer. Kept unedited below because three fix
> attempts were built on it and the order of the evidence is the point — but do not plan from it.

### Original text (retained, not current)

**This section was rewritten on 2026-08-03 after the first diagnosis turned out to be wrong.** It said "a stale
style recalculation, not a wrong value". That was a reasonable reading of the first evidence and it does not
survive the second. The corrected account, and the evidence order that produced it, is kept here deliberately —
two fix attempts were built on the wrong version and both had to be reverted.

**What looked true first.** Forcing a style/layout recalculation snapped everything back:

```
before:  dvh probe = 597.63   body min-height = 653.63px   shell = 653.63   navVisible = false   maxScroll = 56
after:   dvh probe = 597.63   body min-height = 597.63px   shell = 597.63   navVisible = true    maxScroll = 0
```

The probe read the *correct* 597.63 while computed styles were stuck at 653.63 — which reads exactly like a
missed invalidation, so that is what it was recorded as.

**What is actually true.** Injecting the recorder with `Page.addScriptToEvaluateOnNewDocument` — the only way to
observe the first frames, since anything evaluated into a loaded page arrives after the interesting part — gives
this timeline in the installed WebAPK, screen on and app focused:

| t (ms) | event | `innerHeight` | `visualViewport.height` | `clientHeight` | **`100dvh` probe** |
|---|---|---|---|---|---|
| 82 | script-injected | 1830 | 597.63 | — | — |
| 120 | vv.resize | 597 | 597.63 | 597 | **653.63** |
| 126 | window.resize | 597 | 597.63 | 597 | **653.63** |
| 217 | DOMContentLoaded | 597 | 597.63 | 597 | **653.63** |
| 220 | load | 597 | 597.63 | 597 | **653.63** |
| 226 | RO:html | 597 | 597.63 | 597 | **653.63** |
| …3s of dense polling, no change… | | 597 | 597.63 | 597 | **653.63** |

`dvh` is **not stale — it is wrong**. From the first measurable frame, every other source agrees the viewport is
597, and only `dvh` claims 653.63: in a WebAPK Chrome resolves it against the edge-to-edge viewport, ignoring the
system navigation-bar inset. Somewhere after the 3-second mark it silently starts resolving to 597.63, announced
by **no event** — and since nothing invalidates the styles computed at load, the layout keeps 653.63. That is why
a later probe reads the right number next to a wrong layout, and why a forced reflow "fixes" it.

Both symptoms now have one explanation, and the important consequence is that **`dvh` cannot be the input to its
own fix.**

### It is the RELOAD path, deterministically — not a race, and not a resume

**Corrected again on 2026-08-03**, by a hands-on tally that beat the inference below. frantisek ran it on the
device: pull-to-refresh → nav hidden; force-quit + relaunch → nav correct; pull-to-refresh again → hidden again.
**Reload 2/2 broken, cold launch 1/1 correct.**

So the trigger is not luck, it is the **reload path**: a reload inside a live WebAPK computes the page's styles
before Chrome has applied the navigation-bar inset, so `dvh` resolves to the edge-to-edge 653.63 and stays there
for the life of the document. A fresh activity start gets the inset first and comes up correct. This also
explains the session's apparent inconsistency without appealing to chance — the loads at 07:39 and 07:44 were
CDP `Page.reload`s (broken), while 07:37 was a resume that started a fresh activity (correct).

Two practical gains: the defect is **reproducible on demand** (pull to refresh, or `Page.reload` over CDP, so a
fix can be verified without a human at the phone), and the user-facing exposure is sharper than "any load" —
it is *refresh*, plus anything else that reloads in place. Which is still bad: pull-to-refresh is the gesture a
user reaches for when the screen looks wrong, so the recovery action is the trigger.

_The race reading below was the previous hypothesis, kept because it shaped the "repeat five times" test plan —
which is still the right shape, since a fix must be shown to survive repeated reloads._

### Why it looked like a race

Reported by frantisek during this session: *"when I refreshed the app by pulling it down, the bottom tabs fall
again under the Android bottom bar."* That is the reproduction this task needed, and it reframes the trigger.

Not every load hits it. The load at 07:37 came up correctly (shell 597.63, nav visible); the loads at 07:39:55
and 07:44:33 came up at 653.63. Same app, same device, same build. So the page's first style computation **races**
Chrome applying the system-bar inset: win the race and `dvh` is 653.63 and the layout is wrong for as long as the
document lives; lose it and everything is fine. Nothing about the app's own behaviour changes the odds.

Two consequences worth stating:

- **Severity up, not down.** "After a resume" made it sound like an edge case. Any load can hit it — including a
  cold launch from the home screen, which is how the app is normally started — and pull-to-refresh, the one
  gesture a user reaches for when something looks wrong, re-rolls the dice and can produce it. The user's
  recovery action is also its trigger.
- **A pass/fail on one launch proves nothing.** Any verification of the fix has to repeat the load several times,
  and a *pre-fix* run must be seen failing at least once on the same rig, or a lucky launch reads as a fix.

Also note from the timeline: at t=82ms `innerHeight` was still a nonsense 1830 while `visualViewport.height`
already read the correct 597.63. Of the candidate sources, `visualViewport.height` is correct **earliest**, which
matters for a race — at the cost of being the one that shrinks with the keyboard.

## Rejected fix attempts (both reverted 2026-08-03)

1. **Publish `window.innerHeight` as a CSS variable and size the shell from it.** Sized the shell correctly, but
   left `body { min-height: 100dvh }` at 653.63, so 56px of dead scroll remained. Worse, `innerHeight` is not a
   safe source everywhere: where it reports the *large* viewport it would reintroduce the
   nav-behind-the-toolbar bug `dvh` was introduced to fix. Trading an Android bug for an iOS one is not a fix.
2. **Publish a freshly-probed `100dvh` instead**, on the theory that a fresh probe re-resolves correctly. It
   does — *after* the silent correction. At mount it faithfully reproduces 653.63, which is the bug. Verified on
   the device: `publishedVar: 653.63` on a live, focused, screen-on page.

An early measurement that appeared to support attempt 2 came from a **hidden zombie page** — a resumed WebAPK
reloads and leaves the previous document alive as an inspectable target, and that one had mounted while the
screen was off, against a genuinely edge-to-edge viewport. Any future measurement here must confirm
`document.hasFocus() && visibilityState === 'visible'` first; `scratchpad/live.mjs` does this.

## Acceptance criteria

- [ ] The shell's height comes from a source that is **correct at load** on this platform. From the timeline,
      `visualViewport.height`, `innerHeight` and `documentElement.clientHeight` all qualify on Android;
      `100dvh` does not.
- [ ] The chosen source is **also correct on iOS**, where `dvh` is right and `clientHeight`/`innerHeight` may
      report the large viewport. A `min()` of the candidates is the obvious shape; whatever is chosen, the
      reasoning goes in the code, because the next reader will otherwise "simplify" it back to `dvh`.
- [ ] **The keyboard case is decided explicitly, not by accident.** `visualViewport.height` shrinks when the
      on-screen keyboard opens; sizing the shell from it moves the bottom nav up above the keyboard. That may be
      wanted or unwanted, but it is a UX decision — Reps has number inputs on the session screen — and it must be
      stated and tested rather than discovered later.
- [ ] `body { min-height }` in `reset.css` ends up agreeing with the shell, or the dead scroll survives the fix.
- [ ] Re-verified on the device **at load** (not after several seconds): `navBottom == innerHeight`,
      `maxScroll == 0`, on a page confirmed focused and visible.
- [ ] Re-verified on the **iPhone**, which has a signed-off baseline (2026-07-06 / 2026-07-31). A regression
      there would be the worst outcome of this exercise. This also covers TASK-129's `dvh` body change.
- [ ] `b-drawer.ts:26` (`max-height: 100vh`) assessed in the same pass — same family. Fix or record why not.
- [ ] Symbio's shell checked before this is called done — it consumes the same base.

## Provenance — this qualifies TASK-049 rather than contradicting it

The shell's `100dvh` came from **TASK-049** ("Shell `100dvh` viewport height"), and that task was right: `vh` is
the large viewport and a `vh`-sized shell puts a fixed bottom nav behind a mobile browser's toolbar. Nothing
here reverses it — `dvh` remains the CSS fallback and is still the correct *intent*. What TASK-049 could not
have known is that one platform over-reports `dvh` itself, so the intent needs a measured floor under it. Read
the two together: TASK-049 established which viewport the shell should track, this task establishes that the
engine's own answer needs checking against a second source.

## The framework has already learned this lesson — in one component, not in the shell

`Birko.Web.Components/src/layout/b-split-panel.ts:33-39` measures its scrollport in JS
(`--_scrollport-h`) and says so explicitly: *"measured in JS (see `_trackScrollport`) rather than written as
`100dvh`"*. `b-core-app-shell.ts:395-397` — the surface every mobile consumer's chrome hangs off — still trusts
`100dvh` directly. The correct fix is to apply the split-panel's approach one level up.

## Acceptance criteria

- [ ] `BCoreAppShell` sizes itself from a **measured** viewport height (a CSS custom property set from
      `visualViewport.height`, falling back to `innerHeight`), refreshed on `visualViewport` `resize`/`scroll`,
      `window.resize`, `pageshow`, and `visibilitychange` — the last is the one that catches this bug, since a
      resume fires it.
- [ ] `100dvh` is retained as the **CSS fallback** for the first paint and for anything without JS/visualViewport;
      the measured value only overrides once available. No flash of a wrongly-sized shell on cold start.
- [ ] Re-verified **on the device**: sleep/wake the installed app and the nav is still visible, with
      `navBottom == innerHeight` and `maxScroll == 0` — no reload, no force-quit.
- [ ] The browser-tab case (TASK-129) stays fixed, and the URL-bar show/hide transition still resizes cleanly.
- [ ] `b-drawer.ts:26` (`max-height: 100vh`) is assessed in the same pass — same family, and `vh` there is the
      *large* viewport, so a full-height drawer can already overflow on mobile. Fix or record why not.
- [ ] Symbio's shell is checked for the same regression before this is called done — it consumes the same base.

## Out of scope

- A general audit of viewport units across the framework. TASK-129 records the same boundary; this task fixes the
  shell and names `b-drawer` as the one other known instance.

## Human test plan

- [ ] **Installed WebAPK, pull-to-refresh ×5:** pull down to refresh five times, checking after each that all
      four nav items are visible and tappable and the page does not scroll. Five because the trigger is a race —
      one clean launch proves nothing (see above).
- [ ] **Installed WebAPK, cold launch ×5:** force-quit from Recents and relaunch from the icon, same checks.
- [ ] **Installed WebAPK, sleep/wake:** lock the phone, unlock it. Same checks.
- [ ] **Installed WebAPK, app-switch:** background via Recents, open something else, return. Same checks.
- [ ] **Keyboard, whatever is decided:** focus the weight input on the session screen and confirm the bottom nav
      behaves as the decision says it should — this is the check that catches a `visualViewport`-sourced fix
      moving the nav up over the keyboard.
- [ ] **Browser tab:** URL bar shows and hides while scrolling; nav stays put; no dead scroll (TASK-129's case).

## Testability

Same limit as TASK-129 and the safe-area insets: **not reproducible headlessly.** The trigger is a real
platform viewport change on a resume; Playwright cannot sleep a phone, and with no browser chrome
`vh == dvh == innerHeight`, so the stale-recalculation window never opens. Device-only human coverage.

What *can* be guarded automatically is the mechanism the fix introduces rather than the bug it prevents: a
Playground smoke asserting the shell publishes a measured height variable and updates it on a synthetic
`visualViewport` resize. That is worth having, and it is **not** a reproduction of this defect — it must not be
recorded as one.

## REVERTED (2026-08-03) — still open, and the measured-height shape is now ruled out

**Three attempts, all reverted. The defect stands.** Everything below the line documents what was built and
why each version failed; the framework is back to its committed state (`b-core-app-shell.ts` untouched,
`reset.css` keeping only TASK-129's `vh → dvh` line).

**Why reverting was the right call rather than a fourth attempt.** Each version fixed Android and broke the
iPhone: rotation left the shell sized to the portrait height, so the top bar had white space above it and the
bottom nav ran off the screen. iOS is the platform with a **signed-off baseline** (2026-07-06 / 2026-07-31) and
Android's defect, though P1, has never been signed off. Trading a verified platform's correctness for an
unverified one's is the wrong direction, and the third failure was the point to stop iterating on the owner's
phone rather than the point to try a fourth timing.

**What is now ruled out, with reasons — this is the value the three attempts bought:**

| Source | Ruled out because |
|---|---|
| `window.innerHeight` | May report the *large* viewport, reintroducing the nav-behind-the-toolbar bug `dvh` exists to fix. |
| freshly-probed `100dvh` | Faithfully reproduces the over-reported 653.63 at mount; the bug cannot be its own input. |
| `visualViewport.height` | Shrinks with the on-screen keyboard; would jump the nav up over it. |
| `min(innerHeight, dvh)` + timers (rAF/300ms, then 100/300/600/1000ms) | iOS reports **pre-rotation metrics for longer than any window tried**, so the published px stayed at the portrait value. |
| the same + `orientationchange` + `visualViewport.scroll` + a `ResizeObserver` on `documentElement` | Still failed on iPhone rotation. Observation-based rather than timing-based, and *still* not enough — which is the strongest evidence that the shape itself is wrong, not its tuning. |

**The generalisable lesson, recorded because it will outlive this task:** substituting a JS-measured pixel value
for a live CSS viewport unit **moves responsibility for tracking the viewport from the engine to us**, and then
every platform transition has to be enumerated correctly — load, reload, resume, keyboard, rotation, and
whatever the next OS adds. Android needed exactly that takeover; iOS punished it. A measured height is not a
drop-in for `dvh`.

## ATTEMPT 4 (2026-08-03) — fixed and verified on Android, awaiting the iPhone check

**The prescribed shape worked: keep `100dvh`, force a re-resolution, gate it on a condition iOS cannot meet.**
`BCoreAppShell` now runs `_reassertViewportHeight` on mount and on `pageshow` / `resize` / `orientationchange` /
`visibilitychange`, across a settle window (rAF, then 100/300/600/1200/2500/4000ms). Each pass measures the shell
and **does nothing unless it is taller than both `innerHeight` and `documentElement.clientHeight`**; when it is, it
re-asserts the shell's `height` and the body's `min-height` as `dvh`.

### Two corrections to this task's own diagnosis, both measured

**1. `dvh` is not wrong — it is stale. The FIRST diagnosis was right and the "correction" above was the wrong
turn.** The section headed *"`dvh` is **not stale — it is wrong**"* does not survive re-measurement. At 600ms after
an in-place reload, a **freshly created element** resolving `height: 100dvh` reads **597.63 — correct** — while the
shell's computed height is still 653.63. Both numbers, same document, same frame:

| at ~600ms after reload | value |
|---|---|
| fresh element resolving `100dvh` | **597.63 (correct)** |
| shell computed height | **653.63 (stale)** |
| `innerHeight` / `visualViewport` / `clientHeight` | 597 / 597.63 / 597 |

That is a missed style invalidation, not a wrong unit — which is *why* re-asserting the same declaration fixes it,
and why the earlier conclusion that "the bug cannot be its own input" was mistaken. It was drawn from a probe
created at **143ms**, which does read 653.63; the value a probe returns depends on when the first one is created,
so the 3-second timeline recorded above measured the cache rather than the engine's current answer.

**2. Writing the same value to CSSOM is a no-op, and the first version of this fix failed silently because of
it.** Version 1 assigned `style.height = '100dvh'` on every pass. The earliest pass landed while `dvh` still
resolved to 653.63, so that declaration computed 653.63 — and every later pass assigned the **identical string**,
which the engine treats as no change and does not recompute. Traced on the device:

```
t=305ms   shell 653.63   inline "100dvh"   nav hidden   maxScroll 56     <-- fix had "fired"
...no change for the next 8 seconds...
```

**This is a worse failure than no fix at all, because it reads as fixed**: the inline declaration sits there
looking exactly like the working version. The shipped code therefore **alternates** between `100dvh` and
`calc(100dvh)` — identical computed result, guaranteed real transition on every pass.

### The fix converging, traced on the device

```
 t=144ms   shell 0          inline -               (not laid out yet)
 t=307ms   shell 653.63     inline calc(100dvh)    nav hidden    maxScroll 56
 t=477ms   shell 597.63     inline 100dvh          nav VISIBLE   maxScroll 0
```

**Exposure is ~477ms**, then stable — the guard early-returns once healthy, so there is no thrash. That number is
the reason waiting for Chrome's own correction was rejected: measured with zero page interaction, `dvh` did not
self-correct for the shell until **somewhere between 15s and 28s**, so a fix built on the correction would leave
the app's primary navigation unreachable for up to half a minute.

### Why this cannot regress iOS — by construction, not by testing

- **The value stays `dvh`.** The engine keeps owning viewport tracking. This is the whole lesson of the three
  reverted attempts: a measured pixel value moves that responsibility to us and then every platform transition
  has to be enumerated correctly.
- **The guard is unsatisfiable where `dvh` is the small viewport.** It fires only when the shell is *taller* than
  both `innerHeight` and `clientHeight`. On iOS `dvh` ≤ both, so the guard is false and this code does nothing.
  At worst it is inert — the inverse of the reverted attempts, which changed behaviour everywhere.
- **The keyboard case is therefore decided rather than discovered** (the third acceptance criterion): behaviour is
  *exactly* stock `dvh`, because no measured value ever participates. Nothing can move the nav up over the
  keyboard, since there is no px height to shrink. Still listed for the device walk below — decided is not tested.

### One more property, easy to "clean up" and thereby break

The inline declaration **must be left in place**. Withdrawing it after forcing the reflow was measured to revert
to the stale 653.63 (`guardedDvhThenClear`: final shell 653.63, still broken). The cost is that an inline height
outranks a consumer stylesheet targeting the host — accepted, and it is only ever set on a device exhibiting the
pathology.

### Verified on the device (Mi A2 Lite, Android 10, Chrome 150, installed WebAPK)

Every case measured over adb-forwarded CDP, on a target confirmed visible, with the **real shipped build** — not
an injected snippet. The defect was re-confirmed present immediately beforehand (3/3 reloads broken, the exact
653.63 / navBottom 653.63 vs 597 / maxScroll 56 table above), so none of these is a lucky launch:

| case | result |
|---|---|
| in-place reload ×5 (the deterministic repro) | **5/5 OK** |
| cold launch ×3 (force-stop + relaunch) | **3/3 OK** |
| sleep / wake | **OK** |
| app-switch (Home → Settings → back) | **OK** |
| browser tab, TASK-129's case | **OK** (541.63, maxScroll 0) |

### Attempt 4 REVERTED (2026-08-03) — for a real flaw, but NOT the one that was expected

**The framework is back to committed state** (`b-core-app-shell.ts` untouched). Reverted on one measurement from
the iPhone: the readout showed **`inline 100dvh`**, and nothing but `_reassertViewportHeight` writes
`shell.style.height`. So the guard **fires on iOS**, and the central claim of the design — *"unsatisfiable where
`dvh` is the small viewport"* — is false.

**Why it is false, and it is obvious in hindsight.** The guard asks "is the shell taller than the reported
viewport?". During a rotation iOS reports the **new** orientation's `innerHeight` while the shell is still laid out
at the **old** one: portrait 762 against landscape ~390 clears the threshold trivially. A transient metric
mismatch is not a pathology, and the guard cannot tell the two apart. So the fix reacted on every rotation on the
one platform it was designed to be inert on.

**But it did not cause the gap, and that is the important half.** With the fix reverted and `inline -` confirming
nothing writes the height, **the landscape gap is still there**. It is a pre-existing iOS defect, now
**TASK-148**, and TASK-045's step i8 has been un-ticked because it was ticked from an argument rather than a walk.

**Which reopens the reading of attempts 1–3.** All three were reverted on the grounds that each "broke iPhone
rotation". The baseline they were compared against was never measured, and now that it has been, it shows the same
symptom. Those attempts may have been discarded for a symptom they did not cause. That does **not** make the
measured-height shape correct — it remains ruled out on its own merits (a measured px moves viewport tracking from
the engine to us) — but the *evidence* that condemned it was weaker than this task recorded.

**What survives from attempt 4, and it is most of it.** The Android diagnosis and fix are measured and sound:
the stale-invalidation root cause (correction 1), the CSSOM no-op trap (correction 2), the ~477ms convergence, and
5/5 reloads + 3/3 cold launches + sleep/wake + app-switch + the browser tab, all with the real build. Only the
**guard condition** is wrong, and it is wrong in a specific, fixable way.

### The shape to try on the next attempt

Keep everything except the trigger. The Android pathology is **permanent** — the shell stays 56px too tall for the
life of the document — while an iOS rotation mismatch is **transient**, settling well within a second. So require
**stability** rather than mere disagreement: act only when the shell has been taller than the reported viewport
across two samples separated by, say, 400–600ms, and skip entirely for a cool-down after `orientationchange` or
any dimension change. Android's exposure grows from ~477ms to ~1s (against 15–28s for Chrome's own correction, or
forever without a fix); iOS settles long before the second sample, so the guard never fires there.

**Do not attempt that until TASK-148 is understood.** With a pre-existing rotation defect on the same device, any
iPhone verification of a TASK-130 fix cannot distinguish "my fix broke rotation" from "rotation was already
broken" — which is precisely the confusion that cost the first three attempts.

### Outstanding before `done`

- [ ] **A fifth attempt using the stability-gated trigger above** — but only after TASK-148, for the reason given.
- [ ] ~~**iPhone check — the one gate left.**~~ **Run 2026-08-03: the guard fired on iOS, so attempt 4 is
      reverted.** Superseded by the two items above; the original wording follows. Rotate to
      landscape and back (TASK-045 step i8), confirm no gap above the top bar and no nav off-screen, plus the
      keyboard case. The argument above says the guard cannot fire there; this task exists because arguments are
      not checks.
- [ ] **Keyboard on Android** — focus a number input and confirm the nav does not move. Needs a real tap to raise
      the on-screen keyboard, so it is a human step rather than an automated one.
- [ ] Playground `backport-smoke.ts` — assert the *mechanism* (the shell re-asserts a dvh declaration when its
      height exceeds the reported viewport), never the defect. Not reproducible headlessly: `vh == dvh ==
      innerHeight` with no browser chrome.
- [ ] Symbio pass — it consumes the same base shell.

_(Superseded planning below. The "shape to try next" it prescribed is what attempt 4 implemented, so it was
right; the diagnosis it rested on was not, per correction 1 above.)_

### The shape to try next (do not re-try the one above)

Keep `height: 100dvh` — leave the engine owning viewport tracking, since it is right everywhere except one
Android case — and **force a style re-invalidation** on the transitions where Chrome fails to. The forced-reflow
experiment already proved this works on Android: toggling `documentElement.style.display` snapped every stale
length back and the nav reappeared. What is needed is the *cheapest invisible* invalidation that has the same
effect, exercised against the deterministic pull-to-refresh repro **on Android only**, with an iPhone check
requested once and once only, after it is known to work.

Note this inverts the risk: `dvh` stays correct on iOS by definition, so a nudge cannot regress rotation there —
at worst it does nothing.

## Superseded record: fixed + verified on Android (2026-08-03), before the iPhone re-walk reverted it

**The fix, third attempt and the one that holds.** `BCoreAppShell` publishes `--b-shell-viewport-h` on the
document root as **`min(window.innerHeight, freshly-probed 100dvh)`**, refreshed on `resize`, `pageshow`,
`visibilitychange` and `visualViewport.resize`; `:host` and `reset.css`'s `body` both consume it inside
`@supports (height: 1dvh)`, keeping bare `dvh` as the pre-JS fallback and `vh` below that.

`min()` is the load-bearing part. Each candidate is wrong on a different platform and **each is wrong by being
too large**, so the smaller one is the honest one without needing to detect the platform:

| | Android WebAPK after in-place reload | iOS Safari |
|---|---|---|
| `100dvh` | **653.63 — too large** (ignores the nav-bar inset) | correct |
| `innerHeight` | 597 — correct | **may report the large viewport** |
| `min()` | **597 ✓** | **= dvh, i.e. unchanged from today ✓** |

That last column is the point: on the one device with a signed-off baseline, `min()` resolves to exactly what
the stylesheet already did, so the fix is a no-op there by construction.

`visualViewport.height` was rejected despite being correct *earliest* (already 597.63 at t=82ms, when
`innerHeight` still read a nonsense 1830): it shrinks with the on-screen keyboard, which would jump the bottom
nav up over the keyboard whenever a user focuses an input. Confirmed on device that the nav does **not** move
when focusing a weight field — the third acceptance criterion, decided deliberately rather than discovered.

**Verified on the device against the deterministic reproduction**, `typecheck` + `build` clean:

| measure | before | after |
|---|---|---|
| published var | (none) / 653.63 | **597px** |
| shell height | 653.63 | **597** |
| body `min-height` | 653.63 | **597** |
| nav bottom vs `innerHeight` | 653.63 vs 597 ✗ | **597 vs 597 ✓** |
| maxScroll | 56 | **0** |

Six CDP `Page.reload`s **and three human pull-to-refreshes** — the gesture that reproduced the bug 2/2 before
the fix — all clean, plus a screenshot showing the nav in place. Every measurement taken on a target confirmed
`hasFocus() && visibilityState === 'visible'`, after an earlier reading off a hidden zombie page sent the
diagnosis sideways.

### iPhone re-walk (2026-08-03) — 7 of 8 passed, and the 8th caught a regression from this fix

Run as TASK-045 steps i1–i8. **i1–i7 passed**, including the two checks written specifically for this fix: the
keyboard does not shrink the shell (i7 — the nav rides up with iOS's own visual-viewport shift and returns), and
no gap opens below the nav, which was the one way `min()` could have broken iOS.

**i8 failed: rotating to landscape "needed a gentle scroll so it fixes properly on screen".** That is this fix's
own doing. A `100dvh` shell re-resolves a rotation in CSS for free; a **measured px height only tracks the
viewport as well as the sampling that maintains it**, and the first implementation sampled at one frame and
300ms — sufficient for Android's system bars, not for an iPhone rotation animation. So the layout stayed at the
portrait height until some later event forced another sample.

Fixed by sampling across a settle window (rAF, then 100/300/600/1000ms — passes after the value settles are free,
because the publisher early-returns on no change) and by also listening to `orientationchange`, since iOS fires
`resize` before the new metrics are readable.

**The lesson is the one this task keeps re-teaching:** replacing a live CSS unit with a measured value moves the
responsibility for tracking the viewport from the engine to us, and every viewport transition then has to be
enumerated. Rotation was the transition nobody thought of, and it had **no Android counterpart in the plan** —
it was caught only because the iOS re-walk was treated as mandatory rather than as a formality.

### Why rotation is an iOS-only concern here — measured, not assumed

`manifest.webmanifest` declares `"orientation": "portrait"`, and **Chrome honours it**: with the device forced to
landscape over adb (`settings put system user_rotation 1`), the installed app still reported
`innerHeight: 597` — the portrait height — with the nav visible and `maxScroll 0`. iOS home-screen PWAs ignore
manifest orientation, so they do rotate. That is the whole reason i8 had no Android counterpart in the plan, and
it is worth knowing for the next device pass: **any rotation-sensitive behaviour in this app can only be
verified on iOS.**

### Android re-verified after the settle fix (2026-08-03)

Four further in-place reloads on the Mi A2 Lite, all clean: `publishedVar 597px`, shell 597, body 597,
`navBottom 597 == innerHeight`, `maxScroll 0`. Run because the settle fix touched the shared code path that had
been verified *before* it existed — re-run rather than assumed.

_(Method note: target selection now keys on `matchMedia('(display-mode: standalone)')`, not on whether the URL
carries a `#`. The installed app navigates to `#/` once its router runs, so the earlier hash heuristic
mislabelled the WebAPK as a browser tab and reported "no focused page" while the app was plainly on screen.)_

### Outstanding before `done`

- [ ] **iPhone i8 re-test** — rotate to landscape and back, no gentle scroll needed, after the settle fix.
      The only rotation-capable surface (see above), so there is no substitute for it.
- [ ] ~~**iPhone re-check** — steps 5–8 of TASK-045's walk.~~ **Done 2026-08-03**, see above; only i8's re-test
      remains. The shell's height is exactly what the 2026-07-06 iOS
      pass signed off, so that sign-off no longer covers the current code. Also covers TASK-129's `dvh` body
      change on iOS Safari tabs. **This is the one thing gating both tasks, and it needs the iPhone.**
- [x] `b-drawer.ts:26` `max-height: 100vh` — **assessed 2026-08-03: not a defect, and changing it would be the
      behaviour change, not the fix.** This task previously asserted "`vh` there is the *large* viewport, so a
      full-height drawer can already overflow on mobile". That does not survive reading the rule in context. The
      dialog is `position: fixed; inset: 0 0 0 auto; height: 100%`, so its height already resolves against the
      fixed containing block — and `100vh` is the large viewport, i.e. **always ≥ that height**, so the
      `max-height` never binds. It is dead weight, not a clamp. Rewriting it to `100dvh` would *introduce* a
      binding clamp on any browser whose fixed containing block is the large viewport, making the drawer shorter
      than its own box — a real change in appearance, argued from a symmetry with the shell rather than from a
      measured defect. Also **`b-drawer` is unused in Reps** (grep: no occurrences in `Reps.Web/src`); it is a
      desktop pattern at `--b-drawer-width: 30rem`, so there is no mobile exposure here to fix. Left alone
      deliberately. The rest of the family is already correct: `dialogs/index.ts:514` and `b-modal` use `dvh`,
      and `b-split-panel` measures its scrollport in JS on purpose.
- [ ] Symbio pass — it consumes the same base shell.
- [ ] Playground smoke asserting the shell publishes the variable and updates it on a synthetic
      `visualViewport` resize. Guards the mechanism, **not** the defect — see Testability.

## Implementation plan

_Superseded by the section above, which records what was actually built after two reverted attempts._
