# Birko.VersionControl.Git.Tests

## Overview
Tests of `GitService` and the git agent tools (TASK-534) against a temporary repository per test (git 2.38+ on PATH).
They cover:
- init, commit and `NoChanges`, status, the files of a root commit, a log subject containing `|`, branch diff
- branch lifecycle and refused names (`-f`, `a..b`, …)
- the merge check leaving HEAD, the files and the uncommitted work untouched
- a clean `--no-ff` merge returning to the original branch, a conflicting merge changing nothing, a dirty tree refused
- worktrees, and 8 concurrent stage+commit operations without index-lock failures
- the tools through a project resolver and without one, and the capped diff

## Dependencies
xUnit, FluentAssertions; imports Birko.Helpers, Birko.AI.Contracts, Birko.VersionControl.Git.

## Maintenance
See [CLAUDE-maintenance.md](../../Birko.Framework/CLAUDE-maintenance.md).
