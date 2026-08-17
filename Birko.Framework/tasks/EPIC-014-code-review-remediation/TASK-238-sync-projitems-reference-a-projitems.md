---
id: TASK-238
parent: EPIC-014
feature: FEATURE-014
# status: todo | in-progress | review (code done, sign-off pending) | blocked | done | cancelled
status: todo
priority: P3
assignee: ai
created: 2026-08-17
depends-on: []
blocks: []
related: [TASK-234]
findings: []
pr: null
github-issue: null
jira-key: null
---

# Five `Birko.Data.Sync.*` projitems carry a `ProjectReference` to another `.projitems`

## Context

Found while doing [[TASK-234]]'s first batch — the ElasticSearch build sweep ran with `-warnaserror` and
surfaced it, which is exactly what that criterion is for.

`Birko.Data.Sync.ElasticSearch.projitems` line 22:

```xml
<ProjectReference Include="..\Birko.Data.Sync\Birko.Data.Sync.projitems">
```

A shared project **cannot** `ProjectReference` another shared project — a `.projitems` is an include file,
not a build target. The path is also resolved against the *consuming* project's directory rather than the
declaring one, so from `Framework.Tests/Birko.Data.Sync.ElasticSearch.Tests/` it points at
`Framework.Tests/Birko.Data.Sync/`, which does not exist:

```
warning MSB9008: The referenced project ..\Birko.Data.Sync\Birko.Data.Sync.projitems does not exist.
```

**Five projects carry it**: `Birko.Data.Sync.CosmosDB`, `.ElasticSearch`, `.Json`, `.MongoDb`, `.RavenDB`.

**It is a warning today and does nothing** — the reference is inert, and every consumer already imports
`Birko.Data.Sync.projitems` explicitly, which is why nothing has ever been broken by it. That is also why
this is P3 rather than higher.

**But it becomes an error under `-warnaserror`**, which `verify-conventions` runs and which TASK-234's own
acceptance criteria mandate for the build sweep. So every remaining TASK-234 batch has to either see this
noise or filter it, and a filtered sweep is the shape that reported "166 of 166 clean" while 10 projects
were broken.

**It is worth reading as a failed attempt at the thing TASK-234 concluded is impossible.** Someone tried to
express "this shared project depends on that shared project" and MSBuild has no way to say it — the same gap
that forces 23 of TASK-234's 38 projects to record their base in a comment instead of declaring. Deleting
these five lines is right, but the comment that replaces them should say what the reference was *trying* to
express, or it will be re-added.

## Acceptance criteria

- [ ] The five `ProjectReference` items are removed, each replaced by the same kind of comment TASK-234's
      batch 1 introduced: name the projitems a consumer must also import, and why it cannot be expressed
- [ ] `dotnet build -warnaserror` on each of the five test projects is clean, and the sweep matches any
      `warning MSB` / `error <CODE>` rather than a hand-listed set
- [ ] A grep confirms no other `.projitems` in the family carries a `ProjectReference` to a `.projitems`

## Out of scope

- The rest of TASK-234. This is a build-hygiene defect that TASK-234's sweep uncovered, not one of its 38.

## Human test plan

N/A — mechanical.

## Implementation plan

_Populated by `/tasks plan TASK-238` — leave empty until then._
