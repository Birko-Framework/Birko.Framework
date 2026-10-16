---
id: TASK-652
parent: EPIC-015
feature: null
status: done
priority: P2
assignee: ai
created: 2026-10-16
depends-on: []
blocks: []
findings: []
pr: null
github-issue: null
jira-key: null
---

# ObjectTree option to hide null members

## Context

FinStat's desktop API tester (`DesktopFinstatApiTester`, in `C:\Source\ClientApi.CSharp`) shows API
responses in an `ObjectTree` bound to the deserialized client model (`objectTreeData.Source = model`). One
model class serves several API products (EliteCZ deserializes into the classes UltimateCZ fills), so the
fields a product does not return are null on the model, and the tree shows them as `null` leaves, which
reads as if the API had sent them.

The tester cannot switch to the `Json` property: its context menu ("Show XML In Output Window", "Copy JSON")
serializes `SelectedValue` with `XmlSerializer` / `System.Text.Json`, so the nodes must stay the real graph
objects. The tree itself has to be able to leave the nulls out.

Code: `Birko.Xaml.Avalonia/Controls/ObjectTree.cs` (`Rebuild`, `BuildNode`, `Enumerate`, `Summary`).

## Acceptance criteria

- [x] `StyledProperty<bool> HideNullMembers`, default `false`, so existing consumers see no change; a
      `Changed` class handler rebuilds the tree, as `Source` and `Json` do
- [x] With the option on, *named* members whose value is null are not shown: public properties of plain
      objects, `IDictionary` entries, and `JsonObject` entries (a null JSON value is a null `JsonNode`)
- [x] Array / enumerable items (`JsonArray`, `IEnumerable`) are never hidden: their `[i]` index is their
      identity, and hiding one would shift the paths
- [x] A null root still renders the single `null` leaf
- [x] A container's summary counts what is shown: with the option on, a `{n}` member count excludes the
      hidden nulls
- [x] `SelectedValue` / `SelectedPath` behave as before for the nodes that are shown
- [x] The filter lives in one place (`Enumerate` or a wrapper over it), not in `BuildNode` and `Rebuild`
- [x] `ObjectTreeTests`: POCO null property shown by default and hidden with the option; `{"a":1,"b":null}`
      hides `b`; a null array item keeps its `[i]` node; toggling the option on a built tree rebuilds it; a
      null `Source` still shows the null leaf
- [x] Documented in `Birko.Xaml.Avalonia/README.md`, `docs/xaml.md` and the project `CLAUDE.md`; recorded in
      `Recent Updates` / `CHANGELOG.md`

## Out of scope

- Setting `HideNullMembers = true` in `DesktopFinstatApiTester` — a consumer change in its own repo.
- Hiding null items of arrays/enumerables (see the criteria for why).

## Human test plan

N/A — fully covered by automated tests: the change is which nodes the tree builds, which the headless
tests assert node by node; the consumer adoption that a person would look at is out of scope.

## Implementation plan

1. Add `HideNullMembers` + class handler next to `Source`/`Json`.
2. Make `Enumerate` take the filter: a `Members(value)` instance wrapper that drops null values of named
   containers (`JsonObject`, `IDictionary`, plain objects) when the option is on; `Rebuild` and `BuildNode`
   both enumerate through it.
3. `BuildNode` materializes the members once and passes the shown count to `Summary`, so the header count
   is the count of the children built (by construction, for every container kind).
4. Tests in `ObjectTreeTests`, then docs and changelog.

## Review log

- 2026-10-16 — Seven tests added to `ObjectTreeTests`. Red check: with the filter neutralised, 5 fail (POCO,
  JSON field, dictionary entry, summary count, toggle); with indexed items filtered too, the array test fails.
  The null-root test is a pin. `Birko.Xaml.Avalonia` builds with 0 errors (3 pre-existing warnings, none in
  `ObjectTree`); `Birko.Xaml.Avalonia.Tests` 209/209 green.
