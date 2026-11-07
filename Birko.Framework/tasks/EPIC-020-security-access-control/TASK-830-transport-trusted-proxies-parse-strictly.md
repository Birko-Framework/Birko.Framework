---
id: TASK-830
parent: EPIC-020
feature: null
# status — one of: todo, in-progress, verify (code done, sign-off pending), done, cancelled
status: done
# blocked: <reason> — set by /tasks block, never by hand; status stays as it is; /tasks unblock removes it
priority: P3
# assignee — one of: agent (default), unassigned, human, or an installed agent type's name; ai is read as agent, never written
assignee: agent
created: 2026-11-07
depends-on: []
blocks: []
# findings: ids this task remediates — from a review/audit/harvest/drill pass, or from ordinary
# field use with no pass behind it at all. Prefixes: see /tasks intake
findings: []
pr: null
github-issue: null
jira-key: null
---

# Transport configurations refuse a malformed TrustedProxies entry instead of skipping it

## Context

Spawned from [[TASK-829]] (grill Q4, the user's decision on 2026-11-07). TASK-829 made `TrustedProxyList` the one parser
for every trusted-proxy list, in two modes:

- **Strict** for `AddBirkoForwardedHeaders`: a bad entry stops the host at startup.
- **Lenient** for `AuthenticationService`, which serves the `HttpListener` transports (REST, SOAP, SSE) and the
  WebSocket config. A bad entry is skipped and logged at error level. Before TASK-829 it was skipped silently.

Keeping those transports lenient held TASK-829 to its own criteria. The cost:
- An entry such as `10.0.0.300`, a typo'd CIDR, or a `${VAR}` naming an unset variable leaves the proxy untrusted.
- Every request then appears to come from the proxy, so a token binding's `AllowedIps` matches nothing, or matches
  only the proxy.
- The operator gets one error log line and no refusal.

That is rule 60's shape: the neighbouring setting (`Birko:ForwardedHeaders`) fails loudly on the same input.

Where it lives:
- `Birko.Security/Authentication/AuthenticationService.cs`, constructor: the `TrustedProxyList.ParseLenient` call.
- Static `GetClientIpAddress(..., IReadOnlyCollection<string>)`: public, and parses per call.

## Acceptance criteria

- [x] `AuthenticationService` parses `TrustedProxies` with `TrustedProxyList.Parse`. A bad entry throws
      `InvalidOperationException` at construction, naming the entry and the configuration type, and wrapping the
      `FormatException`.
      → `AuthenticationService.ParseTrustedProxies`, in the configuration's mode (default `Strict`), run before the
      lock is created, so a refusal leaks nothing. Pinned by
      `Task830StrictTransportProxiesTests.A_bad_entry_refuses_at_construction_naming_the_configuration`; 6 of its 9
      tests are red with the default set to `Lenient` (measured).
- [x] Decide with the user what the public static `GetClientIpAddress(…, IReadOnlyCollection<string>)` does with a bad
      entry (it parses per request), and record it.
      **Decided 2026-11-07 (the user): a mode parameter, on the static method and on the constructor.**
      - New enum `TrustedProxyParsing { Strict, Lenient }`.
      - The static method takes it as an optional last argument, defaulting to `Lenient`, so existing callers are
        unchanged.
      - `AuthenticationConfiguration.TrustedProxyParsing` defaults to `Strict`. A consumer that the new strictness
        breaks can set `Lenient` to opt back in.
- [x] The WebSocket map-time check (`WebSocketAuthenticationGate`) parses the WebSocket configuration's list in its
      configured mode, so a bad entry refuses at startup rather than on the first request. Before, a list of only
      malformed entries read as empty and deferred to the host (found by the TASK-829 respec).
      → `EnsureProxiesValid`, run at map time with or without `AddBirkoForwardedHeaders`. Pinned by
      `A_websocket_list_with_a_bad_entry_refuses_when_mapped_even_without_the_registration`, which is red with the
      old default.
- [x] Each transport's startup path (REST server, SOAP, SSE, WebSocket) is checked to surface the throw at startup,
      not on the first request. Any that does not is listed with what it does instead.
      → **REST, SOAP, SSE:** the framework never builds these. `RestAuthenticationService`,
      `SoapAuthenticationService` and `SseAuthenticationService` are constructed by the consumer (`new`, or DI), and
      each builds its `AuthenticationService` in its constructor. `SseAuthenticationMiddleware` takes the service in
      its own constructor, so it throws when the consumer composes the server; a consumer that resolves the service
      lazily throws on first use.
      → **WebSocket:** at map time (above).
      → **Consumers:** none of `C:\Source\Birko\Consumers` sets a transport `TrustedProxies` (grep, 2026-11-07), so
      the breaking change refuses no known consumer.
- [x] Tests in `tests/Birko.Security.Tests` pin the refusal. The transport suites that import Birko.Security stay
      green.
      → `Task830StrictTransportProxiesTests` (9 tests). REST, SOAP, SSE, SSE.AspNetCore, WebSocket, Security and
      Security.AspNetCore suites are green.
- [x] `Birko.Security/CLAUDE.md` and `docs/security.md` § Static Token Authentication say a bad entry refuses. A
      Recent Updates entry marks it **breaking**: a config that started before may now refuse.
      → Also `Birko.Security/README.md` (`TrustedProxyParsing`).

## Out of scope

- The forwarded-headers registration, already strict since TASK-829.

## Human test plan

N/A — fully covered by automated tests: the change is construction-time parsing, asserted directly. A person running a
transport with a bad entry would see only the exception the tests already check.

## Implementation plan

1. `Birko.Security/Authentication/TrustedProxyParsing.cs` (enum, projitems).
   `TrustedProxyList.Parse(entries, mode, onSkipped)` dispatches to `Parse` / `ParseLenient`.
2. `AuthenticationConfiguration.TrustedProxyParsing = Strict`. The `AuthenticationService` constructor parses in that
   mode. A `FormatException` becomes an `InvalidOperationException` naming `<ConfigType>.TrustedProxies`, wrapping it
   (rule 9).
3. Static `GetClientIpAddress(…, trustedProxies = null, TrustedProxyParsing mode = Lenient)`. Strict throws the
   `FormatException`.
4. `WebSocketAuthenticationService.EnsureProxyListsAgree` → `EnsureProxiesValid(config, host)`: parse the own list in
   its mode (strict throws), then the agreement check. `WebSocketAuthenticationGate` runs it at map time even
   without a host list.
5. Tests:
   - `tests/Birko.Security.Tests/Task830StrictTransportProxiesTests.cs`: the constructor refuses per transport config
     type; `Lenient` opts back in; the static method in both modes.
   - The WebSocket map-time refusal.
6. Docs:
   - `docs/security.md` § Static Token Authentication
   - `Birko.Security/CLAUDE.md` and `README.md`
   - a Recent Updates entry marked **breaking**
