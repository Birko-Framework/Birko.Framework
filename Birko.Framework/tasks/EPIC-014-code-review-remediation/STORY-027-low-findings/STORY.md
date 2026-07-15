---
id: STORY-027
parent: EPIC-014
status: in-progress
created: 2026-06-18
source: CODE-REVIEW-AUDIT-2026-06-17.md
severity: low
finding-count: 418
finding-ids: CR-L001 …
---

# Low findings

## Progress

**311 / 418 triaged** as of 2026-07-15. Next open is CR-L312 (Birko.Models.Pricing cluster, L312…).

**Batch BR — Birko.Models.Inventory.SQL cluster (CR-L310, CR-L311):** Birko.Models.Inventory.SQL. Both
closed; **/code-review clean (no findings)**. **L310** (test-gap): created a new
**Birko.Models.Inventory.SQL.Tests** project (git-init'd + registered in `.slnx`/`.code-workspace`;
mirrors Customers.SQL.Tests) — `InventoryMappingTests` runs each `Configure()` against a fresh `ModelMap<T>`
and asserts the three table names (Items/Repositories/WareHouseDocumentItems), Guid primary+unique, the
InventoryDocumentLine decimal columns' precision 22/scale 6, and StockItem's bounded string columns.
**L311** (cleanup): dropped the redundant `map.Property(x => x.SortOrder).HasColumnName("SortOrder")` in
StorageLocationMapping — the column name already defaults to the property name and the int needs no facet,
so SortOrder maps by default (framework auto-discovers unmapped properties; explicit `Property()` only adds
facets — StockItemMapping already relies on this). **Tests:** new Inventory.SQL.Tests 13. Suite green:
Inventory.SQL.Tests 13.

**Batch BQ — Birko.Models.Inventory (CR-L309):** Birko.Models.Inventory. Closed;
**/code-review clean (no findings)**. **L309** (convention — "implement on all" option): only StockItem /
StockItemVariant implemented `ICopyable<T>` with a typed `CopyTo`; StockMovement, InventoryDocument,
InventoryDocumentLine and StorageLocation implemented only `ILoadable`, so cloning them via the inherited
`AbstractLogModel.CopyTo` silently dropped every domain field. Implemented `ICopyable<T>` + a typed `CopyTo`
on all four (instantiate-if-null, `base.CopyTo`, copy own fields — matching the concrete StockItem pattern);
`InventoryDocument` deep-copies its `Lines` (each via the new `InventoryDocumentLine.CopyTo`) so the clone
doesn't alias the original's line objects. **Tests:** Inventory.Tests 3 → 8 — field-copy for each of the
four + null-clone-instantiates + Lines deep-copy (clone lines `NotBeSameAs` originals). Suite green:
Inventory.Tests 8.

**Batch BP — Birko.Models.Customers.SQL cluster (CR-L307, CR-L308):** Birko.Models.Customers.SQL. Both
closed; **/code-review clean (no findings)**. **L307** (test-gap — **partly pre-existing**): a
`Birko.Models.Customers.SQL.Tests` project already existed (created for CR-M219/M220, git-init'd + registered
in `.slnx`/`.code-workspace`) with the string-column precision asserts — verify-first confirmed the project +
precision coverage. The audit's remaining unmet asks (table names + PK/unique on Guid) were added:
`Mappings_HaveExpectedTableNames` (Customers/Addresses/InvoiceAddresses/ContactPersons) and
`Mappings_MarkGuidAsPrimaryAndUnique` across all 4 mappings, via `ModelMap<T>` directly. **L308** (cleanup):
the source project was missing the framework-required `.gitignore` — copied the standard 325-line VS template
from a sibling (`Birko.Models.Customers`), so `bin/obj/.vs` aren't committed. **Tests:** Customers.SQL.Tests
22 → 24. Suite green: Customers.SQL.Tests 24.

**Batch BO — Birko.Models.Customers (CR-L306):** Birko.Models.Customers. Closed;
**/code-review clean (no findings)**. **L306** (cleanup): 5 ViewModels (Address, BaseCustomer, Customer,
CustomerAddress, InvoiceAddress) allocated a fresh `new[] { … }` array + ran a LINQ `Contains` on **every**
PropertyChanged event to decide whether to raise the aggregate object notification. Hoisted each watched-set
to a `private static readonly HashSet<string>` (allocated once) and test membership against it with a
null-narrowing guard (`e.PropertyName != null && set.Contains(...)`). Behavior-preserving (same ordinal
equality, same null→no-raise); swapped the now-unused `using System.Linq` for `System.Collections.Generic`.
(ContactPerson/CustomerBankAccount have no such dispatch — untouched.) **Tests:** Customers.Tests 4 → 5 —
`Address_WatchedPropertyChange_RaisesAddressObjectNotification` pins the preserved aggregate notification.
Suite green: Customers.Tests 5.

**Batch BN — Birko.Models.Contracts (CR-L305):** Birko.Models.Contracts. Closed;
**/code-review clean (no findings)**. **L305** (nullable): `HierarchyHelper.IsDescendantOf` dereferenced
both `Path` properties unguarded, NRE-ing when either entity's Path wasn't yet materialized. Added a
guard returning false when either argument or its Path is null/empty (guard-clause convention), and aligned
the `StartsWith` to `StringComparison.Ordinal` to match the sibling `RewriteDescendantPaths` path handling.
**Tests:** Contracts.Tests 7 → 11 — `IsDescendantOf` child→true, sibling-prefix→false (separator boundary),
same-node→false, and null/empty Path→false (the L305 guard). Suite green: Contracts.Tests 11.

