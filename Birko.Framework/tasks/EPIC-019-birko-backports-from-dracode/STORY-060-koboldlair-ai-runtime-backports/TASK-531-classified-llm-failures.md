---
id: TASK-531
parent: STORY-060
feature: null
status: todo
priority: P2
assignee: ai
created: 2026-10-08
depends-on: []
blocks: []
pr: null
github-issue: null
jira-key: null
---

# LlmResponse carries a classified failure (status, transient, retry-after), not just a string

## Context

DraCode's `Services/ErrorClassifier.cs` (168) decides transient vs permanent by matching substrings of error text:

- `"500"` and `"timeout"` match unrelated text
- Unknown is documented as permanent, but any non-empty unknown text returns Transient
- `"quota exceeded"` (usually billing) counts as transient

It exists because `LlmResponse.Error(string)` keeps only a message, while `LlmProviderBase` knows the `HttpStatusCode` and
already has `IsRetryableStatusCode`. `AgentTaskRecord.ErrorCategory` is an untyped `string?`.

Adopted in the consumer by DraCode TASK-129 (`Consumers/DraCode/tasks/EPIC-018-adopt-birko-upstreams/STORY-040`).

## Acceptance criteria

- [ ] `LlmResponse` (`Birko.AI.Contracts`) carries the status code, an `IsTransient` flag and retry-after when known
- [ ] Classified once, in `LlmProviderBase`; providers that bypass it are covered
- [ ] `ErrorCategory` is typed
- [ ] A text classifier, if shipped at all, is a documented fallback for tool output only
- [ ] Tests: 429 with Retry-After, 5xx, 400, 401/402 billing, network timeout, cancellation (not a failure)

## Human test plan

N/A — library behaviour, covered by unit tests.

## Implementation plan
