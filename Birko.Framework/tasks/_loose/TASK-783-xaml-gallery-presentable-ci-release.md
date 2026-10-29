---
id: TASK-783
parent: null
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-29
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# Birko.Xaml gallery: presentable, built and released by CI, linked from the wiki and the landing page

## Context

The user asked (2026-10-29, `docs/BRIEF.md`) for the Birko.Xaml desktop gallery to look better, for CI that builds it
on every commit, and for links to it on the wiki and https://birko-framework.github.io/. Before: a 520×600 window,
a big title, four plain theme buttons, a wrapping TabControl, controls with no labels; no CI and nothing to download.

## Decisions (the user's picks from previews, 2026-10-29)

| # | Decision | State |
|---|---|---|
| D1 | Layout like Birko.Web.Playground: Birko's own Ribbon as the header (Gallery sections, Appearance themes, Help), every control in a card with a one-line description and an ⓘ tooltip, a 1280×820 window | approved |
| D2 | CI builds on every push to main for Windows x64, Linux x64 and macOS (Apple Silicon), self-contained, published as a rolling `latest` GitHub Release; unsigned | approved |
| D3 | CI also renders screenshots, attached to the release, so the wiki and the landing page show the current build | approved |

No Lab tab: unlike the web Playground, the gallery has no test-only harnesses; the ribbon's look pickers stay with the
ribbon demo as its "try it" controls.

## Acceptance criteria

- [x] Header is a Birko `Ribbon` (Ctrl+F1 and Alt key tips off on it, so the Chrome demo ribbon keeps them)
- [x] Every demo in a `DemoCard`: title, type, ABOUT line, ⓘ with source and Birko.Web twin (also the button's help text)
- [x] `--screenshots <dir>` renders every section headlessly (13 PNGs)
- [x] `.github/workflows/build.yml`: win-x64 / linux-x64 / osx-arm64 builds + screenshots → `latest` release; first run green
- [x] Wiki Desktop-XAML (download table, screenshots), Home and sidebar link it; the landing page's Birko.Xaml card shows
      the screenshot and the downloads, and Where things live has a Birko.Xaml.Gallery card

## Human test plan

1. Download the build for your system from https://github.com/Birko-Framework/Birko.Xaml.Gallery/releases/latest and
   start it as the release notes say (Windows: SmartScreen "More info → Run anyway"; macOS: clear the quarantine flag).
2. The window opens at about 1280×820 with the ribbon header; every section under Gallery switches the body, and the
   pressed button follows.
3. Appearance: Light, Dark, Neon, Finstat restyle everything live; the pressed theme follows.
4. Hover or click ⓘ on a few cards: the tooltip names the source, the AXAML type and the Birko.Web twin.
5. Help: About opens the dialog ("Start exploring" closes it); Source, Guide, Web gallery and Downloads open the browser.
6. Chrome: the demo ribbon still answers Alt (key tips) and Ctrl+F1; the header ribbon does not.
7. Overlays: modal, form modal, drawer, and Ctrl+K open and close.
8. Resize the window narrow: the cards wrap to one column, nothing is cut off.

## Outcome (2026-10-29)

Birko.Xaml.Gallery `5d90f96`: GalleryView rewritten (ribbon + section panels + DemoCard), DemoCard.cs/.axaml,
Screenshots.cs, Program `--screenshots`, Avalonia.Headless + Skia packages, workflow, README / CLAUDE.md. CI run
40839669956 green (3 builds 37–39 MB compressed, screenshots, release). Checked locally: `dotnet run` and the published
win-x64 single file start and render; the CI screenshot renders the same. Wiki `6068ab2`, landing page `21fc94f`
(and `d45d0ba`, the User interface section that the previous request had left unpushed). macOS and Linux builds are
built and packaged by CI but not started by hand — human test step 1.

## Human test round 1 (2026-10-29, the user) — closed

All eight steps passed on the Windows download (x64, started from the release). Step 6 was first reported failing: the
user had pressed the keys on the header ribbon, which deliberately ignores Ctrl+F1 and Alt; on the Chrome section's
demo ribbon both work. The step's wording ("the header ribbon does not") invited exactly that reading — a future plan
should say which ribbon to look at first.

The macOS and Linux builds are built and packaged by CI (run 40839669956) but were not started by hand; a report from
either platform reopens this as a bug.