**Batch BM — Birko.Models.Category cluster (CR-L303, CR-L304):** Birko.Models.Category. Both closed;
**/code-review clean (no findings)**. **L303** (convention): all three `LoadFrom` methods (Model +
ViewModel ×2) called `base.LoadFrom(data)` **before** the `if (data == null) return;` guard — illogical
ordering (guard after the parameter's first use). Moved the guard to the top; base is null-safe so this is a
no-op ordering fix, now honoring the guard-clause convention. **L304** (bug): the ViewModel `Path` setter
assigned + raised `PropertyChanged` unconditionally, unlike the `Title`/`Slug`/`Description` setters (which
guard `if (_field != value)`) — setting `Path` to its current value fired a spurious change that cascaded to
the `Category` object notification (dirty-flag/re-render churn). Added the `if (_path != value)` guard.
**Tests:** Category.Tests 9 → 12 — `PathSetter_SameValue_DoesNotRaisePropertyChanged`,
`PathSetter_NewValue_RaisesPropertyChanged`, `LoadFrom_Null_DoesNotThrow` (all three overloads). Suite
green: Category.Tests 12.

**Batch BL — Birko.Models cluster (CR-L300, CR-L301, CR-L302):** Birko.Models. All closed;
**/code-review clean (no findings)**. **L300** (cleanup): `SourceValueExtensions.GetValue` ran
`Any()`+`FirstOrDefault()` — two enumerations of a possibly-lazy sequence (can even yield different elements
between passes). Now a single `FirstOrDefault` with a guard; `SetValue` likewise single-scans. **L301**
(nullable): `AbstractPercentage.CopyTo` returned `clone!` when `clone` was null — a provably-null value with
a suppressed warning, NRE-ing the caller. It's abstract (can't self-instantiate like `ValueData.CopyTo`), so
it now throws `ArgumentNullException` on a null clone (the only subclass, `AbstractDatabasePercentage`,
doesn't override `CopyTo` and has no null-clone callers). **L302** (other): `AbstractTree.BuildPath` returned
`string?` but never actually returned null (`"/" + null == "/"`), making `LoadFrom`'s `?? string.Empty` dead;
rewrote it to return non-null `string`, enumerate the sequence once, and documented "/" as the root sentinel;
dropped the dead coalesce. **Tests:** Models.Tests 35 → 38 — `GetValue_EnumeratesSourceExactlyOnce` (a
counting iterator proves the single pass), `AbstractPercentage_CopyTo_NullClone_ThrowsArgumentNullException`
+ `AbstractPercentage_CopyTo_CopiesPercentageIntoClone` (via a concrete `TestPercentage`). BuildPath and
GetValue/SetValue happy paths were already covered (behavior preserved). Suite green: Models.Tests 38.

**Batch BK — Birko.Messaging.Razor cluster (CR-L298, CR-L299):** Birko.Messaging.Razor (+ new
`TemplateNotFoundException` in Birko.Messaging). Both closed; **/code-review clean (no findings)**. **L298**
(other): `RazorTemplateEngine` keyed inline templates by a `ConcurrentDictionary<fullTemplateString,
guidKey>` that never evicted — a slow leak for a long-lived engine rendering many one-off inline templates.
Replaced it with a **content-hash cache key** (`"inline_" + SHA256(template)`), so identical content still
reuses the compiled entry (RazorLight compiles once) without storing every template string; dropped the
dictionary field + its Dispose clear. **L299** (other, security): the file→inline fallback caught
`TemplateRenderException`, which also swallowed the **path-traversal rejection** `ResolveFilePath` throws —
a malicious template Name silently fell back to inline instead of surfacing the rejection. Added a
`TemplateNotFoundException : TemplateRenderException` subtype (in Birko.Messaging, alongside
`TemplateRenderException`); the provider throws it only for genuine not-found, and the engine's fallback now
catches **only** that subtype, so a traversal `TemplateRenderException` propagates. Since the subtype IS-A
`TemplateRenderException`, all existing `catch` sites are unaffected. **Tests:** Razor.Tests → 39 — provider
not-found now asserts `TemplateNotFoundException`, traversal asserts `TemplateRenderException` but NOT the
not-found subtype, engine propagates a traversal Name instead of falling back, and identical inline templates
render consistently (hash-key path). Downstream Messaging.Tests 52 green (new type is additive). Suites
green: Razor.Tests 39, Messaging.Tests 52.

**Batch BJ — Birko.Messaging cluster (CR-L295, CR-L296, CR-L297):** Birko.Messaging. All closed;
**/code-review clean (no findings)**. **L295** (bug): `SmtpEmailSender.SendBatchAsync` delegated each element
to `SendAsync`, which throws `ArgumentNullException` on a null message — so a single null element aborted the
whole batch and discarded results already collected, breaking the result-collecting contract. Null elements
are now captured as `MessageResult.Failed("Null message.")` and the batch continues (result count matches
input). **L296** (nullable): `ToMailAddress` passed `address.Value` straight to `new MailAddress(...)`;
`MessageAddress` only guards null (not empty/whitespace/malformed), so a `Value=""` or bad address threw a
generic exception surfaced as "Failed to send email". Now it guards empty/whitespace and converts a
`FormatException` to the dedicated (previously-unused) `InvalidRecipientException`, which `SendAsync` catches
(before the generic catch) → a clear "Invalid recipient" Failed reason. **L297** (test-gap — mostly
pre-covered): verify-first found `StringTemplateEngineTests` already covers the missing-property throw,
multi-segment path, and null-leaf → empty; the one gap was a **null intermediate** mid-path (distinct from a
missing property) — added a test for it. SMS/Push have DTOs only (no senders yet), so no sender tests.
**Tests:** Messaging.Tests → 52 — `SendBatchAsync_NullElement_CapturedAsFailed_OthersSucceed`,
`SendAsync_EmptyRecipientAddress_ReturnsInvalidRecipientFailure`,
`SendAsync_MalformedRecipientAddress_ReturnsInvalidRecipientFailure`,
`RenderAsync_NullIntermediateInPath_ReplacesWithEmpty`. Suite green: Messaging.Tests 52.

**Batch BI — Birko.MessageQueue.Redis cluster (CR-L292, CR-L293, CR-L294):** Birko.MessageQueue.Redis. All
closed; **/code-review clean (no findings)**. **L292** (other — reword doc): `RedisStreamSettings.BlockMilliseconds`
is documented accurately now — it's an **empty-poll back-off interval** (`Task.Delay`), not a server-side
XREAD `BLOCK` (StackExchange.Redis doesn't expose BLOCK), so newly-arrived-message latency is bounded below
by this interval. **L293** (cleanup): `RedisProducer` XADD wrote both a full serialized `message` field AND
duplicate per-field entries (id/body/payload_type/headers/created_at/priority) — the consumer prefers
`message` and only uses per-field parsing as a fallback, so the per-fields were dead weight (≈2× payload,
Headers serialized twice). Now writes only `message` (+ `ttl_ms`, which the consumer reads for its expiry
check); the consumer's per-field fallback is retained for entries written by other producers. **L294**
(cleanup): `SendAsync<T>` set `ContentType` twice (initializer + conditional re-set that clobbered a
caller value); collapsed to a single unconditional stamp of the serializer's content type — correct because
the typed body is serialized by `_serializer`, and consistent with the InMemory sibling (CR-L284). To make
L293/L294 testable without live Redis, added a `Func<IDatabase>` test-seam ctor to `RedisProducer` (mirrors
the consumer's existing seam). **Tests:** Redis.Tests 45 → 49 — XADD writes only `message` (and `message`+`ttl_ms`
with a TTL), the `message` blob round-trips id/body/payload_type/priority, and the typed send stamps the
serializer content type (over caller headers). Suite green: Redis.Tests 49.

**Batch BH — Birko.MessageQueue.MQTT cluster (CR-L288, CR-L289, CR-L290, CR-L291):** Birko.MessageQueue.MQTT.
All closed; **/code-review clean (no findings)**. **L288** (bug — "document" option): documented on
`MqttSettings.LoadFrom` that `ClientCertificate` (an `IDisposable` `X509Certificate2`) and `LastWill` are
copied by **reference** — ownership stays with the source, don't dispose one side's cert while the other is
in use. **L289** (bug): `MqttConsumer` now exposes an `OnHandlerError` (`Func<Exception, QueueMessage, Task>`)
callback invoked when a message handler throws (was a silent empty catch); other handlers still run and an
exception from the hook itself is swallowed so it can't break dispatch. **L290** (bug, concurrency):
`_reconnectCts` was read/cancelled/reassigned/disposed from three paths (broker `OnDisconnectedAsync`,
`DisconnectAsync`, `Dispose`) without synchronization — could dispose a CTS another path was using or leak
the previous one on reassign. Serialized all access behind a `_reconnectLock` via `StartNewReconnectCts`
(cancel+dispose old, create new — closes the per-cycle leak) and `CancelReconnect`; cancel-before-dispose
keeps the reconnect loop's captured token safe post-dispose. **L291** (bug): `MqttTopic.Matches` now excludes
`$`-prefixed system topics from a leading `#`/`+` wildcard (per the MQTT spec) — an exact first level
(`$SYS/#`) still matches. **Tests:** MQTT.Tests → 32 — `Matches` +4 cases (`#`/`+/x` don't match `$SYS/…`,
`$SYS/#` does, `#` matches a normal topic), `LoadFrom_SharesLastWillReference` (pins the L288 shallow-copy),
`OnHandlerError_InvokedWhenHandlerThrows` + `OnHandlerError_HookThatThrows_DoesNotBreakDispatch`. **L290's
reconnect path is code-review-verified** — `MqttMessageQueue` creates its `IMqttClient` internally and the
reconnect loop is broker-event-driven, so it isn't deterministically unit-testable without a live broker.
Suite green: MQTT.Tests 32.

**Batch BG — Birko.MessageQueue.InMemory cluster #2 (CR-L285, CR-L286, CR-L287):** Birko.MessageQueue.InMemory.
All closed; **/code-review clean (no findings)**. **L285** (bug, concurrency): `InMemoryChannel`'s dispatch-loop
lifecycle (start on first subscriber, stop on last) was decided non-atomically over a `ConcurrentDictionary`
count + a nullable `DispatchCts`, so concurrent `AddSubscriber` calls (or an add racing the last remove) could
start two loops or leave none running. Added a per-`DestinationState` `SyncRoot` lock and moved the
TryAdd+start and TryRemove+stop transitions (and the `Dispose` teardown) inside it; start now keys on
`DispatchCts == null` (a loop runs whenever there's ≥1 subscriber and none running). No behavioral change
besides the race fix; `Task.Run` in `StartDispatching` never takes the lock, so handlers can't deadlock.
**L286** (other — document): a destination is either pull-consumed (`ReadAsync`) or push-consumed
(`AddSubscriber`), never both — the dispatch loop drains the channel and would steal a pull caller's
messages. Documented on both methods. **L287** (test-gap): added the previously-missing delayed-send,
use-after-dispose, and handler-failure-isolation coverage. **Tests:** MessageQueue.Tests 82 → 85 —
`DelayedSend_DeliversAfterDelay` (deferred then delivered), `Producer_SendAfterDispose_ThrowsObjectDisposedException`,
`Dispatch_OneSubscriberThrows_OthersStillReceive`. Suite green: MessageQueue.Tests 85.

**Batch BF — Birko.MessageQueue.InMemory cluster (CR-L283, CR-L284):** Birko.MessageQueue.InMemory. Both
closed; **/code-review clean (no findings)**. **L283** (cleanup — "wire it in" option): `InMemoryMessageQueueOptions`
(documented as the config surface but never consumed) is now wired in via a new
`InMemoryMessageQueue(InMemoryMessageQueueOptions options, IMessageSerializer? serializer = null)` ctor that
delegates to the raw-capacity ctor (`?? throw` null-guard on options). Additive, no ambiguity with the
existing ctor (distinct first-param types). **L284** (cleanup): `InMemoryProducer.SendAsync<T>` set
`ContentType` twice (object-initializer + a conditional re-set) — collapsed to a single unconditional
`message.Headers.ContentType = _serializer.ContentType` after `Headers = headers ?? new MessageHeaders()`;
net behavior identical. **Tests:** MessageQueue.Tests → 82 — `SendTyped` stamps the serializer content type
both without caller headers and overriding caller headers (CR-L284); options ctor yields a working queue +
null-options `ArgumentNullException` (CR-L283). Suite green: MessageQueue.Tests 82.

**Batch BE — Birko.MessageQueue core cluster (CR-L280, CR-L281, CR-L282):** Birko.MessageQueue. All closed;
**/code-review clean (no findings)**. **L280** (bug — **already fixed, verify-first**): the
`RetryPolicy.GetDelay` `(long)Math.Pow(2, n-1)` overflow the audit describes was **already remediated under
CR-M199** (a medium finding) — the code computes `BaseDelay.Ticks * Math.Pow(...)` in double and saturates at
`MaxDelay` before the tick cast, and `RetryPolicyTests` already pins it (incl. an `int.MaxValue` attempt →
MaxDelay, never negative). No code change; closed as superseded. **L281** (test-gap): new
`DeadLetterOptionsTests` — default suffix appends `.dlq`, a custom suffix is used, an explicit `Destination`
overrides the suffix, and the defaults (Enabled/`.dlq`/null Destination). **L282** (nullable):
`MessageFingerprint.Compute(QueueMessage)` now guards the argument with `ArgumentNullException.ThrowIfNull`
before dereferencing `message.Body` (was an NRE on null); also guarded the two `string` overloads so a null
body/destination throws a clearly-named `ArgumentNullException` instead of the opaque one from
`Encoding.UTF8.GetBytes(null)`. **Tests:** MessageQueue.Tests → 78 — `MessageFingerprintTests` +3 null-guard
(message/body/destination), `DeadLetterOptionsTests` +4 (new file). Suite green: MessageQueue.Tests 78.

**Batch BD — Birko.Localization.Data cluster (CR-L277, CR-L278, CR-L279):** Birko.Localization.Data. All
closed; **/code-review clean (no findings)**. **L279** (nullable): `GetCultureTranslationsAsync` now writes
`dict[model.Key] = model.Value ?? string.Empty` — a persisted/deserialized `TranslationModel` with a null
`Value` no longer surfaces as a null value in the `IReadOnlyDictionary<string,string>` (which could NRE a
downstream consumer); the key was already null/empty-guarded, the value wasn't. **L278** (test-gap): the
existing cache tests never mutated the store between reads, so they couldn't distinguish a cache hit from a
fresh read (their own comments admitted it). Rewrote them to mutate a seeded model's `Value` behind the cache
(the cache copies string values, so a stale read proves a hit): cached value survives a store mutation until
`InvalidateCache`; `InvalidateCache()` reloads every culture; a new `Cache_ExpiresAfterTtl` (50ms TTL +
150ms delay) proves TTL expiry; `NoCaching` now mutates + asserts a fresh read each call. **L277** (test-gap):
added `GetTranslationAsync`/`GetAllAsync` pre-cancelled-token tests asserting `OperationCanceledException`
surfaces (the token is forwarded to the store, whose `EnsureInitializedAsync` throws on a cancelled token —
confirmed by the passing tests). **Tests:** Localization.Data.Tests → 28 (cache tests rewritten;
+2 cancellation). Suite green: Localization.Data.Tests 28.

**Batch BC — Birko.Localization cluster (CR-L273, CR-L274, CR-L275, CR-L276):** Birko.Localization. All
closed; **/code-review clean (no findings)**. **L273** (cleanup): dropped the dead
`string.IsNullOrEmpty(name) ? InvariantCulture : GetCultureInfo(name)` ternary in
`JsonTranslationProvider.GetSupportedCultures` — the preceding `.Where(!IsNullOrEmpty)` already filters empty
names, so the InvariantCulture branch was unreachable; empty file names (a bare `.json`) are skipped, not
mapped to Invariant. (Left `InMemoryTranslationProvider`'s equivalent ternary alone — it has no pre-filter,
so there it's live, CR-M197.) **L274** (cleanup): `Localizer.Resolve` now tracks `defaultAttempted` across
steps 1–2 so step 3 doesn't query the default culture a second time when it was reached as a parent (default
"sk" as parent of requested "sk-SK") — the old guard compared only the original culture to the default and
missed the default-as-parent case. **L275** (other — "document as English-only" option): `DateFormatter.FormatRelative`
now carries a `<remarks>` stating the relative phrasing is English-only by design; the culture param governs
only numeric/date formatting in the other overloads. **L276** (bug): the positional `StringInterpolator.Interpolate`
used `string.Format(template, args)` with the ambient thread culture, not the resolved translation culture —
threaded an `IFormatProvider` into both interpolator overloads (positional → `string.Format(provider,…)`;
named → format `IFormattable` values via the provider) and wired `Localizer.Get(args)` overloads to resolve
the culture once and pass it, so e.g. a decimal renders with the translation culture's separator. The
interpolator is internal + the params optional, so no caller breaks. **Tests:** Localization.Tests +6
(→ 146) — StringInterpolator positional+named de-DE decimal → comma; `Localizer` positional-args-in-resolved-culture
+ `Resolve_DefaultCultureReachedAsParent_NotQueriedTwice` (counting provider asserts exactly `["sk-SK","sk"]`);
Json `GetSupportedCultures` skips a bare `.json`; DateFormatter FormatRelative English regardless of culture.
Suite green: Localization.Tests 146.

**Batch BB — Birko.Helpers cluster (CR-L271, CR-L272):** Birko.Helpers. Both closed;
**/code-review clean (no findings)**. **L271** (bug): `CsvParser.Parse`'s trailing-row block (no final
newline) now applies the same `TrimEnd('\r')` the in-loop newline branches use, so a file whose last line
ends in a bare `\r` no longer emits a stray carriage return in the last field — the no-newline case is now
consistent with the CRLF case (verified the in-loop path already trims `\r`, incl. quoted fields, so this is
alignment not a new behavior). Also added a `CancellationToken ct = default` parameter observed via
`ct.ThrowIfCancellationRequested()` in the read loop, so a long parse over a slow stream can be cancelled
(matching BatchHelper's token convention); the optional param keeps all existing `Parse()` callers
(CsvProcessor, tests) source-compatible. **L272** (cleanup — accept-as-is per the audit): the obsolete
`EnumerableHelper.Diff` stays O(n*m) — it compares via an equality `Func<T,T,bool>` from which no hash key
can be derived, which is exactly why the O(n) `DiffByKey<T>` (key-selector based) exists as the replacement;
delegating is not feasible. Documented that on the method and pinned the previously-untested null-input
behavior. **Tests:** Helpers.Tests +5 (→ 97) — `Parse_TrailingRowEndingInBareCarriageReturn_TrimsIt`,
`Parse_CancelledToken_ThrowsOperationCanceled`, and `Diff` null-input matrix (both-null → all null;
null-source → all added; null-destination → all removed). Suite green: Helpers.Tests 97.

**Batch BA — Birko.Health.Redis cluster (CR-L269, CR-L270):** Birko.Health.Redis. Both closed;
**/code-review clean (no findings)**. **L269** (convention): `RedisHealthCheck.CheckAsync` now calls
`ct.ThrowIfCancellationRequested()` as its first line — StackExchange.Redis `PingAsync` has no
`CancellationToken` overload, but an already-cancelled token is honored before any work. Placed **before**
the `try` so the cancellation propagates to `HealthCheckRunner`'s timeout handling (which honors the
registration's `TimeoutStatus`) instead of being masked as a generic Unhealthy by the catch (cf. the CR-M191
Azure fix). **L270** (nullable/robustness): the `Func<IConnectionMultiplexer>` overload can legally return
null; added a `connection == null` guard returning `Unhealthy("Redis connection factory returned null.")`
instead of the obscure `NullReferenceException`-as-Unhealthy from dereferencing it. **Tests:**
Health.Redis.Tests 8 → 10 — `CheckAsync_AlreadyCancelledToken_ThrowsOperationCanceled` (also asserts the
factory is never invoked, proving the guard runs first) and `CheckAsync_FactoryReturnsNull_ReturnsUnhealthyWithClearReason`.
Suite green: Health.Redis.Tests 10.

**Batch AZ — Birko.Health.Data cluster (CR-L266, CR-L267, CR-L268):** Birko.Health.Data. All closed;
**/code-review clean (no findings)**. **L266** (other — "document" option taken): documented on the three
`TcpClient.Connected` reads (TcpHealthCheck, MqttHealthCheck TCP path, MongoDbHealthCheck's TCP-based
pingFunc) that after a successful `ConnectAsync` the socket is essentially always connected (a failed connect
throws into the catch), so the `!isConnected` branch only guards a rare post-connect drop — while noting the
branch stays meaningful for the custom-ping ctors (a ping may legitimately return false). No behavior change.
**L267** (other): `ElasticSearchHealthCheck` now reads the top-level `status` via `System.Text.Json`
(`JsonDocument`, `TryGetProperty`) in a new private `ReadClusterStatus` instead of substring-matching
`"status":"red"`/`"yellow"` — the old check depended on exact whitespace/field-order and could be fooled by a
nested object's status field. Tier mapping unchanged (red→Unhealthy, yellow→Degraded, else→Healthy); a
non-JSON/missing-status 200 still reads Healthy (parity with the old fall-through); adds `clusterStatus` to
the result data. **L268** (test-gap): added the empty-input + invalid-endpoint coverage that the sibling
checks had but CosmosDb/TimescaleDb lacked, plus TimescaleDb's untested port-range guard. **Tests:**
Health.Tests 82 → 91 — ES parsing via an offline `StubHttpMessageHandler` (spaced-JSON yellow → Degraded —
the case the old substring missed; red → Unhealthy; green → Healthy, each asserting `data["clusterStatus"]`),
`CosmosDbHealthCheck` empty-url guard + invalid-url Unhealthy, `TimescaleDbHealthCheck` empty-host guard +
port-too-low/too-high `ArgumentOutOfRangeException` + invalid-host Unhealthy. Suite green: Health.Tests 91.

**Batch AY — Birko.Health.Azure cluster (CR-L264, CR-L265):** Birko.Health.Azure. Both closed;
**/code-review clean (no findings)**. **L264** (cleanup): the near-identical `CheckAsync` bodies of
`AzureBlobHealthCheck` and `AzureKeyVaultHealthCheck` (stopwatch + latencyMs rounding + `>2000ms` Degraded
branch + data dict + cancellation-rethrow + catch-all) are extracted into a new internal
`AzureHealthCheckHelper.MeasureAsync(label, probe, ct, slowThreshold?)` (registered in the `.projitems`).
Both checks now delegate — passing their label and a lightweight-probe lambda — so the CR-M191
cancellation-rethrow lives in one place and future Azure checks (Service Bus, per the CLAUDE.md) don't
re-copy the boilerplate. Behavior is byte-for-byte preserved (identical messages, same `>2s` boundary via
`sw.Elapsed > threshold`, factory-throw → Unhealthy). **L265** (test-gap): the previously-untestable Healthy
and Degraded branches (no fake Azure client, probe not abstracted) are now unit-testable via the helper.
`AzureHealthCheckHelperTests` covers fast-probe → Healthy (with `latencyMs`), a real small delay + a
`slowThreshold: TimeSpan.Zero` → Degraded (deterministic, no 2s wait), probe-throws → Unhealthy (label in
message + Exception set), and probe-`OperationCanceledException` → rethrows (CR-M191). The helper's `internal`
visibility is fine — the shared project compiles into the test assembly. **Tests:** Health.Azure.Tests
12 → 16. Suite green: Health.Azure.Tests 16.

**Batch AX — Birko.Health cluster (CR-L261, CR-L262, CR-L263):** Birko.Health. All closed;
**/code-review clean (no findings)**. **L261** (nullable): `DiskSpaceHealthCheck.CheckAsync` replaced
`new DriveInfo(Path.GetPathRoot(_drivePath)!)` — which masked a genuinely-nullable value — with capturing
the root and a `string.IsNullOrEmpty` guard that returns a clear `Unhealthy("Invalid drive path: … has no
root.")` (a rootless bare relative path previously surfaced as a generic "Failed to check disk space" via
the outer catch; both Unhealthy, now clearer). Serves the no-nullable-warnings convention. **L262** (other,
config-validation): both checks now guard threshold ordering in the ctor (mirroring the existing drivePath
guard) — `DiskSpaceHealthCheck` throws `ArgumentException` when `critical >= warning` (disk Unhealthy must
trigger at *less* free space than Degraded), `MemoryHealthCheck` when `critical <= warning` (memory Unhealthy
at *higher* usage). An inverted config used to silently make the Degraded tier unreachable. Traced all
constructions (framework + tests + README): every existing caller uses the valid defaults or valid explicit
values, so the guards break nothing. **L263** (test-gap): the runner tests only covered the default
Unhealthy-on-timeout path; added `RunAsync_CheckTimesOut_WithDegradedTimeoutStatus_ReportsDegraded` — a slow
check registered with `timeoutStatus: HealthStatus.Degraded` + a 50ms timeout comes back Degraded (entry +
aggregate report). **Tests:** Health.Tests 78 → 82 — `DiskSpaceHealthCheck_RootlessPath_ReturnsUnhealthy`,
`DiskSpaceHealthCheck_CriticalNotBelowWarning_ThrowsArgumentException`,
`MemoryHealthCheck_CriticalNotAboveWarning_ThrowsArgumentException`, and the Degraded-timeout runner test.
Suite green: Health.Tests 82.

**Batch AW — Birko.EventBus.Outbox cluster (CR-L259, CR-L260):** Birko.EventBus.Outbox. Both closed;
**/code-review clean (no findings)**. **L259** (cleanup, efficiency): the background loop ran a full retention
`CleanupAsync` scan/delete on **every** poll (default PollingInterval 5s), though cleanup is a coarse
retention prune (default 7 days). Added `OutboxOptions.CleanupInterval` (default 1 hour) and a new
`OutboxProcessor.CleanupIfDueAsync` that throttles the prune to that cadence (first call always runs; tracks
`_lastCleanupUtc`); the hosted loop now calls `CleanupIfDueAsync` instead of `CleanupAsync`. The processor
takes an optional `IDateTimeProvider clock` (defaults to `SystemDateTimeProvider`, reusing the framework's
established clock pattern — same as `InMemoryDeduplicationStore`) so the cadence is deterministically
testable; `CleanupAsync`'s cutoff now uses the injected clock too. Cadence state lives in the singleton
processor, touched only by the single background loop (documented — no synchronization needed). **L260**
(cleanup — **ordering premise corrected by verification**): the DI factory resolved `IEventBus` **twice** in
the un-decorated path (`… as OutboxEventBus` then `?? sp.GetRequiredService<IEventBus>()`); resolve it once
into a local and unwrap via `bus is OutboxEventBus ob ? ob.Inner : bus`. The audit's "AddOutbox must be
called after AddOutboxEventBus, otherwise the processor loops back into the outbox" concern is **overstated**:
the processor factory is a **lazy** `sp => …`, so it resolves `IEventBus` at runtime after all registrations
— `Decorate` has already replaced the descriptor regardless of call order, so the unwrap always finds the
decorator and publishes through the real inner bus. Documented the true (order-independent) behavior on
`AddOutbox` via `<remarks>` and fixed its inaccurate summary (it does NOT wrap the bus — that's
`AddOutboxEventBus`). **Tests:** EventBus.Tests 102 → 108 — `OutboxProcessorCleanupCadenceTests` (first-call
runs; within-interval skips; after-interval re-runs; 20 polls over 100s clean up once — pinned via
`TestDateTimeProvider`), `OutboxDiRegistrationTests` (both registration orders → processor publishes through
the inner bus exactly once, no new pending entry, entry ends Published). Suite green: EventBus.Tests 108.

**Batch AV — Birko.EventBus.MessageQueue cluster (CR-L256, CR-L257, CR-L258):** Birko.EventBus.MessageQueue.
All closed; **/code-review clean (no findings)**. **L256** (other — "document the requirement" option taken):
added a `<remarks>` on `DistributedEventBus.Subscribe<TEvent>` stating the non-obvious coupling — a manually
subscribed handler is only ever invoked from inside the `SubscribeToTransportAsync<TEvent>` delivery callback
(via `GetHandlers`), so `Subscribe` alone silently receives nothing until a matching transport subscription
exists (call it, or let `AutoSubscriber`/`DistributedEventBusHostedService` create it from DI). Auto-wiring
the transport from `Subscribe` was deliberately NOT done: `Subscribe` is sync and the transport subscribe is
network-bound async, and CR-M188 removed sync-over-async from this class to avoid deadlocks. **L257**
(cleanup — **defensive-only, premise narrowed by verification**): added
`typeof(IEvent).IsAssignableFrom(eventType)` alongside `eventType.IsClass` in `AutoSubscriber.DiscoverEventTypes`
before `MakeGenericMethod`. The audit's failure scenario (a class implementing `IEventHandler<T>` with T not
IEvent → `ArgumentException` at `MakeGenericMethod`) is actually **unreachable**: `IEventHandler<in TEvent>`
already constrains `where TEvent : IEvent`, so no closed handler type can carry a non-IEvent T — a direct
negative test can't even be declared. Kept the guard as belt-and-suspenders (robust if that interface
constraint ever loosens; documents the `class, IEvent` requirement the reflection path enforces). **L258**
(test-gap): three new test files + one error-isolation test. `AutoSubscriberTests` (SubscribeAllAsync
subscribes a DI-registered handler type so a published event is dispatched; a handler type NOT registered in
DI yields no subscription — which also demonstrates L256: a manual `Subscribe` alone is inert).
`DistributedEventBusHostedServiceTests` (AutoSubscribe=false short-circuits — no subscription; AutoSubscribe=true
auto-subscribes; the `IEventBus`→`DistributedEventBus` cast-failure throws `InvalidOperationException` via a
`NotADistributedEventBus` stub; StopAsync no-op). `AddDistributedEventBusTests` (singleton `IEventBus` +
options wiring; `IHostedService` registered only when AutoSubscribe=true). Error isolation: a throwing handler
doesn't stop delivery to the other handlers (CR-H114 semantics). **Tests:** EventBus.Tests 93 → 102. Suite
green: EventBus.Tests 102.

**Batch AU — Birko.EventBus.EventSourcing cluster (CR-L254, CR-L255):** Birko.EventBus.EventSourcing.
Both closed; **/code-review clean (no findings)**. Both findings were **partly pre-addressed by CR-M186**
(which had already fixed the OccurredAt/EventId drop the audit detail still described as open and shipped a
`DomainEventPublishedTests.cs` covering the field mapping) — verify-first narrowed the remaining gaps.
**L254** (nullable): added the missing `if (domainEvent is null) throw new ArgumentNullException(nameof(domainEvent))`
guard clause as the first line of `DomainEventPublished(DomainEvent)` — the ctor dereferences `domainEvent`
immediately, and it's reachable via `EventStoreEventBus.PublishDomainEventAsync` if an inner store's
`AppendRange`/replay `IEnumerable` ever yields a null element; now matches the project's own null-guard
convention (EventStoreEventBus/EventReplayService both `?? throw`). **L255** (test-gap): the CR-M186 test
already asserted AggregateId/Version/type/data/Metadata/UserId + OccurredAt/EventId, so the remaining gaps
were the `Source == "event-sourcing"` field (added an assertion to the existing mapping test) and the
null-argument behavior (new `Constructor_NullDomainEvent_ThrowsArgumentNullException` pinning the L254 guard
with `WithParameterName("domainEvent")`). **Tests:** EventBus.Tests 92 → 93. Suite green: EventBus.Tests 93.

**Batch AT — Birko.EventBus core cluster (CR-L248 … CR-L253):** Birko.EventBus. All closed;
**/code-review clean (no correctness bugs; one deliberate documented semantic change)**. **L248** (bug,
concurrency): added `TryMarkProcessedAsync` to `IDeduplicationStore` as a **default interface method**
(non-breaking; DIM fallback = Exists+Mark for existing implementers), overridden atomically in
`InMemoryDeduplicationStore` (`ConcurrentDictionary.TryAdd`). `DeduplicationBehavior` reserves the EventId
**before** running handlers — chose **mark-before / at-most-once** (dedup = skip-duplicates guard): fixes
the two-concurrent-publishes lost-update race and the throw-before-mark reprocess window; documented that a
throwing handler won't be reprocessed. **L249** (bug, thread-safety): `InMemoryDeduplicationStore` cleanup
bookkeeping moved from an unsynchronized `DateTime _lastCleanup` to a `long _lastCleanupTicks` with
`Interlocked.Read` + `CompareExchange` slot-claim, so only one thread sweeps per interval. **L250** (bug,
thread-safety): `InProcessEventSubscription` dispose guard is now an atomic `Interlocked.Exchange` on an
`int _isActive` (+ `Volatile.Read` for `IsActive`), so concurrent/repeat disposes call `_unsubscribe`
exactly once. **L251** (convention — **premise corrected by verification**): the audit claimed
DefaultTopicConvention's `GetTopic(IEvent)` didn't implement the interface DIM, but a probe proved it DOES
(implicit implementation), so there's no split there. The real gap was `AttributeTopicConvention` (only
implemented `GetTopic(Type)`, so its event-based path ignored `Source` for attribute-less events) — gave it
a `GetTopic(IEvent)` (explicit `[Topic]` wins; else source-aware fallback), the finding's "uniform
participation" intent. Zero blast radius (the distributed bus routes via `GetTopic(Type)`). **L252** (bug):
`RuleFilterBehavior.BuildDefaultContext` now skips indexer properties and try/catches each getter, so an
event with an indexer or a throwing getter can't fail the whole publish pipeline. **L253** (cleanup):
`DefaultTopicConvention` kebab-case regex is now a `static readonly Regex` (Compiled) built once instead of
a per-call inline `Regex.Replace`. **Tests:** EventBus.Tests 84 → 92 — dedup (TryMark atomic reserve,
mark-before dedups a throwing handler, cleanup eviction via TestDateTimeProvider), topic conventions
(Default via interface is source-aware; AttributeConvention event-based source-aware + attribute-wins),
robustness (idempotent double-dispose stops delivery; rule filter tolerates indexer + throwing getter).
Suite green: EventBus.Tests 92.

**Batch AS — Data.XML + XML.ViewModel cluster (CR-L244, CR-L245, CR-L246, CR-L247):** Birko.Data.XML,
Birko.Data.XML.ViewModel (+ Birko.Data.JSON.ViewModel same-defect sweep). All closed;
**/code-review clean (no correctness bugs)**. **L244** (bug): aligned the sync single-file bulk
`AbstractXmlStore.CreateCore(IEnumerable)` with the async sibling — `item.Guid ??= Guid.NewGuid()`
(preserve caller Guid, was discarded), `_items[…] = item` (indexer upsert, was `_items.Add` which threw
`ArgumentException` on a duplicate key), + a `data == null` guard for parity. Cross-file trace confirmed
scope: `XmlBatchStore.CreateCore` already used preserve+indexer, `XmlSeparateStore.CreateCore` delegates
per-item to the single-item path (preserve + `.Add`, the framework's single-item convention) — only the
single-file base store was defective. **L245** (convention): documented the deliberate sync-eager vs
async-lazy `SetSettings` divergence (accept-as-is) — the sync `*Core` methods read `_items` directly (no
lazy data-load hook) so it must load eagerly; the async `*CoreAsync` all await `EnsureDataLoadedAsync`, so
SetSettings defers (a sync SetSettings shouldn't block on I/O). Unifying would mean reworking the whole
sync CRUD path to lazy — disproportionate for a low finding; both `SetSettings` now carry a `<remarks>`.
**L246** (convention): replaced the misleading `base(null)` + conditional-assign + "creates default"
comment with a private static `ValidateStore` helper. Async repos use `base(ValidateStore(store))`
(base param is `IAsyncStore`, matches); sync repos keep `base(null)` then `Store = ValidateStore(store)`
— required because the sync bulk base ctor takes `IBulkStore<TModel>?` while the ctor accepts any `IStore`
(incl. a wrapper), which can't be passed to that base. **Swept the JSON.ViewModel sibling** the audit
explicitly flagged (identical defect). **L247** (test-gap): created **Birko.Data.XML.ViewModel.Tests**
(git-init'd + registered in `.slnx`/`.code-workspace`; mirrors JSON.ViewModel.Tests from CR-L133) —
`XmlRepositoryUnwrapTests` covers accept-raw / resolve-through-wrapper / reject-foreign / accept-null for
sync + async. **Tests:** XML.Tests 16 → 18 (`XmlBulkCreateGuidTests`: caller-Guid preserved on round-trip,
duplicate-Guid upsert doesn't throw), XML.ViewModel.Tests 7 (new), JSON.ViewModel.Tests 5 (sweep regression,
unchanged). Suites green: XML.Tests 18, XML.ViewModel.Tests 7, JSON.ViewModel.Tests 5.

**Batch AR — Data.Views cluster (CR-L240, CR-L241, CR-L242, CR-L243):** Birko.Data.Views. All closed;
**/code-review clean (no correctness bugs; 1 low docs note + 1 out-of-cluster analogue, both handled)**.
**L240** (other, robustness): `RegisterFromAssembly` now routes `assembly.GetTypes` through a new
`internal static IEnumerable<Type> GetLoadableTypes(Func<Type[]>)` seam that catches
`ReflectionTypeLoadException` and falls back to `ex.Types.Where(t => t != null).Select(t => t!)`, so one
unloadable type (missing optional dependency) can't hard-fail startup discovery. Exposed as an injectable
seam — the Views shared project compiles into the test assembly, so the internal is testable without a
purpose-built broken assembly. **L241** (other, consistency): removed the `AggregateFunction.Count`
short-circuit in the aggregate validator so the target-property existence check now runs for **every**
aggregate (Count included, was skipped — inconsistent vs Min/Max); the numeric-type constraint stays gated
to Sum/Avg. Renamed `ValidateNumericAggregates` → `ValidateAggregates` (single private caller). Via the
type-safe fluent API a Count's ViewProperty always resolves from a real member expression, so the negative
path isn't reachable publicly — defensive-consistency, valid Count builds stay green. **L242** (nullable):
`new[] { builder }` → `new object[] { builder }` (builder is guarded non-null) — explicit element type,
nullable-strict clean. **L243** (test-gap): new `ViewMapRegistryEdgeCaseTests` pins the silent last-wins
overwrite on duplicate `TView` registration and the L240 RTLE fallback deterministically via the
`GetLoadableTypes` seam (throwing delegate with a null slot → only loaded types; no-exception passthrough;
happy-path RegisterFromAssembly regression). From /code-review: added a CLAUDE.md note on the
last-wins + RTLE-tolerant behaviors. **Deferred analogue (record-and-defer, out of cluster):**
`Birko.Models.SQL/Mapping/ModelMapRegistry.cs:22` has the identical bare `assembly.GetTypes()` (same gap
the audit flagged) — different project/cluster, no open CR-L; noted for a future pass. **Tests:**
Views.Tests 32 → 36 (`ViewMapRegistryEdgeCaseTests` ×4). Suite green: Views.Tests 36.

**Batch AQ — Data.ViewModel cluster (CR-L237, CR-L238, CR-L239):** Birko.Data.ViewModel. All closed;
**/code-review: 1 real finding (a CR-id mislabel), fixed in-batch; no correctness bugs**. **L237**
(cleanup): dropped the dead `.Where(m => m != null).ToList()!` from the async bulk repo's
`CreateAsync`/`UpdateAsync`/`DeleteAsync(IEnumerable)` — `LoadModelInstance` returns a non-null TModel
(`CreateModelInstance` → `Store.CreateInstance()` / `Activator.CreateInstance<TModel>()`), so the
filter never fired and the `ToList()!` was a needless snapshot; the projection is now passed lazily,
matching the already-lazy sync bulk sibling (`AbstractBulkViewModelRepository`, which never had the
defect). **L238** (docs): the README + CLAUDE.md advertised `ViewModel<T>` / `ModelViewModel<TVm,TModel>`
/ `AbstractLogViewModel` / `LogViewModel` under a `ViewModels/` folder + a `LoadAsync`/`SaveAsync`
usage example — none of which exist here (the projitems compiles only the 8 Repositories files). Those
base classes DO exist but in **Birko.Data.Core** (namespace `Birko.Data.ViewModels`, and they are
**non-generic**). Rewrote both docs: this project ships the repository abstractions only, the base
classes are pointed at Birko.Data.Core, the phantom folder is gone from the architecture tree, and the
example is now buildable (consumer VM `: ModelViewModel, ILoadable<Product>`, concrete repo overriding
`MapToModel`, real `CreateInstance`/`CreateAsync` API). **L239** (convention): replaced the CLR
corrupted-state `AccessViolationException` (uncatchable under `legacyCorruptedStateExceptionsPolicy`)
with `InvalidOperationException` at **all 18** read-mode guard sites across the 4 repos — the audit
named the 6 single-item sites; the bulk siblings carried the identical CR-M180 defect and were fixed
for consistency. Cross-file trace confirmed no framework/test source catches or asserts the old type
(no downstream breakage). **/code-review** found the dead-filter work (CR-L237) had been mislabeled
`CR-L239` in three source comments + two test artifacts (CR-L239 is the exception finding) — corrected.
**Tests:** Data.ViewModel.Tests 11 → 17 — `SingleItemViewModelRepositoryReadModeTests` (the audit's
primary sync+async single-item read-mode guards → InvalidOperationException) + `AsyncBulkViewModelRepositoryProjectionTests`
(Create/Update/Delete bulk round-trips prove no items dropped by the lazy projection); existing
`BulkViewModelRepositoryReadModeTests` flipped to InvalidOperationException. Downstream SQL.ViewModel.Tests
11 green (consumer of the fixed abstract bases). Suites green: Data.ViewModel.Tests 17, SQL.ViewModel.Tests 11.

**Batch AP — Data.TimescaleDB.ViewModel cluster (CR-L234, CR-L235, CR-L236):** Birko.Data.TimescaleDB.ViewModel
(+ same-defect extras in Birko.Data.TimescaleDB and Birko.Data.ViewModel). All closed; **/code-review:
4 findings, all fixed in-batch — including one significant base-layer bug**. **L234** (bug): removed the
`DestroyAsync` override (`base.DestroyAsync` + `DropAsync`) from the ViewModel `AsyncTimescaleDBRepository`
AND its model-repo sibling `AsyncTimescaleDBModelRepository` (Batch AO had left it) — the repo base already
destroys through the store and `AsyncDataBaseStore.DestroyAsync` IS a table drop, so the override dropped
the table a second time via the unwrapped connector, bypassing wrappers (Mongo CR-L155/L156 precedent);
`DropAsync` stays as the explicit helper. Behavioral change: `DestroyAsync` on an unconfigured repo no
longer throws (the trailing `DropAsync` used to). **The /code-review cross-file trace then found the
deeper instance:** `AbstractAsyncBulkViewModelRepository.DestroyAsync` (Birko.Data.ViewModel) still had
the CR-H080 double-destroy shape — `base.DestroyAsync` → `Store.DestroyAsync`, then `BulkStore.DestroyAsync`
where `BulkStore` is `Store as IAsyncBulkStore` (the SAME instance) — CR-H080 had fixed only the
model-side `AbstractAsyncBulkRepository`. Removed that override too (so ViewModel-repo destroys went
3→1 drops, not 3→2; unsafe for non-idempotent stores: double-dispose/double-close). Sync siblings
verified clean (no overrides). **L235** (cleanup): the ViewModel repo gets the same `RequireConnector()`
capture-once helper as Batch AO — each `Connector` read re-walks the wrapped-store unwrap chain, and the
guard-then-use pattern traversed it twice per call. **L236** (other — accepted as-is per the audit):
Task.Run-wrapped sync connector calls give the token cancel-before-start semantics only; documented on
both repos' `RequireConnector()` AND in the public methods' `<param name="ct">` docs (IntelliSense-visible,
per /code-review). **Tests:** TimescaleDB.ViewModel.Tests 5 → 7 (structural no-override pin + the
unconfigured-Destroy behavioral change), TimescaleDB.Tests 19 (model-repo pin), Data.ViewModel.Tests
9 → 11 (`BulkViewModelRepositoryDestroyTests` — a counting store proves exactly ONE `DestroyAsync` per
repository destroy + structural pin; new CLAUDE.md for the project, was missing). Downstream
SQL.ViewModel.Tests 11 green (consumer of the fixed base). Suites green: TimescaleDB.ViewModel 7,
TimescaleDB 19, Data.ViewModel 11, SQL.ViewModel 11.

**Batch AM-bis — repo-wide shared-project GUID sweep (user-authorized follow-up to CR-L227):** DONE.
The Batch AM /code-review found the fake-GUID pattern was much wider than the one tracked finding:
five Views projects carried non-hex GUIDs (`…-view00000001`, `…-sqlview00001`, `…-esview00001`,
`…-rvnview0001`, `…-cdbview0001`) and EIGHT valid-hex GUIDs were each shared by TWO different projects
(Security.Vault.Configuration↔Security.AzureKeyVault, Data.Sync.RavenDB↔Communication.WebSocket,
Data.Aggregates↔Time, Data.Sync.MongoDb↔Communication.Bluetooth, Data.Sync.Sql↔Communication.Hardware,
Models.Customers↔Communication.REST.Server, Telemetry.OpenTelemetry↔Data.CosmosDB,
Data.Sync.Tenant↔Communication.Network) — GUID-keyed tooling could bind the wrong project. With user
authorization, 13 projects were regenerated (`projitems SharedGUID` + `shproj ProjectGuid` in
lockstep; for each collision pair the side NOT pinned by `Id=` in `Birko.Framework.slnx` was changed —
the four pinned Communication entries kept theirs). Verified post-sweep: every GUID now appears
exactly twice (its own pair), all are shape-valid hex, no stale old-GUID references anywhere in
framework/tests/slnx/workspace/aggregator, and all pairs match. One commit per repo (13 repos).
GUIDs are VS-project-system metadata only — MSBuild imports .projitems by path — so no build impact.
(A planned smoke build was skipped: the machine's .NET 10 host/runtime disappeared mid-session —
hostfxr max 8.0.29 with 10.x SDKs orphaned on disk — an environment issue unrelated to this change;
reported to the user.)

**Batch AO — Data.TimescaleDB cluster (CR-L232, CR-L233):** Birko.Data.TimescaleDB. Both closed;
**/code-review: 4 findings — 3 fixed in-batch, 1 deferred**. **L232** (nullable): both
`TimescaleDBConnector` constructors now throw a clear `ArgumentNullException` on null settings — the
typed ctor via a throw-expression in the `base(...)` call, the RemoteSettings path via a guard at the
top of `AsTimescaleSettings` (which used to NRE on `settings.Location`). **L233** (cleanup,
verify-first — the finding was partly stale): the copy-pasted `if (Connector == null) throw ...`
guards are consolidated into a private `RequireConnector()` per class (AsyncTimescaleDBStore 3 sites,
AsyncTimescaleDBModelRepository 4 sites; messages unified to "…Call SetSettings() first."). The
audit's third site — `Repositories\AsyncTimescaleDBRepository.cs` — turned out to be a **never-compiled
bit-rotted copy**: absent from the `.projitems`, no longer implementing the base's abstract
`MapToModel`, superseded by the abstract ViewModel repositories in Birko.Data.TimescaleDB.ViewModel;
both dead copies (sync + async) were DELETED rather than registered (registering them broke the build
— CS0534). From /code-review: fixed the stale project CLAUDE.md/README (both still advertised the
deleted repos + never-existing `*BulkStore`/`*BulkRepository` types; CLAUDE.md's example used a
nonexistent `base(settings)` ctor and the wrong `TimeColumn` default), and dropped the initially-written
reflection pin for the deletion (the legitimate ViewModel classes share name+namespace — it would
false-fail under aggregator imports). **Deferred (altitude, needs its own pass):** the same
`Connector == null` throw is copy-pasted in AsyncMySQLStore/AsyncMSSqlStore/AsyncPostgreSQLStore/
AsyncSQLiteStore — a `protected DB RequireConnector()` on `AsyncDataBaseStore<DB,T>` would fix all
five backends once; spans four already-closed clusters, so record-and-defer. **Tests:**
TimescaleDB.Tests 14 → 18 (`TimescaleDBGuardTests`: both ctor null-guards ArgumentNullException-not-NRE;
unconfigured store + model-repo schema methods fail fast with the "Call SetSettings" message). Updated
the test project CLAUDE.md (was a 10-line stub with a wrong path; now documents scope per suite).
Suite green: TimescaleDB.Tests 18.

**Batch AN — Data.Tenant cluster (CR-L229, CR-L230, CR-L231):** Birko.Data.Tenant. All closed;
**/code-review: 4 findings, all fixed in-batch**. **L229** (other — "confirm fail-open + test" option
taken): `BelongsToCurrentTenant`'s no-tenant behavior is confirmed as a deliberate FAIL-OPEN
("non-tenant/admin mode") and documented with a `<remarks>` on both wrappers naming the flip side (a
mis-wired context falling back to the static `Models.Tenant.Current` singleton opens cross-tenant
writes rather than failing closed) — and, from /code-review: the remark's "derive and override" escape
hatch was impossible (the method was non-virtual, and the bulk wrappers bind
`items.All(BelongsToCurrentTenant)` as a method group), so the method is now `protected virtual` on
both wrappers, with an override test proving dispatch from single-item AND bulk call sites. The
project CLAUDE.md (×2 lines) + README claimed the UnauthorizedAccessException guard unconditionally —
now state the no-tenant fail-open explicitly. **L230** (bug): added `using System.Linq;` to
`TenantMiddleware.cs` — `FirstOrDefault()` over `StringValues` is a LINQ extension, and shared
.projitems source must compile in consumers with ImplicitUsings disabled (swept the project: no other
file has the latent defect). **L231** (cleanup): removed the dead `using Birko.Configuration;` from all
five store files (no Settings usage anywhere, incl. crefs). **Tests:** Tenant.Tests 16 → 23 —
`TenantFailOpenTests`: fail-open pinned by actual writes (foreign rows seeded via the inner store;
update mutation + delete removal asserted by read-back — /code-review caught that NotThrow-only
assertions couldn't distinguish fail-open from silently-skipped), fail-closed matrix (foreign item →
throw; one foreign item poisons a bulk batch; sync + async), and the virtual-override fail-closed
escape hatch. Also created the missing Birko.Data.Tenant.Tests CLAUDE.md. Suite green: Tenant.Tests 23.

**Batch AM — Data.Tagging cluster (CR-L226, CR-L227, CR-L228):** Birko.Data.Tagging. All closed;
**/code-review: 4 actionable findings — 3 fixed in-batch, 1 declined, plus the deferred GUID sweep
above**. **L226** (cleanup — "accept as deliberate" option taken): `AttachTagByNameAsync`'s create path
keeps routing through `CreateTagAsync` (whose second `FindTagByNameAsync` narrows the TOCTOU window —
a concurrently created same-name tag is returned instead of inserted twice; this layer has no
unique-name constraint to fall back on) and now carries a comment saying so ("don't optimize this to
`CreateTagInternalAsync`"). Pinned by a deterministic race test: `InMemoryTagService` gains an
`AfterFindTagByName` callback + `SeedTag` helper, and the new
`AttachTagByName_ConcurrentCreateBetweenMissAndInsert_ReusesRacedTag` injects a raced tag between the
miss and the re-check (raced tag reused, `CreateTagCalls == 0`, link still created). **L227**
(convention): replaced the unparseable `SharedGUID`/`ProjectGuid`
`a1b2c3d4-e5f6-4a7b-8c9d-tag000000001` ('tag' is not hex) with a real GUID
`eff6b1e6-…` in `.projitems` + `.shproj` (lockstep; slnx/workspace reference by path — grep-verified no
other reference to the old string). The /code-review sweep found the pattern is repo-wide (5 more
non-hex + 8 cross-project duplicates) — deferred, see the authorization note above. **L228** (other,
docs): the "All operations are tenant-scoped" claim now states the real division of labor in all the
places an implementor looks — `ITagService` XML doc, a TENANT-SCOPING CONTRACT comment on the abstract
hooks region in `TagServiceBase`, CLAUDE.md Patterns, README — the base stamps `TenantGuid` on inserts,
but the read/delete hooks carry no tenant parameter, so **implementations MUST tenant-filter every
hook** (incl. `GetTagByIdAsync`); enforcement-by-signature was declined as breaking (the hooks are
implemented by external consumers, e.g. Symbio). The four-place restatement was flagged by /code-review
as duplication and deliberately kept (audit's own fix option; different audiences). Also from
/code-review: fixed stale Tagging CLAUDE.md model claims (`Tag`/`EntityTag` derive from
`AbstractLogModel`, `Group` renamed `TagGroup` long ago) and created the missing
Birko.Data.Tagging.Tests CLAUDE.md. **Tests:** Tagging.Tests 11 → 12. Suite green: Tagging.Tests 12.

**Batch AL — Data.Sync.Xml (CR-L225 + a Cosmos audit-gap extra):** Birko.Data.Sync.Xml (+
Birko.Data.Sync.CosmosDB). Closed; **/code-review: 4 findings, all fixed in-batch**. **L225** (cleanup):
removed the dead `int Id` property (`[XmlElement("Id")]`) from `XmlSyncKnowledgeItem` — copied from
`SqlSyncKnowledgeItem` where it is a DB auto-increment (`[IncrementField]`), but XML has no increment
source: never assigned by `CreateKnowledgeItem`, never read anywhere (framework + tests grep-clean),
not in `ISyncKnowledgeItem`; identity is Guid/EntityGuid (same removal as JSON CR-L213 / Mongo CR-L216 /
ES CR-L212 / Raven CR-L219). Legacy files carrying `<Id>0</Id>` still load — XmlSerializer ignores
unknown elements — pinned by a test that injects `<Id>7</Id>` into a real store file and re-reads it.
**Audit-gap extra (Cosmos):** the /code-review altitude pass found `CosmosSyncKnowledgeItem.InternalRecordId`
("for database compatibility") is the same dead pattern — never set/read, serialized `InternalRecordId:0`
into every document — and NOT tracked by any audit finding (all Cosmos entries closed); removed it too
(the Cosmos analogue of Raven's L219; unmapped JSON members are ignored on read, so existing docs stay
readable — no Mongo-style guard needed). Also from /code-review: the legacy-file test now locates the
store file via the public `GetPath()` (as the JSON sibling tests do) instead of an over-broad
`EnumerateFiles(...).Single()` glob; removed two dead usings (`Birko.Data.Stores`, `Birko.Configuration`)
from `AsyncXmlSyncKnowledgeStore` (JSON-copied, same as L221); fixed the stale Sync.Xml.Tests CLAUDE.md
scope bullet (wrong class name `AsyncJsonSyncKnowledgeStoreTests` + missing new coverage) and created the
missing Sync.CosmosDB.Tests CLAUDE.md (convention requires one per test project). **Tests:** Sync.Xml.Tests
5 → 7 (`Model_HasNoVestigialIdField` reflection guard; `LoadData_LegacyFileWithIdElement_StillDeserializes`),
Sync.CosmosDB.Tests 7 → 8 (`Model_HasNoDeadInternalRecordId`). Suites green: Sync.Xml.Tests 7,
Sync.CosmosDB.Tests 8.

**Batch AK — Data.Sync.Tenant cluster (CR-L222, CR-L223, CR-L224):** Birko.Data.Sync.Tenant. All closed;
**/code-review clean (no findings)**. **L222** (bug — cancellation): in `ExecuteSyncAsync` the
`IsCancellationRequested` check only `break`s the inner item `foreach`; the outer batch `for` kept iterating
(firing `OnBatchCompleted` for every remaining batch), so cancellation didn't stop the sync promptly. Added a
cancellation break at the top of the outer `for` (graceful-partial semantics preserved — the post-loop
knowledge-persist still runs on the processed set). Additionally `ApplyConflictResolutionAsync` didn't receive
the token, so its two `UpdateAsync` calls ran uncancellable during conflict resolution — added an optional
`CancellationToken` param (source-compatible with the existing test call), forwarded from the call site and
into both writes. **L223** (efficiency): `GetUpdatedAt` called `typeof(T).GetProperty("UpdatedAt")` on every
invocation (twice per `GetVersionHash`, plus the conflict/newest paths) — a per-item hot-path reflection
lookup. Cached the resolved PropertyInfo in a `private static readonly PropertyInfo? _updatedAtProperty` (the
static-method equivalent of the ctor-cached `_guidProperty`, since these methods are static + called
statically by tests); the DateTime/DateTime? guard is baked into the one-time resolver, so a non-matching
property caches as null. **L224** (cleanup): removed the redundant `new EnqueueAsync(scope, op, ct)` shadow on
`TenantSyncQueue` — it re-declared the inherited `SyncQueue.EnqueueAsync`, which already keys via the virtual
`GetQueueKey(scope)` this class overrides, so it added no behavior and introduced a member-hiding footgun.
Verified safe: no caller uses the 3-arg form on a `TenantSyncQueue` reference, and the context case is
equivalently expressed via the tenant-explicit overload with `tenantGuid: null` (identical `"{scope}_{tenant}"`
key). **Tests:** Sync.Tenant.Tests 12 → 17 — `Cancellation_StopsOuterBatchLoop_NotJustInnerLoop` (exactly 1
`OnBatchCompleted` after cancelling in the first batch, was 3), `ConflictResolution_ForwardsCancellationToken_ToUpdate`
(a cancelled token makes the conflict write throw `OperationCanceledException`), a direct `GetUpdatedAt`
DateTime/DateTime?/null/absent matrix, and a new `TenantSyncQueueTests` (reflection-asserts the `new` overload
is gone + the null-tenant context enqueue runs). Suite green: Sync.Tenant.Tests 17.

**Batch AJ — Data.Sync.Sql cluster (CR-L220, CR-L221):** Birko.Data.Sync.Sql. Both closed;
**/code-review clean (no findings)**. **L220** (other, docs — SQL analogue of JSON CR-L214): documented the
derived-timestamp / empty-scope-no-op contract on both the async `Get`/`SetLastSyncTimeAsync` and the sync
`Get`/`SetLastSyncTime` — last-sync-time is the max `LastSyncedAt` over a scope's rows, and Set only
refreshes existing rows, so stamping an **empty** scope persists nothing (Set echoes the value; a later Get
stays null → initial-sync). Kept the deliberate cross-backend design (shared with the JSON reference): the
sync provider always persists the round's rows before stamping, so a stamp only ever lands on a populated
scope; the doc also notes the abstract `ISyncKnowledgeStore.SetLastSyncTimeAsync` takes a non-nullable time
whereas this per-item overload takes `DateTime?` and short-circuits on null. **L221** (cleanup): removed the
unused `using Birko.Data.Stores;` and `using Birko.Configuration;` from both store files (build-verified —
the base classes come from `Birko.Data.SQL.Stores`; nothing from those namespaces is referenced; the dead
imports were copied from the JSON reference). **Tests:** Sync.Sql.Tests 6 → 7
(`SetLastSyncTime_EmptyScope_IsANoOp_AndGetStaysNull` — a live on-disk SQLite store proves an empty-scope
stamp is echoed back but a subsequent Get returns null). Suite green: Sync.Sql.Tests 7.

**Batch AI — Data.Sync.RavenDB cluster (CR-L217, CR-L218, CR-L219):** Birko.Data.Sync.RavenDB. All closed;
**/code-review clean (no findings)**. **L217** (convention): the synchronous `RavenSyncKnowledgeStore`
returned `Dictionary`/`void`/`DateTime?` from seven methods misleadingly named `*Async`. Renamed to their
true synchronous names (`GetKnowledge`, `GetKnowledgeItem`, `UpdateKnowledge`, `UpdateKnowledgeItem`,
`DeleteKnowledge`, `GetLastSyncTime`, `SetLastSyncTime`) — mirroring the CosmosDB sync sibling; no external
callers (framework + tests grep-clean, only internal delegation). The accepted `CancellationToken` is now
honored via `ThrowIfCancellationRequested()` at each session-opening entry point (was accepted-but-ignored).
The async store keeps its (correct) `*Async` names. **L218** (cleanup): removed the dead private
`ConvertToRavenItemAsync` (only wrapped the sync `ConvertToRavenItem` in `Task.FromResult`, never called),
the dead `RavenSyncKnowledgeItem.CollectionName` const, and the dead `GenerateDocumentId` helper — the latter
two are genuinely superseded, since the duplicate-document bug was fixed via `DeterministicGuid` (CR-H103),
not `GenerateDocumentId`; RavenDB resolves the collection from the type name (no CollectionName convention).
Pared both store files' usings to only those referenced (dropped `System.Linq.Expressions`,
`Birko.Configuration`, `Birko.Data.Stores`, `Birko.Data.Sync.Stores`, `Birko.Data.Repositories`,
`Birko.Data.Models`, `Raven.Client.Documents.Session`; kept `Birko.Data.Tenant.Models` — `ITenant` is used).
**L219** (cleanup): removed the dead `int InternalRecordId` field ("for database compatibility") — never
set/read; RavenDB ignores the unmapped `internalRecordId` on existing docs, so no guard is needed (unlike
Batch AH's Mongo removal). **Tests:** Sync.RavenDB.Tests 8 → 9 (dropped `GenerateDocumentId_UsesEntityAndScope`
for the removed helper; added `Model_HasNoDeadMembers` — reflection-asserts all three model members gone —
and `SyncStore_MethodsDropTheMisleadingAsyncSuffix` — the sync store exposes the plain names, no `*Async`).
The `ConvertToRavenItem`/`DeterministicGuid` upsert tests stay green. Suite green: Sync.RavenDB.Tests 9.

**Batch AH — Data.Sync.MongoDb cluster (CR-L215, CR-L216):** Birko.Data.Sync.MongoDb. Both closed;
**/code-review clean (no findings)**. **L215** (cleanup): removed the decorative
`[BsonIgnore] CollectionName => "SyncKnowledge"` property from `MongoSyncKnowledgeItem` and fixed the
matching CLAUDE.md claim. It never affected where documents live — the base store resolves the collection
via `MongoDBClient.GetCollection<T>()` → `collectionName ?? typeof(T).Name`, so documents live in a
`MongoSyncKnowledgeItem` collection. Wiring it through was deliberately declined: that would silently
relocate existing data to `SyncKnowledge`. (Contrast `CosmosSyncKnowledgeItem.ContainerName`, honored because
it is passed to the base ctor.) **L216** (cleanup): removed the dead `int IdRecord` field (`BsonElement
"recordId"`, "for compatibility") — never assigned by `CreateKnowledgeItem`, never queried, inflating every
document with `recordId:0`. **Backward-compat guard:** because `IdRecord` WAS a persisted element and the
MongoDB driver throws on unmapped elements by default (no IgnoreExtraElements convention is registered in the
Birko Mongo layer — grep-confirmed), added `[BsonIgnoreExtraElements]` to the model so documents already
written with `recordId` still deserialize; without it, dropping a persisted field would break reads of
existing data. `CollectionName` was `[BsonIgnore]` (never persisted), so its removal has zero data impact.
**Tests:** Sync.MongoDb.Tests 3 → 5 (`Model_HasNoDeadFields` reflection-asserts both members are gone;
`Deserialize_LegacyDocumentWithRecordId_DoesNotThrow` — an offline `BsonSerializer.Deserialize` of a
hand-built doc carrying a legacy `recordId` element succeeds, proving the guard; built by hand to sidestep
the driver's global GuidRepresentation config). Suite green: Sync.MongoDb.Tests 5.

**Batch AG — Data.Sync.Json cluster (CR-L213, CR-L214):** Birko.Data.Sync.Json. Both closed;
**/code-review clean (no findings)**. **L213** (cleanup): removed the vestigial `int Id` field (serialized
`"id"`) from `JsonSyncKnowledgeItem` — dead state that defaulted to 0 in every record (the store keys
exclusively by `AbstractModel.Guid`, and neither `ISyncKnowledgeItem` nor the sync provider ever read/wrote
it; identity is covered by Guid/EntityGuid). Existing files carrying `"id":0` are harmless — System.Text.Json
skips unmapped members. This is the JSON analogue of Batch AF's ES `RecordId` and the Mongo `IdRecord`.
**L214** (other, docs — finding rated "acceptable as-is"): documented the derived-timestamp contract on
`GetLastSyncTimeAsync`/`SetLastSyncTimeAsync` — last-sync-time is the max `LastSyncedAt` over a scope's items,
so stamping an **empty** scope persists nothing (Set echoes the value back, a later Get still returns null →
reads as initial-sync). Kept the derived design: `AsyncSyncProvider` always persists the round's knowledge
items (Create/Update) before stamping (`AsyncSyncProvider.cs:204-208`), so a stamp only ever lands on a
populated scope; a sentinel/scope record would change the persisted shape for a case that never occurs.
**Tests:** Sync.Json.Tests 5 → 7 (`SetLastSyncTime_EmptyScope_IsANoOp_AndGetStaysNull` pins the L214
contract; `Model_HasNoVestigialIdField` reflection-asserts the L213 removal). Suite green: Sync.Json.Tests 7.

**Batch AF — Data.Sync.ElasticSearch (CR-L212):** Birko.Data.Sync.ElasticSearch. Closed;
**/code-review clean (no findings)**. **L212** (cleanup, verify-first — finding partly stale): the model
declared two supposedly-dead fields, `RecordId` (int, mapped `recordId`) and `Id` (string). Verification
split them: **`RecordId` dropped** — genuinely dead (never assigned by `CreateKnowledgeItem`, never read,
always persisted as 0, not in `ISyncKnowledgeItem`; the `[Number]`/`NumberType` was its only use, but
`using Nest;` stays for the other attributes). **`Id` kept** — the finding's claim that it is "not consumed"
and "the Mongo sibling has neither field" is stale on both counts: `Id` IS populated by `CreateKnowledgeItem`
via `GenerateId` (deterministic `{EntityGuid}_{Scope}` → `docKey`) and was deliberately reworked under
CR-H101 off the reserved `_id`; and the MongoDB sibling DOES carry an `Id` (`[BsonId]`) plus an equally-dead
`IdRecord`. Doc-comment now states why `Id` is retained. **Tests:** Sync.ElasticSearch.Tests 10 → 11
(`RecordId_Removed_AsDeadField` — reflection asserts the property and its `recordId` mapping are both gone,
catching a future reintroduction; the existing CR-H101 `docKey`/reserved-`_id` mapping tests stay green).
Suite green: Sync.ElasticSearch.Tests 11.

**Batch AE — Data.Sync.CosmosDB cluster (CR-L210, CR-L211):** Birko.Data.Sync.CosmosDB. Both closed;
**/code-review clean (no findings)**. **L210** (cleanup, efficiency): `GetKnowledgeItem` / `GetKnowledgeItemAsync`
now query directly on `Scope + TenantId + EntityGuid` with a `.Take(1)` (async iterates the feed and
returns the first non-null; sync reuses the `allowSynchronousQueryExecution` + `.ToList().FirstOrDefault()`
pattern), instead of materializing every document in the scope into a Dictionary just to pull one item out
via `TryGetValue` — a single-item read no longer pulls the whole partition set across the wire. Incidental
robustness gain: the old whole-scope `ToDictionary(x => x.EntityGuid)` threw on a duplicate EntityGuid;
`.Take(1)` returns the first. **L211** (cleanup): the 14-line `ConvertToCosmosItem` mapping — duplicated
verbatim across both stores — is consolidated into a single shared factory
`CosmosSyncKnowledgeItem.FromInterface(item, tenantId)` on the model; both stores call it (the tenant-stamp
CR-H100 and null-Guid-populate CR-M158 behavior is preserved). `System.Guid.NewGuid()` is fully qualified in
the factory because the inherited `AbstractModel.Guid` instance property shadows the type name in a static
method's expression context. **Tests:** Sync.CosmosDB.Tests 6 → 7 (the sync/async `ConvertToCosmosItem`
duplicate tests fold into one `FromInterface` set; added a full field-copy assertion). L210's LINQ filter
is a live-Cosmos path (code-review verified). Suite green: Sync.CosmosDB.Tests 7.

**Batch AD — Data.Sync core cluster (CR-L207, CR-L208, CR-L209):** Birko.Data.Sync. All closed;
**/code-review clean**. **L207** (cleanup): `SyncOptions.MaxItems` is now honored — Sync/Preview (sync + async)
cap `allGuids` via `.Take(MaxItems)`; the `SaveFilterBlockAction.MarkConflict` block action (previously
falling through the switch to behave like Skip) now invokes `OnConflict` with a `ConflictInfo`, making a
blocked save distinguishable from a skip; `SkipPreview` documented as reserved (no internal caller —
Preview and Sync are independent public entry points). **L208** (cleanup): dropped the unused
`localExists`/`remoteExists`/`hasKnowledge` bools in `ProcessBatch(Async)` (the `out` vars are still used).
**L209** (bug): entity change-detection dictionaries are built via a new `BuildEntityDictionary` helper that
throws a **clear** `InvalidOperationException` naming the side when an entity has an empty Guid (all unsaved
entities map to `Guid.Empty`) or two entities collide — instead of the opaque `ArgumentException` a plain
`ToDictionary(GetGuid)` throws (surfacing as a generic "Sync failed"). **Tests:** Sync.Tests 41 → 43
(`MaxItems` caps the processed set; null `MaxItems` processes everything). L209's empty-Guid guard is
code-review verified — the InMemory test store auto-assigns Guids on Create, so empty-Guid entities can't be
staged through it. Suite green: Sync.Tests 43.

**Batch AC — Data.Stores cluster (CR-L203 … CR-L206):** Birko.Data.Stores. All closed;
**/code-review clean**. **L203** (cleanup): removed the dead `AggregateMath.BucketByTime<T>` (no callers;
the mutation concern it was meant to address was already fixed differently under CR-H097 — `AggregateHelper`
computes bucket time on-the-fly via `BucketTimeOf`/`TruncateToBucket`, no mutation). **L204** (convention):
appended `.ConfigureAwait(false)` to every `*CoreAsync` / inner await in `AbstractAsyncStore` (5) and
`AbstractAsyncBulkStore` (8), matching the `EnsureInitializedAsync` awaits — avoids a captured-context
deadlock/perf risk in a base library that may run under a sync context. **L205** (convention): async
`ReadAsync(Guid)` builds its filter via the shared `Filters.ModelByGuid<T>` (mirroring the sync
`AbstractStore.Read(Guid)`) instead of an inline `x => x.Guid == guid`, so the two paths can't silently
diverge. **L206** (test-gap): added `SharedHelperTests` covering `AggregateMath` (ComputeSum/Avg/Aggregate
empty/all-null/Count/Min-Max edge cases + `TruncateToBucket`), `TimeIntervalParser.Parse` (human + TimeSpan
forms), and `PropertyUpdate.ApplyTo`; the time-bucket path already had `AggregateHelperTimeBucketTests`.
**Tests:** Birko.Data.Tests 193 → 205; InMemory 40 (downstream async-store consumer, no regression from the
base changes). Suites green: Birko.Data.Tests 205, InMemory 40.

**Batch AB — Data.SQL.Views cluster (CR-L201, CR-L202):** Birko.Data.SQL.Views. Both closed;
**/code-review clean**. **L201** (bug): `SqlViewTranslator.Translate` replaces the nine silent `continue`s
(on a failed table/field/view-property/join lookup) with descriptive `InvalidOperationException`s that name
the unresolved `SourceType.SourceProperty` — a view referencing an unmapped table or a misspelled/unmapped
property now fails loudly at translation time instead of silently producing a structurally-wrong SQL view
that's very hard to diagnose. **L202** (cleanup): removed the unused `using System.Reflection;` from
`SqlViewStore.cs`. **Tests:** SQL.Views.Tests 17 → 18 (a view over an unregistered source type throws
`InvalidOperationException` naming it; well-formed views still translate — the grouped-aggregate/COUNT paths
stay green). Suite green: SQL.Views.Tests 18.

**Batch AA — SQL.View.Migrations + SQL.ViewModel (CR-L198; CR-L199, CR-L200):** All closed;
**/code-review clean**. **L198** (convention, docs): rewrote the `ViewSqlGenerator` docs in
Birko.Data.SQL.View.Migrations README + CLAUDE to state the real API — a single `char quoteChar` applied
**symmetrically** (default `"`), supporting ANSI/PostgreSQL + MySQL only; **SQL Server bracket quoting
`[ ]` and T-SQL `CREATE OR ALTER VIEW` are out of scope** (use Birko.Data.SQL.MSSql.View) — the docs had
claimed a `(open, close)` tuple and broad provider support that the single-char API can't deliver.
**L199** (convention): `AsyncDataBaseRepository` gains `AddOnInit`/`RemoveOnInit` (delegating to the
unwrapping `DataBaseStore`) for parity with the sync `DataBaseRepository`; the `Connector` accessor was
already restored under CR-C17 (verify-first). **L200** (cleanup): deleted the ~20-line commented-out
`ReadView<TView>` block in the sync `DataBaseRepository` (referenced a removed `GetConnector()`/`SelectView`
API). **Tests:** SQL.ViewModel.Tests 10 → 11 (async `AddOnInit`/`RemoveOnInit` fire/unfire via
`Connector.DoInit()` over a real SQLite store). Suite green: SQL.ViewModel.Tests 11.

**Batch Z — Data.SQL.View cluster (CR-L195, CR-L196, CR-L197):** Birko.Data.SQL.View. All closed;
**/code-review clean**. **L195** (bug): aggregate view columns are now aliased by the **unique view-property
name** (`field.Property.Name`) in both `ViewSelectSqlBuilder` (the `AS` alias) and
`GetPersistentViewSelectFields` (the queried column), instead of the aggregate **function name**
(`FunctionField.Name` = "COUNT"/"SUM") — two aggregates of the same function collided on a duplicate column
name in a persistent view's DDL, and the two sides must name the identical column so the persistent query
resolves. **L196** (bug): `BuildViewJoinConditionSql` routes a constant join value through a new
`FormatJoinConditionValue` — numerics emit unquoted via **InvariantCulture** (a comma-decimal locale no
longer corrupts the SQL), bools emit `TRUE`/`FALSE`, and strings keep the single-quoted + doubled-quote
escaping (was: everything quoted as a culture-dependent string). **L197** (nullable): `DataBase.ReadView`
guards a null `LoadView` result with a clear `TableAttributeException` instead of deferring an NRE into the
base `Read` via a null-forgiving `!`. **Tests:** SQL.Tests 294 → 299 (aggregate aliases use property names +
match `GetPersistentViewSelectFields`; `FormatJoinConditionValue` string/int/bool/decimal-under-de-DE-locale
matrix). Updated the MSSql.View schema-binding assertions to the new property-name aliases
(`AS [OrderCount]`/`AS [TotalSpent]`). All SQL.View consumers green: SQL.Tests 299, MSSql.View 19,
SqLite.View 9, Views 17, View.Migrations 11, ViewModel 10.

**Batch Y — SqLite cluster (CR-L192; CR-L193, CR-L194):** Birko.Data.SQL.SqLite + .SqLite.View.
All closed; **/code-review clean**. **L192** (bug): `SqLiteConnector_OnException` detects the missing-table
case via the typed `SqliteException` + `SqliteErrorCode == 1` (SQLITE_ERROR) and a case-insensitive
`"no such table"` match, instead of the brittle locale/version-dependent `"SQLite Error"` prefix substring
(which would break on a Microsoft.Data.Sqlite upgrade or non-English locale). **L193** (other): documented on
`BuildCreateViewSql` that `CreateView`/`CreateViewAsync` is a no-op on SQLite when the view already exists
(`CREATE VIEW IF NOT EXISTS` doesn't update an outdated body, unlike the base `CREATE OR REPLACE VIEW`) — use
`RecreateView` to replace. **L194** (test-gap): new **Birko.Data.SQL.SqLite.View.Tests** project
(git-init'd + registered) — `ViewExists`/`ViewExistsAsync` null/empty/whitespace guards, the
`CREATE VIEW IF NOT EXISTS` DDL string (via `BuildCreateViewSql`), and a **real** round-trip against an
on-disk SQLite db (a seeded view → `ViewExists` true, a missing name → false, a same-named TABLE → false via
the `type='view'` filter; async parity). **Tests:** SqLite.Tests 26 (L192 build-verified, no regression),
SqLite.View.Tests 9 (new). Suites green: SqLite.Tests 26, SqLite.View.Tests 9.

**Batch X — PostgreSQL cluster (CR-L188 … CR-L190; CR-L191):** Birko.Data.SQL.PostgreSQL + .PostgreSQL.View.
All closed; **/code-review clean**. **L188** (cleanup): `IsTransientException`'s
`ex is NpgsqlException npgsqlEx && npgsqlEx is PostgresException pgEx` collapsed to `ex is PostgresException
pgEx` (PostgresException derives from NpgsqlException; the outer binding was unused). **L189** (other):
`PostgreSqlSettings.GetConnectionString` composes via `NpgsqlConnectionStringBuilder` so
Host/UserName/Password/Database values containing `;`/`=`/`'` are quoted/escaped correctly instead of
breaking the key=value parsing or injecting keywords (the builder omits keys at their Npgsql default —
e.g. Port 5432 — documented). **L190** (convention): documented on both `CreateCore`/`CreateCoreAsync` that
bulk create intentionally assigns a fresh Guid to every row (discarding a caller-supplied Guid), matching the
MSSql sibling's bulk convention — changing one provider alone would diverge cross-provider; callers needing a
known id use the single-item path. **L191** (bug): `ViewExists`/`MaterializedViewExists`(+async) add
`AND table_schema = current_schema()` / `AND schemaname = current_schema()` so a same-named object in another
schema on the search path isn't a false positive (the bare single-part CREATE lands in the current schema).
**Tests:** PostgreSQL.Tests 17 → 18 (connection-string round-trip via NpgsqlConnectionStringBuilder incl. a
`;`/`=`/`'`-in-password escaping case; the old literal-Port assertion replaced — the builder omits the
default port). L188 is behavior-preserving (build-verified), L191 HasRows is a live-PG path (code-review
verified). Suites green: PostgreSQL.Tests 18, PostgreSQL.View.Tests 7.

**Batch W — MySQL.View cluster (CR-L186, CR-L187):** Birko.Data.SQL.MySQL.View. Both closed;
**/code-review clean**. **L186** (convention): added a public `ViewExistsAsync(string, CancellationToken)`
override mirroring the sync `ViewExists` — runs the same parameterized `information_schema.VIEWS` query
scoped to `DATABASE()` via `DoCommandAsync` (observing the token), instead of the base fallback's
`SELECT 1 FROM <view> WHERE 1=0` in a try/catch that swallows connection/permission errors as
"view does not exist" and isn't database-scoped; extracted the shared SQL into a `ViewExistsSql` const.
**L187** (test-gap): new **Birko.Data.SQL.MySQL.View.Tests** project (git-init'd + registered in
`.slnx`/`.code-workspace`) — the `ViewExists`/`ViewExistsAsync` null/empty/whitespace `ArgumentException`
guards (both overloads) + a structural assert that the async override is declared on `MySQLConnector` (so
async callers get the information_schema query, not the base probe). The catalog-query HasRows outcome needs
a live MySQL (integration-tier). Suite green: MySQL.View.Tests 7.

**Batch V — MySQL cluster (CR-L183, CR-L184, CR-L185):** Birko.Data.SQL.MySQL. All closed;
**/code-review clean** (dead-code + doc, no behavior change beyond removing dead code). **L183** (cleanup):
the `MySQLConnector_OnException` table-missing guard was `A || (B && A)` — because `&&` binds tighter than
`||`, the second operand could only be true when the first already was, so it was entirely dead; reduced to
`!IsInitializing && ex.Message.Contains("doesn't exist")`. **L184** (convention): the `IsTransientException`
XML doc listed only 1213/1205/2006/2013/1040 but the switch also returns true for 1317/2002/2003 — updated
the doc to match the actual case labels. **L185** (other): documented on the `#region Native Bulk Operations`
that a first-run table-missing failure rolls back and auto-inits (DoInit) but does **not** re-run the bulk
command (the payload is silently dropped) — inherited framework auto-init behavior; callers must ensure the
schema exists (InitAsync / a prior single-row write / CreateTable) before the first bulk op. No new tests
(nothing observable changed — the removed clause was dead). Suite green: MySQL.Tests 14.

**Batch U — MSSql cluster (CR-L179; CR-L180 … CR-L182):** Birko.Data.SQL.MSSql + .MSSql.View. All closed;
**/code-review clean**. **L179** (bug): the six native bulk methods (`BulkInsert`/`Update`/`Delete` sync+async)
opened the connection (and began the transaction) **outside** the `try`, so an `Open`/transient failure
bypassed `InitException`. Moved `Open`/`OpenAsync` (and `BeginTransaction(Async)`) inside the `try`; the
transaction-bearing methods now declare a nullable `SqlTransaction? transaction` outside, roll back via a
null-check in `catch`, and dispose it in a `finally` (replacing the former `using var transaction`). **L180**
(convention): `MSSqlConnector_View.GetSchemaName` (was a private hardcoded `"dbo"`) is now `protected virtual`
so a derived connector can target a non-dbo schema in the SCHEMABINDING two-part names. **L181** (bug): a new
`EnsureIndexedViewSupported` guard makes `CreateIndexedView`/`CreateIndexedViewAsync` throw a clear
`NotSupportedException` for aggregate (GROUP BY) views (SQL Server requires `COUNT_BIG(*)` in a SCHEMABINDING
aggregate view's select list, which the generic builder doesn't emit — the clustered-index step would fail
at runtime with an opaque error). **L182** (convention): rewrote the MSSql.View CLAUDE.md Components section
to document the full indexed-view API (Create/Drop/Exists ×sync+async, key-column ladder,
BuildSchemaBindingSelectSql, GetSchemaName) + the SCHEMABINDING / aggregate-view limitations. **Tests:**
MSSql.Tests 21 (L179 bulk paths are live-SQL-Server-only — build-verified/code-review), MSSql.View.Tests
15 → 19 (aggregate CreateIndexedView[Async] → NotSupportedException; non-aggregate passes the guard;
GetSchemaName override changes the two-part qualification). Suites green: MSSql 21, MSSql.View 19.

**Batch T — Data.SQL.Caching cluster (CR-L177, CR-L178):** Birko.Data.SQL.Caching. Both closed;
**/code-review clean** (comment/doc only, no logic change). **L177** (verify-first, "no action required"):
documented on `CachedAsyncDataBaseBulkStore.ResolveTableName` that construction-time table-name resolution
is intentional and correct — it depends only on T's mapping attributes (not connection/settings state) and
`LoadTable` is static/cached, so resolving it before `SetSettings`/`Init` is cheap and safe; it feeds only
the cache-key prefix. **L178** (cleanup): fixed the misleading `SqlCacheKeyBuilder.ComputeHash` comment
("Use first 12 bytes" → "8 bytes"); the loop already reads 8 bytes = 16 hex chars, matching the
`StringBuilder(16)` capacity, so only the comment was wrong. **Tests:** SQL.Caching.Tests 6 → 7 (assert the
BuildKey filter/order hash segments are exactly 16 hex chars, locking in the corrected comment). Suite green:
SQL.Caching.Tests 7.

**Batch S — Data.SQL core cluster (CR-L173 … CR-L176):** Birko.Data.SQL (+ provider overrides in
.SQL.PostgreSQL / .MSSql / .MySQL). All closed; **/code-review clean**. **L173** (bug, diagnostic):
`DataBase.GetGeneratedQuery` replaces parameter names longest-first (`OrderByDescending(name.Length)`) so a
name that's a prefix of another (`@WHEREName0_5` ⊂ `@WHEREName0_50`, `@LIMIT` ⊂ `@LIMITxxx`) can't corrupt
the rendered SQL — this string is the OnExecute/InitException diagnostic, not executed. **L174** (bug): the
bulk `Insert(tableName, IEnumerable<IDictionary>)` (sync + async) validates that every row shares the first
row's column set (`HashSet.SetEquals`) and throws `ArgumentException` otherwise, instead of silently
mis-binding heterogeneous dictionaries (a missing key left the prior row's stale value); rows are also
materialized once instead of being re-enumerated 4×. **L175** (convention): `SqlUnitOfWork.FromStore` passes
`connector.Settings` (the public property) into the normal ctor, deleting the dummy ctor + the reflection
helper that read the private `_settings` field by name. **L176** (convention/bug): promoted table-missing
detection to a virtual `AbstractConnectorBase.IsMissingTableException(Exception)` (base = SQLite's
"no such table"), routed the three reader catches through it, and added provider overrides — PostgreSQL
(`relation … does not exist` / SQLSTATE 42P01), MSSql (`Invalid object name` / error 208), MySQL
(`doesn't exist` / error 1146) — so a missing table yields an empty read on all backends, not a hard error.
**Tests:** SQL.Tests 289 → 294 (GetGeneratedQuery prefix-collision + string-quoting; base
IsMissingTableException matrix), SqLite.Tests 24 → 26 (heterogeneous bulk-insert throws; `FromStore`
Begin/Commit persists without reflection), MSSql/PostgreSQL/MySQL .Tests +3 each (provider
IsMissingTableException wording matrix). Suites green: SQL 294, SqLite 26, MSSql 21, PostgreSQL 17, MySQL 14.

**Batch R — Data.Repositories cluster (CR-L171, CR-L172):** Birko.Data.Repositories. Both closed;
**/code-review clean**. **L171** (convention): the bulk repository layer now surfaces `ReadFirst(filter)` /
`ReadFirstAsync(filter, ct)` for parity with the store contract (where the inherited bulk `Read(filter)`
returns the collection, not a single entity). Added to `IBulkReadRepository<T>` /
`IAsyncBulkReadRepository<T>` and implemented in `AbstractBulkRepository` / `AbstractAsyncBulkRepository`,
delegating to the store's `ReadFirst`/`ReadFirstAsync` and mirroring each sibling read's null-store behavior
(sync throws `InvalidOperationException` when the store isn't an `IBulkStore`, async returns null). Every
concrete repo inherits the base impl — no direct interface implementers, so nothing breaks. **L172** (other):
`RepositoryLocator.GetRepository<TRepository, TSettings>` constructs the repo parameterlessly and uses the
settings only as a cache key — it has no store/model type to build a configured store. A repo whose only
ctor takes a store previously threw a raw `MissingMethodException`; a new `CreateParameterless` helper now
converts that to a clear `InvalidOperationException` naming the parameterless-ctor requirement, and the XML
doc states the settings-only-for-key contract (use a store-injecting overload to apply settings). **Tests:**
Repositories.Tests 10 → 16 — `BulkRepositoryReadFirstTests` (sync/async ReadFirst over a real InMemory
store: match / no-match) + `RepositoryLocatorSettingsOverloadTests` (store-only repo → clear error;
parameterless repo created + cached by settings id). Suite green: Repositories.Tests 16.

**Batch Q — RavenDB cluster (CR-L164 … CR-L170):** Birko.Data.RavenDB (store + repos), .ViewModel,
.Views. All closed; **/code-review clean**. **L164** (bug): both `RavenDBStore`/`AsyncRavenDBStore` now
implement `IDisposable` and track a `_ownsStore` flag — the connection-string ctor + `Settings.CreateDocumentStore`
paths mark the store owned (disposed on `Dispose`), while an externally-supplied `IDocumentStore` (the
`IDocumentStore` ctor) is left untouched; a repeat `SetSettings` disposes the previously-owned store via a
shared `ReplaceDocumentStore` helper (was leaking the prior store). **L165/L166** (convention): added a sync
`IsHealthy()` to `RavenDBStore` mirroring the async store's real connectivity probe (an empty
`Query<T>().Take(0)`), and pointed the sync repos (`RavenDBRepository`/`RavenDBModelRepository` in the store
project + the `.ViewModel` sync repo) at it instead of `DatabaseExists()` — which returned true for an empty
database name without touching the server, so sync `IsHealthy` disagreed with async. **L167** (other):
documented that the ViewModel `SetSettings(RemoteSettings)` is a delegation no-op only when the store fails
to unwrap (never for the constructor-guaranteed backing store). **L168** (bug): `RavenViewTranslator` emits
`group result by 1 into g` (global aggregate) for an aggregate-only view with no GroupBy/Fields, instead of
the invalid `group result by new {  } into g` an empty composite key produced (RavenDB rejected it at
index-put with an opaque error). **L169** (cleanup): removed the dead `firstJoin`/`rightTypeName` locals in
the join-map build (the per-join loop recomputes the name). **L170** (other): `RavenViewStore.QueryAsync`
replaces the bare `(IRavenQueryable<TView>)sorted` hard-cast with a guarded `is not` pattern that throws a
descriptive `InvalidOperationException`, so a future non-Raven `IQueryable` in the pipeline fails clearly
rather than with an opaque `InvalidCastException`. **Tests:** RavenDB.Tests 30 → 35 (`RavenDBStoreDisposalTests`
— owned-store disposed, external-store untouched, idempotent, sync + async; offline since `DocumentStore.Initialize`
doesn't connect), Views.Tests 4 → 5 (aggregate-only view groups by the constant `1`). L165/L166 health probes
are live-server paths (code-review verified). Suites green: RavenDB.Tests 35, ViewModel.Tests 4, Views.Tests 5.

**Batch P — Data.Processors cluster (CR-L160 … CR-L163):** Birko.Data.Processors. All closed;
**/code-review clean**. **L160** (cleanup): extracted a shared `AbstractDecoratorProcessor<TProcessor, TModel>
: AbstractProcessor<TModel>` that holds `_inner` + the public `Inner` accessor and wires the whole event
pipeline once; `HttpProcessor`/`ZipProcessor` now derive from it, deleting the byte-identical
`WireInnerEvents` copies (registered the new file in `.projitems`). **L161** (cleanup): `ProcessorParseException`
no longer fabricates a synthetic `new Exception(message)` inner exception when none is supplied — it passes
the nullable `innerException` straight through (the base `ProcessorException(string, Exception?)` ctor was
relaxed to accept null), so a parse exception without a cause has a clean null `InnerException` instead of a
misleading duplicate-message "caused by". **L162** (test-gap, verify-first): already covered — the nested
subfolder entry by CR-M127's `ProcessStreamAsync_NestedFolderEntry_Extracts` and the `../` Zip Slip path by
the dedicated `ZipProcessorZipSlipTests` (CR-H076). **L163** (test-gap): added the HTTP download→temp-file→
inner.ProcessStream(Async)→cleanup happy path (sync + async) via a stub `HttpMessageHandler` returning CSV,
asserting items are produced and the temp file is deleted. **Tests:** Processors.Tests 36 → 40
(HttpProcessor happy path ×2, ProcessorParseException null/preserved inner ×2). Suite green: Processors.Tests 40.

**Batch O — Data.Patterns cluster (CR-L158, CR-L159):** Birko.Data.Patterns. Both closed;
**/code-review clean**. **L158** (`RuleSpecification`): removed the dead `memberAsObject` local, and hardened
the compiled-expression (`ToExpression`) path against runtime throws — `BuildStringMethod` now guards a
null string member (`x.Name != null && x.Name.Contains(...)`, so an in-memory compiled delegate no longer
NREs on a null property) and returns an unsatisfiable leaf for a non-string member; `BuildComparison`/
`BuildBetween` route the value through a new `TryConvertConstant` helper that degrades to
`Expression.Constant(false)` when the value is null against a non-nullable value type or is
non-convertible (was `Convert.ChangeType` → `InvalidCastException`/`FormatException`/`ArgumentException`),
and accepts an already-correctly-typed value directly (covers enums, which `Convert.ChangeType` can't
target). **L159** (`AsyncPagedRepositoryWrapper.ReadPagedAsync`): awaits the page-read then the count
**sequentially** instead of starting both on the same `_repository` and `Task.WhenAll`-ing — a
connection-bound backend can't service two in-flight calls on one instance (matches the already-sequential
sync `PagedRepositoryWrapper`). **Tests:** Patterns.Tests 22 → 32 — `RuleSpecificationExpressionTests`
(compiled Contains/NotContains over a null string no-throw, non-convertible + null-vs-non-nullable →
unsatisfiable, convertible values still match, Between with valid bounds, enum value already-typed) +
`AsyncPagedRepositoryWrapperTests` (an instrumented probe repository asserts max observed concurrency == 1,
result assembly, null-ctor guard). Suite green: Patterns.Tests 32.

**Batch N — MongoDB cluster (CR-L153 … CR-L157):** Birko.Data.MongoDB (store), .MongoDB.ViewModel,
.MongoDB.Views. All closed; **/code-review clean (no findings)**. **Nullable:** L153
(`Settings.ReplicaSet` is now `string?` — the `= null!` suppression lied about the type; it is genuinely
optional and only ever read via `IsNullOrEmpty`). **Cleanup:** L154 (dropped the no-op
`(Expression<Func<T, bool>>)filter` self-cast in `MongoDBStore.Update`/`AsyncMongoDBStore.UpdateAsync` — the
parameter already has that type; the driver's implicit `FilterDefinition<T>` conversion is unaffected),
L155/L156 (removed the `DestroyAsync`/`Destroy` overrides on both ViewModel repos — the base already destroys
the store via `BulkStore`/`Store`, so the override's extra `DropAsync`/`Drop` dropped the collection a second
time, and that second call bypassed any wrapper by hitting the unwrapped store; `DropAsync`/`Drop` remain as
explicit collection-drop helpers), L157 (`MongoViewTranslator` materializes the group-by projection once and
reuses it for both the `_id` composite and the `$first`-carried-forward fields, instead of building two
identical `Select` enumerables). **Tests:** MongoDB.Tests 42 → 44 (`ReplicaSet` default-null + omitted from
the connection string, `LoadFrom` round-trips a null), ViewModel.Tests 4 → 6 (structural: the repos no
longer re-declare `Destroy`/`DestroyAsync` while `Drop`/`DropAsync` stay), Views.Tests 6 → 7 (the `$group`
stage carries the key in both `_id` and `$first`). Suites green: MongoDB 44, ViewModel 6, Views 7.

**Batch M — Migrations backend cluster (CR-L141 … CR-L152):** CosmosDB, ElasticSearch, InfluxDB, MongoDB,
RavenDB, SQL migration stores. **Bugs fixed:** L143 (ES `ElasticSearchDataMigrator` range operators
`$gt/$gte/$lt/$lte` validate the value via a new internal `ToRangeBound` — `Convert.ToDouble(null)`
silently returned 0, so `{"x":{"$gt":null}}` became a range > 0; now throws `ArgumentException`), L144 (ES
`CopyData` throws `NotSupportedException` when a `transformJson` is supplied instead of silently dropping it
— the server-side reindex applies no transform), L150 (SQL `SqlDataMigrator` routes identifier quoting
through the connector dialect via a `QuoteIdentifier` helper + a quoter threaded into `ParseFilterToWhere`,
instead of hardcoded ANSI double quotes that break on SQL Server `[brackets]`; `SqlMigrationContext` now
passes the connector), L152 (SQL `SqlSchemaBuilder.CollectionExists` picks `sqlite_master` vs
`INFORMATION_SCHEMA` from the connection provider — the unconditional INFORMATION_SCHEMA query threw on
SQLite). **Convention:** L142 (ES migrations-index create honors the configured `NumberOfShards`/
`NumberOfReplicas` — were hardcoded 1/0; `UseAliases` documented as reserved), L151 (SQL RemoteSettings
ctor copies the whole chain via `SqlMigrationSettings.LoadFrom(remoteSettings)` — the manual field-copy
dropped `UseSecure`). **Cleanup:** L141 (Cosmos) + L149 (Raven) replace the misleadingly-named
`_cachedState` (only ever a null-check sentinel; every read re-fetches) with a `bool _initialized` flag,
L147 (InfluxDB extracts the duplicated `if (_migrationsBucket == null) Initialize();` into one
`EnsureInitialized()`), L148 (MongoDB drops the unused `IMongoClient _client` field + ctor param;
`MongoMigrationRunner` updated). **Docs/verify-first:** L145 (InfluxDB `*Async` observe the token at entry;
genuine SDK-async threading is the deferred CR-M108 work), L146 (InfluxDB broad `catch{}` narrowed to
`InfluxException` so non-Influx exceptions surface — precisely distinguishing no-data from auth needs the
live tier, deferred). **Tests:** SQL 24 → 29 (`ParseFilterToWhere` bracket-quoter, `CollectionExists` on
real SQLite, `SqlMigrationSettings.LoadFrom` copies UseSecure), ES 2 → 8 (`ToRangeBound` null/non-numeric/
numeric matrix + `CopyData` transform-throws). **/code-review: 2 PLAUSIBLE** (L148 Mongo ctor breaking
change — documented/kept; L146 InfluxDB narrowing still swallows auth within InfluxException — deferred).
Suites green: Migrations CosmosDB 11, ElasticSearch 8, InfluxDB 17, MongoDB 7, RavenDB 11, SQL 29.

**Batch L — Data.Localization cluster (CR-L135 … CR-L140):** all in Birko.Data.Localization.
**Bugs fixed:** L135 (the filter-based `Update(filter, PropertyUpdate)` / `UpdateAsync` overrides in both
bulk wrappers now detect — on a non-default culture — a PropertyUpdate that targets a **localizable**
field and fall back to the `Action<T>` read-modify-write path so a translation row is persisted, instead
of the native pass-through that mutated only the base column and wrote no translation; a new shared
internal `LocalizedPropertyUpdateHelper` does the detect + `ToAction` (reusing `PropertyUpdate.ApplyTo`)),
L138 (`ApplyInMemoryOrderBy` sorts through a defensive `SafeObjectComparer` — nulls-first, same-typed
`IComparable` direct, else stable ordinal-string fallback — so a sort on a non-`IComparable` property
degrades gracefully instead of throwing `InvalidOperationException` at sort time). **Provider-friendliness:**
L136 (`BuildGuidFilter` builds the membership test over `List<Guid>.Contains` instead of
`HashSet<Guid>.Contains` — more query providers translate it to SQL `IN`; documented best-effort + no
chunking for very large sets). **Docs/verify-first:** L137 (documented the localized-filter operator/null
semantics on `LocalizedExpressionAnalyzer` — `== null`/`!= null` are not localized (pass through to the
base column) and `!=` only matches entities that HAVE a translation row; full `!=` correctness deferred),
L139 (`EntityTranslationFilter.ByEntityType`/`ByEntityTypeAndCulture` are intended public API — covered by
`EntityTranslationFilterTests` + documented in CLAUDE.md — so kept, per the finding's own criterion).
**Tests:** Localization.Tests 70 → 79 — `LocalizedHelperTests` (ApplyInMemoryOrderBy multi-field ASC/DESC,
ApplyInMemoryPaging offset/limit, the L138 non-comparable-no-throw, BuildGuidFilter empty/non-empty,
CombineFilters param rebinding — the L140 gap) and `LocalizedPropertyUpdateFallbackTests` (the L135
localizable-field fallback writes a translation; non-localizable field / default culture keep the native
path). **/code-review: clean (no findings).**

**Batch K — Data.JSON cluster (CR-L129 … CR-L134):** JSON + JSON.ViewModel.
**Bugs fixed:** L129 (async bulk `UpdateCoreAsync` guards `ContainsKey` before writing + only saves when
something changed — was silently upserting a non-existent item, unlike single-item + sync-bulk Update),
L130 (`AsyncJsonBatchStore.SetSettings(Settings)` uses `is not BatchSettings` → clear `InvalidDataException`
instead of an opaque `InvalidCastException` from the old `(BatchSettings)` hard-cast, matching the sync
batch stores), L131 (`JsonStore.LoadData` + `JsonBatchStore` + `JsonBatchBulkStore` loaders guard
`item?.Guid.HasValue == true` before `_items.Add` — a record with a missing/null guid used to NRE via the
`Guid!.Value`; matches the async/separate loaders). **Convention:** L132 (`AsyncJsonStore.GetPath` guards
both `Location` and `Name`, aligning with the sync `JsonStore.GetPath` — behavior already converged via
`GetDirectory()`). **Cleanup:** L134 (removed the unused `using Birko.Configuration;` from both
JSON.ViewModel repo files — `using Birko.Data.Stores;` kept, needed for the extension methods). **New
project:** L133 (**Birko.Data.JSON.ViewModel.Tests** — sync/async repo ctor guard + JsonStore unwrap, 5
tests; git-init'd + registered in `.slnx`/`.code-workspace`). **Tests:** JSON.Tests +4 (async-bulk no-upsert,
batch SetSettings clear-throw + accept, LoadData null-guid skip via a hand-written camelCase file located
through the public `GetPath()`). **/code-review: clean (no findings).** Suites green: JSON 18,
JSON.ViewModel 5.

**Batch J — EventSourcing + InfluxDB + InfluxDB.ViewModel + InMemory (CR-L120 … CR-L128):**
**Fixes:** L122 (InfluxDB `MapRecordToModel` outer catch now rethrows `InvalidOperationException` with
the target type instead of silently `return default` — the per-property inner catch still skips a single
bad field, but a structural/constructor failure no longer vanishes via the bulk read's `Where(m != null)`
and mask round-trip bugs; sync + async), L123 (added a public `AsyncInfluxDBStore.Settings` accessor;
`InfluxDbUnitOfWork.FromStore` reads Bucket/Organization through it instead of **reflecting** the private
`_settings` field). **Cleanup:** L120 (removed the unused `using Birko.Configuration;` from all 5
EventSourcing `Stores/` files — `System.Linq.Expressions` is actually *used* by the 4 wrappers, so kept;
the finding's claim it was unused in the Extensions file was stale — that file never imported it), L125
(removed the unused `using Birko.Configuration;` from both InfluxDB.ViewModel repo files — the finding also
named `using Birko.Data.Stores;` but that's **required** for the `GetUnwrappedStore`/`IsStoreOfType`
extension methods, so kept). **Docs/verify-first:** L121 (documented InfluxDB `Settings` intentionally
extends `Configuration.Settings` directly, not RemoteSettings — token+org auth, no user/password/port),
L124 (documented the accepted bulk filter-override gap — InfluxDB has no native update-by-predicate;
native filter-Delete is feasible but live-only, deferred; base fallback correct), L126 (the double-destroy
this finding worried about is already fixed in both repos under CR-M096; shared-base extraction deferred —
parallel sync/async hierarchies), L127 (documented InMemory `SetSettings(ISettings)` cast-or-no-op is
intentional — the store never reads settings). **wontfix:** L128 (InMemory `_settings`/`SetSettings`
duplication — the finding itself says leave as-is; no shared base across the sync/async abstract stores,
~20 lines). **Tests:** InfluxDB.Tests +1 (`Settings` accessor + `FromStore`-without-reflection). L122 is a
live-cluster read path (code-review verified — same sanctioned surfacing as ES L114). **/code-review: 1
PLAUSIBLE** (L122 read-now-throws on a structurally-corrupt row — documented, kept). Suites green:
EventSourcing 5, InfluxDB 26, InfluxDB.ViewModel 5, InMemory 40.

**Batch I — Data.ElasticSearch cluster (CR-L111 … CR-L119):** ElasticSearch (store), .ViewModel, .Views.
**Fixes:** L111 (filter-based `Delete`/`Update` overrides in both stores now call `EnsureInitialized`/
`EnsureInitializedAsync` first — lazy-init + cancelled-token gate, matching every base CRUD method),
L112 (`StoreAggregationHelper` composite-bucket `TryGetValue(out string keyValue)` → `out string?` ×2,
clears the CS8600 risk), L113 (extracted a shared `ElasticSearchStoreHelper` — `ResolveIndexName`/
`SanitizeIndexName` + `BuildUpdateScript<T>` — so the sync/async stores stop copy-pasting the index-name
sanitization and the PropertyUpdate→Painless script builder), L114 (bulk per-item failures in an
otherwise-valid response now **throw** with the offending items instead of a swallowed `// TODO` — matches
the single-item paths + UnitOfWork; ES bulk is non-atomic so the successful items are already persisted,
documented inline + flagged by /code-review as PLAUSIBLE, intended), L118 (`ExecuteSimpleQueryAsync`
clamps Size via a new internal `ClampWindowSize` so `From + Size` stays within the ES default
`max_result_window` of 10000 — a non-zero offset with the default size used to exceed it and get rejected),
L119 (`SetPropertyValue` handles enum/Guid targets via a new internal `ConvertValue` — `Convert.ChangeType`
silently dropped every enum/Guid group-by/aggregate column). **API asymmetry:** L115 (async repo gains
`CountAsync(QueryContainer)`/`ClearCacheAsync`/`ReadAsync(SearchRequest)` delegating through the unwrapping
`ElasticSearchStore` property, mirroring the sync repo). **Cleanup:** L117 (removed unused
`using Birko.Configuration;` from both repo files). **Verify-first:** L116 (Birko.Data.ElasticSearch.
ViewModel.Tests already exists — M092 — augmented with async-repo ctor guard + unwrap + CountAsync-zero).
**Tests:** Views.Tests +12 (`ConvertValue` enum/Guid/int/fail matrix + `ClampWindowSize` boundaries),
ViewModel.Tests +3 (async repo). L111/L114 are live-cluster paths (code-review verified). **/code-review:
1 PLAUSIBLE (the sanctioned L114 partial-commit-then-throw semantics — documented, kept).** Suites green:
ES.Tests 78, ViewModel.Tests 6, Views.Tests 19.

**Batch H — Data.CosmosDB (CR-L103 … CR-L110):** **Fixes:** L103 (`IsHealthyAsync` observes the ct),
L106 (removed `AsyncCosmosDBRepository.DestroyAsync` double-destroy override — base is sufficient), L109
(`CosmosViewManager.DropAsync` idempotent on NotFound), L110 (`CosmosViewStore` takes the SQL path for
group-by-only views, not just aggregate views — was returning ungrouped docs via LINQ). **New project:**
L108 (**Birko.Data.CosmosDB.ViewModel.Tests** — repo ctor guard + unwrap, 5 tests). **Docs/verify:** L104
(bulk filter override = accepted base fallback), L105 (CRUD/UnitOfWork need the emulator — deferred), L107
(SetSettings(RemoteSettings) already accepts Cosmos Settings via upcast). **/code-review: clean** (verified
the group-by SQL builder handles zero-aggregate views). Suites green: CosmosDB 43, CosmosDB.Views 7,
CosmosDB.ViewModel 5.

**Batch G — core foundation (CR-L094 … CR-L102):** Configuration, Contracts, CQRS, Data.Aggregates,
Data.Core. **Fixes:** L094 (`PasswordSettings.Password` defaults to `string.Empty` not `null!` — no
consumer distinguishes null from empty), L095 (`Contracts.RetryPolicy.GetDelay` clamps `attemptNumber` to
≥1), L098 (`CQRS/Unit.cs` adds `using System.Threading.Tasks;` for self-containment). **Docs/verify:**
L096 (Contracts CLAUDE.md `IDefault.Default`→`IsDefault`), L097 (Mediator static-cache process-wide intent),
L100 (AggregateMapper.Expand emits insert/delete only), L101 (ICopyable nullability contract — full
alignment deferred, would cascade warnings across ~15 implementers), L102 (LogViewModel duplication
deferred — parallel hierarchies). **Tests:** CQRS pipeline exception-propagation + pre-cancelled-token,
Contracts clamp Theory, Configuration empty-password. **/code-review: clean (no findings).** Suites green:
Configuration 12, Contracts 13, CQRS 30.

**Sub-batch F2 (CR-L084 … CR-L093) — SOAP + SSE + WebSocket:** **L084** (SOAP Send* helpers close the
OutputStream in `finally`), **L085** (StreamReader honors `request.ContentEncoding`), **L086** (extracted a
shared `SoapXml` Escape/BuildFault helper — 3 duplicated copies → 1, byte-identical output), **L087**
(removed dead query-strip in GetServicePath), **L088** (token query-string splits on first `=`), **L089**
(`SseEvent.ToString` emits explicit LF, not AppendLine's CRLF), **L090** (deferred — SendLoop Channel
refactor is untestable-live cleanup; correctness fine), **L091** (removed unused usings in SseServer),
**L092** (verify-first — WebSocketPort.Write no-op catch already gone, CR-M073), **L093** (WebSocketServer
`BroadcastAsync` is best-effort — one failed client no longer aborts the whole broadcast). Suites green:
SOAP 7, SSE 19, WebSocket 33.

**Communication cluster summary (A–F2, CR-L041 … CR-L093, 53 findings):** 2 new test projects created
(Birko.Communication.Tests, Birko.Communication.OAuth.Providers.Tests), `IPort : IDisposable` added,
numerous bug fixes (query-string `=` handling ×3, OutputStream `finally` close ×2, NDEF big-endian, OAuth
poll order + refresh retry, Modbus early-exit, Network thread-join, best-effort broadcast), and a batch of
doc/verify-first closures.

**Sub-batch F1 (CR-L078 … CR-L083) — REST + REST.Server:** **L078** (verify-first — `_clients` already a
ConcurrentDictionary with GetOrAdd), **L079** (documented OnRequest/OnResponse handlers must not throw —
they run inline), **L080** (added static-cache tests; SendRequestAsync HTTP-path seam noted as residual),
**L081** (RestServer route match iterates once + reuses the parameters dict instead of running IsRouteMatch
twice), **L082** (query-string parser splits on the first `=` so token/JWT values containing `=` aren't
dropped), **L083** (response `OutputStream.Close()` moved into a `finally` in both SendResponseAsync and
SendServerErrorAsync so a mid-write client disconnect doesn't leave the connection half-open). Suites green:
REST 22, REST.Server 7.

**Sub-batch E (CR-L070 … CR-L077) — NFC + OAuth + OAuth.Providers:** **L070** (`SerialNfcTransport.TransceiveAsync`
wraps the blocking serial Write/Read in `Task.Run` + observes the token, mirroring ReadTagAsync), **L071**
(documented `NfcReaderPort.Write` blocks + drops the APDU response — use `TransceiveApduAsync`), **L072**
(documented `NfcReaderSettings` intentionally stays on `PortSettings`), **L073** (`NdefRecord.GetText` UTF-16
now defaults to big-endian per the NFC Forum spec, honoring a LE BOM — was UTF-16LE unconditionally),
**L074** (OAuth `GetTokenAsync` no longer re-issues an identical just-failed refresh under the RefreshToken
grant — rethrows + removed the dead switch arm), **L075** (device-code poll issues the first request
immediately, delaying only after authorization_pending/slow_down per RFC 8628), **L076** (GitHub
`CreateDeviceFlowClient` delegates to `CreateDeviceFlowSettings` — single source of truth), **L077** (new
**Birko.Communication.OAuth.Providers.Tests** project, git-init'd + registered). Suites green: NFC 121,
OAuth 54, OAuth.Providers 5.

**Sub-batch D2 (CR-L065 … CR-L069) — Modbus + Network:** **L065** (dropped the always-overwritten dead
`expectedMinResponse` parameter of `SendWriteRequest` + the four `8` call-site args), **L066** (documented
Modbus client as synchronous-by-design, no `CancellationToken` — inherent to the sync IPort contract),
**L067** (response-wait loop exits early on a complete error frame via `IsCompleteErrorResponse()` instead
of spinning the full timeout), **L068** (verify-first — exception-response + TCP tx-id mismatch already
covered by CR-H025/CR-M052; chunked-buffer MockPort residual noted), **L069** (TcpIp/Udp `ReadWorker`
capture local `_stream`/`_client` refs + `Close()` joins the read thread with a 500ms timeout — Udp closes
the client first to unblock its blocking `Receive`). Also added `MockPort.Dispose()` in Modbus.Tests
(ripple from the L043 `IPort : IDisposable` change). Suites green: Modbus 71, Network 25.

Communication cluster remaining: NFC (L070–L073), OAuth + OAuth.Providers (L074–L077), and the web
protocols REST/SOAP/SSE/WebSocket (L078 onward).

**Sub-batch D1 (CR-L059 … CR-L064) — Hardware + IR:** **L059** (verify-first — Serial Read/HasReadData/
RemoveReadData already guard size<0, "negative = all", CR-M049), **L060** (removed a no-op `catch(Exception){throw;}`
in `Serial.Open`), **L061** (rewrote Hardware CLAUDE.md to the real Serial/Infraport/LPT surface; README was
already accurate), **L062** (`InfraredPort.HandleReceivedTiming` tries RawProtocol last regardless of
registration order — RawProtocol matches any non-empty timing; + regression test), **L063** (documented
`SamsungAcProfile.Protocol` as informational — transmit AC via `GetTiming()`), **L064** (documented
`IrTiming.TotalDurationUs` as single-pass, excluding repeats). Suites green: Hardware 23, IR 103.

**Sub-batch C (CR-L053 … CR-L058) — gRPC + gRPC.Server:** **L053** (Credentials doc corrected to
attribute scheme-based inference to `GrpcChannel.ForAddress`, not the pool), **L054/L055** (`DeadlineSeconds`
+ `ExtraMetadata` documented as reserved/not-auto-applied — kept, removing is breaking), **L056**
(verify-first: the client interceptor overrides only unary calls — no streaming overrides exist to test;
the settings-based CreateClient path is integration-tier), **L057** (added the three streaming server-handler
auth-gate tests — Client/Server/Duplex — gRPC.Server.Tests 5→11), **L058** (verify-first: `EnableReflection`
is a host-honored intent signal, already round-tripped by GrpcServerSettingsTests). Suites green: gRPC 25,
gRPC.Server 11.

**Batch 4 — Communication cluster (in progress).** Worked in sub-batches by project.
- **Sub-batch B (CR-L048 … CR-L052) — Camera + GraphQL:** **L048** (`FfmpegCameraSettings.JpegQuality`
  clamped to [1,31] in the setter, so an out-of-range value can't silently fail the ffmpeg capture),
  **L049** (verify-first — settings/frame tests exist; happy path needs ffmpeg; added a JpegQuality clamp
  Theory), **L050** (GraphQL subscription frames — connection_init/pong/subscribe — now use the shared
  `_serializer` so variables are camelCased consistently with the HTTP path, instead of raw default-options
  `System.Text.Json`), **L051** (`SchemaPath`/`SubscriptionProtocol`/`EnableAutoPersistedQueries` documented
  as reserved/not-yet-implemented — kept, since removing them is breaking), **L052** (dropped an unused
  `System.Text.Json` using). Suites green: Camera 21, GraphQL 58.
- **Sub-batch A (CR-L041 … CR-L047) — core + Bluetooth:** **L041** (`PortSettings.GetID` typo
  `AbstratPort`→`AbstractPort`), **L042** (`AbstractPort.InvokeProcessData` public→protected — fired
  internally by derived ports, not part of the IPort contract), **L043** (`IPort : IDisposable` +
  `AbstractPort.Dispose()`→`Close()` with a `_disposed` guard; the 3 ports that already had `Dispose`
  — Serial/NFC/BluetoothLE — became `override`), **L044** (new **Birko.Communication.Tests** project,
  git-initialized + registered in `.slnx`/`.code-workspace`, 6 hardware-free tests via an in-memory
  `AbstractPort` subclass). Bluetooth (platform-gated): **L046** (read worker surfaces faults via a new
  `ReadError` event instead of a bare `catch{break;}`), **L045** (WinRT discovery keys by `args.Id`
  instead of the not-always-present address property — code-review-only), **L047** (Linux P/Invoke uses
  `Marshal.SizeOf<SockaddrL2>()` + `[StructLayout(Sequential)]` — compile-checked with `DefineConstants=LINUX`).
  Suites green: Communication.Tests 6; Bluetooth/Hardware/NFC test projects build clean.

**Batch 3 (2026-07-14) — Caching cluster (CR-L034 … CR-L040, 7 findings):** Birko.Caching + .Hybrid + .Redis.

**Batch 3 (2026-07-14) — Caching cluster (CR-L034 … CR-L040, 7 findings):** Birko.Caching + .Hybrid + .Redis.
**Bugs fixed:** L036 (`MemoryCache.GetAsync` degrades a type-mismatch to a Miss via `is T` instead of the
unchecked `(T)Value!` cast that threw `InvalidCastException`; a stored null is still a hit), L040
(`RedisCache.RemoveByPrefixAsync` batches deletes via the `KeyDeleteAsync(RedisKey[])` array overload
instead of one round-trip per key — ct was already checked under CR-M034). **Convention:** L034 (the six
`MemoryCache` async CRUD methods now observe the `CancellationToken`). **Hardening:** L035 (largely resolved
by CR-M030 — `EvictExpired` no longer sweeps `_locks`; added a `volatile _disposed` flag + guard as belt-and-
suspenders). **Docs:** L038 (documented the intentional L2-hit L1-population staleness cap that differs from
GetOrSet). **Test-gaps:** L037 (added sliding-expiration + CacheSerializer round-trip tests; stampede + L1Max
already covered; NeverRemove-in-EvictExpired left uncovered — not observable without reflection), L039
(`GetL1Options` made `internal` + a full matrix test: null / absolute-below/above-max / sliding-only / null-max).
Suites green: Caching 40, Hybrid 39, Redis 12.

**Batch 2 (2026-07-14) — BackgroundJobs cluster (CR-L014 … CR-L033, 20 findings):** core + 8 backends.

**Batch 2 (2026-07-14) — BackgroundJobs cluster (CR-L014 … CR-L033, 20 findings):** core + 8 backends.
Verify-first. **Bugs fixed:** L014 (`RetryPolicy.GetDelay` overflow → compute in double, saturate at
MaxDelay), L019 (`JobExecutor` typed path returned `JobResult.Failed` when the matched `ExecuteAsync`
yields a null Task instead of masquerading as Succeeded), L025/L029 (JSON + RavenDB `FailAsync` fall back
to `RetryPolicy.MaxRetries` when the job's own MaxRetries is 0, mirroring `InMemoryJobQueue`), L021 (Cosmos
dequeue `ScheduledAt != null` guard), L020 (Cosmos FIFO tiebreaker `ThenBy(EnqueuedAt)`). **Convention/
cleanup:** L017 (`InMemoryJobQueue.EnqueueAsync` stamps `EnqueuedAt` from the injected clock), L022 (Cosmos
`FailAsync` signature matches the interface's non-nullable error), L023 (removed dead ES `IndexName` const),
L027 (removed dead Mongo `CollectionName` prop), L031 (Redis lock-release Lua extracted to one const +
shared sync/async helpers), L015 (documented `JobStatus.Failed` as reserved/unused), L024/L030 (corrected
stale "called automatically" schema doc-comments), L026/L033 (documented the intentional JSON-metadata
serializer + XML `Delay`-resolved-by-pipeline behavior). **Comment-only + deferred:** L016 (fixed the
misleading "re-enqueue" comment; a true no-retry requeue needs a dedicated `IJobQueue.RequeueAsync` — every
backend `EnqueueAsync` is an insert, so re-enqueuing the same id would PK-conflict). **Verify-first (already
resolved / not-a-defect):** L018 (helper is used by backend models — finding scoped to a core-only checkout),
L028 (`Birko.BackgroundJobs.MongoDB.Tests` already exists, CR-M022), L032 (SqlJobLockProvider already
dispatches per dialect, CR-M027). Suites green: core 77, JSON 7, RavenDB 7, Cosmos 3, ES 6, Mongo 6,
Redis 7, XML 7.

**Batch 1 (2026-07-14) — Birko.AI cluster (CR-L001 … CR-L013):**
`Birko.AI` / `.Agents` / `.Contracts` / `.Providers` / `.Resilience`. Verify-first (unverified reviewer
claims). Closed: **L001** (snapshot conversation before the sync fallback so a partially-mutated
streaming turn isn't resubmitted), **L002** (dead `Done ? conversation : conversation` ternary +
`HandleResponse`/`HandleToolUse` return type simplified from the unused `(Done,Continue)?` tuple to
`bool?`), **L003 partial** (removed the redundant all-errors reflection in Agent.cs via an `errorCount`;
the cross-provider `ToolResult` record was deliberately not introduced — the anonymous shape is JSON-
serialized to the wire with exact snake_case keys, so a record risks breaking every provider),
**L004/L009** (double-checked-lock `RegisterAll` in `AgentRegistration`/`ProviderRegistration`),
**L005** (`AgentOptions.FromDictionary` uses `TryParse` so a malformed config value is skipped, not a
`FormatException`), **L006** (`LlmProviderFactory` → `ConcurrentDictionary`), **L007** (new
`LlmProviderFactoryTests` + `FromDictionary` tolerance tests in the existing `Birko.AI.Tests`),
**L008 verify-first** (already resolved by CR-M003 — `LlmStreamingResponse` is `IDisposable`/
`IAsyncDisposable`), **L010** (stale default Claude model `claude-3-5-sonnet-latest` → `claude-sonnet-4-6`),
**L011** (Claude/Gemini tool-arg parsing deserializes the whole input object into `Dictionary<string,object>`
so nested JSON structure survives round-tripping, instead of per-property `ToString()`), **L012**
(`CheckBudgetAsync` short-circuits when `_config.Enabled` is false), **L013** (`ProviderRateLimiter`
last-wins dictionary build, no throw on case-duplicate providers). Suites green: `Birko.AI.Tests` 20,
`.Resilience.Tests` 13, `.Providers.Tests` 17, `.Agents.Tests` 18. Statuses flipped in the audit.

## User story

As a maintainer, I want the **low**-severity code-review findings triaged so genuine nits get
cleaned up opportunistically without derailing higher-priority work.

## Scope

The 418 low findings `CR-L001 …` from
[`CODE-REVIEW-AUDIT-2026-06-17.md`](../../../CODE-REVIEW-AUDIT-2026-06-17.md). **Unverified** —
reviewer claims, not adversarially re-checked; many are stylistic. Confirm value before fixing.

## Tasks

**Not pre-created.** Extract tasks from `CODE-REVIEW-AUDIT-2026-06-17.md` on demand — one task per
`CR-Lxxx` entry (verify-first), copying its ID/Title → title, Path → file:line, Detail → context,
Fix → approach, Acceptance → derive + add a regression test. Flip each finding's `Status` in the
audit (`done` / `wontfix`) as it's triaged.
