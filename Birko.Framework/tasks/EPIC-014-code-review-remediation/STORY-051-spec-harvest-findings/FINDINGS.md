# Spec-harvest findings audit — 2026-07-30

Per-finding detail for **STORY-051**. Produced by the `/specs` harvest of the 25 cross-cutting
areas in `docs/specs/.map.yml`, at code HEAD `f3ac675`.

**57 high · 399 medium · 368 low = 824 findings** across 21 areas.

## How to read this

Every finding here is **specced as-is** in the corresponding `docs/specs/<area>.md` — the specs
record what the code does, defects included. This document is the review queue for changing it.

Findings are **harvester claims, not confirmed defects**, except where a `Verdict:` line appears.
15 were hand-verified against the code: **12 CONFIRMED**, **3 CONFIRMED-NARROWER**
(real mechanism, but the claim overstates or mis-states its scope — read the verdict before fixing),
0 refuted. The unverified remainder should be confirmed before or during its fix.

## Coverage gaps — do not read this as exhaustive

- **1 of 25 areas is missing**: `validation-and-rules`. Its uncapped sweep failed the output
  schema five times running (an over-tight per-item length bound of mine, not a code problem), so
  only an 8-item capped list ever existed for it and that list is NOT included below. A re-run with
  relaxed bounds is outstanding.
- **3 areas were never capped** and are complete, but predate the severity rating, so they carry no
  high/medium/low split: `core-model-contracts` (4), `store-lazy-initialization` (6),
  `unit-of-work-and-transactions` (6).
- The first harvest pass capped findings at 8 per area and 22 of 25 areas hit that ceiling. The
  uncapped re-sweep below averaged 39 per area, so any earlier count of ~8 was an artefact.
- Test coverage was explicitly out of scope for the sweep; a missing test is not reported here.

## High severity

### area: background-jobs

#### SH-H001 — CosmosDB named-queue dequeue never matches null-QueueName jobs, so no dispatcher-enqueued job ever runs

`../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs:65`  ·  _restates a first-pass finding_

The predicate is `queueName == null || j.QueueName == queueName`; every sibling (InMemory/SQL/Mongo/ES/Raven/JSON/XML) also admits `j.QueueName == null`. BackgroundJobProcessor always passes _options.DefaultQueueName ("default", never null), and JobDispatcher.EnqueueAsync/ScheduleAsync/EnqueueWithPriorityAsync leave QueueName null. On Cosmos every such job is invisible forever: DequeueAsync returns null, the documents accumulate in Pending, and PurgeAsync never removes them. Silent total loss of background work.

### area: bulk-filter-operations

#### SH-H002 — SQL Delete(filter)/Update(filter,PropertyUpdate) turn an untranslatable or null filter into an unfiltered whole-table statement

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:156`

**Verdict: CONFIRMED** — Delete(filter) forwards `filter as LambdaExpression` to Connector.Delete with no null or translatability check, and AbstractConnector_Delete.cs:30 builds `"DELETE FROM " + QuoteIdentifier(tableName)` with the WHERE appended only when conditions exist — so zero conditions (null filter, or a filter the parser silently dropped) is a whole-table DELETE.

Connector.Delete(typeof(T), filter as LambdaExpression) -> ParseConditionExpression, whose fall-through returns Array.Empty<Condition>() (DataBase.cs:818) both for a null filter and for every predicate shape it cannot parse (e.g. the InvocationExpression from `x => pred(x)`). AddWhere then appends nothing, so the statement is `DELETE FROM <table>` - every row. Same at 129 (UPDATE with no WHERE) and AsyncDataBaseBulkStore.cs:174/211/213. No-filter, matches-nothing and untranslatable collapse into match-all on the destructive paths.

#### SH-H003 — OrderBy names are concatenated into ORDER BY unescaped, making the bulk read path an injection sink

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:44`

**Verdict: CONFIRMED** — AbstractConnectorBase.cs:558 builds `" ORDER BY " + string.Join(", ", orderFields.Select(kvp => string.Format("{0} {1}", kvp.Key, ...)))` — the key is interpolated verbatim with no QuoteIdentifier and no whitelist. OrderBy<T>.ByName(string) accepts an arbitrary string, so a consumer forwarding a user-supplied sort column (?sort=) has an injection sink.

ReadCore passes orderBy?.ToDictionary() to Connector.Select as string keys; CreateSelectCommand renders them with string.Format("{0} {1}", kvp.Key, ...) (AbstractConnectorBase.cs:558) - no quoting, no whitelist, not a parameter. OrderBy<T>.ByName(string) takes an arbitrary caller string, so `ByName(request.Sort)` puts user input into CommandText ahead of LIMIT/OFFSET. Microsoft.Data.Sqlite and Npgsql both execute multiple statements per command. Same call at AsyncDataBaseBulkStore.cs:61/68.

### area: caching

#### SH-H004 — Cache key derives from Expression.ToString(), so closure-captured filter values collide across tenants

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:51`  ·  _restates a first-pass finding_

filter?.ToString() is taken on the RAW expression (no funcletization/normalization at this point). A captured local renders as `value(<>c__DisplayClass0_0).tenantGuid`, identical for every captured value, so `x => x.TenantGuid == tenant` produces one cache key for ALL tenants. The first tenant's rows are then served to every other tenant from both read overloads. Only inline literals render distinctly.

#### SH-H005 — SQL cache keys carry no database, connection or tenant identity

`../Birko.Data.SQL.Caching/Caching/SqlCacheKeyBuilder.cs:24`  ·  _restates a first-pass finding_

BuildKey keys on table name + filter/order/limit/offset only, and ResolveTableName() (CachedAsyncDataBaseBulkStore.cs:186) derives the table purely from typeof(T)'s mapping, deliberately before SetSettings. Two stores pointed at different databases/schemas but sharing one ICache compute identical keys, so each serves the other's rows.

#### SH-H006 — RedisCache.ClearAsync issues FLUSHDB when no KeyPrefix is configured

`../Birko.Caching.Redis/RedisCache.cs:187`  ·  _restates a first-pass finding_

**Verdict: CONFIRMED** — Read verbatim: no KeyPrefix takes the else branch to server.FlushDatabaseAsync(_settings.Database). A cache clear wipes the whole logical DB including anything else sharing it.

RedisSettings.KeyPrefix defaults to null, so the default ClearAsync path is server.FlushDatabaseAsync(_settings.Database) — it destroys every key in that database, including keys written by other framework components sharing the connection (Birko.MessageQueue.Redis, Birko.BackgroundJobs.Redis, Redis sync stores), not just this cache's entries. ICache.ClearAsync is documented as 'Removes all entries from the cache'.

#### SH-H007 — UpdateAsync(filter, Action<T>) writes back a stale cached snapshot, silently reverting concurrent changes

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:96`

The base UpdateAsync(filter, Action<T>) does `ReadAsync(filter...)` then `UpdateAsync(item)` per row. That read is now served from the cache, so the loop mutates entities as they were up to DefaultExpiration (5 min) ago and UpdateCoreAsync issues a full-row UPDATE of every mapped column — silently overwriting any column another writer changed in the meantime. Lost updates with no error. Aggravated by the fact that the mutation is applied to the cached instance itself (see the shared-reference finding).

### area: data-sync

#### SH-H008 — Bidirectional direction never applies SyncAction.Create — new items are silently dropped

`../Birko.Data.Sync/SyncProvider.cs:276`  ·  _restates a first-pass finding_

`case SyncAction.Create:` tests only `options.Direction == Download` then `== Upload`, with no else and no Bidirectional arm. DetermineSyncAction returns Create for both one-sided-presence Bidirectional branches (SyncProviderBase.cs:114, :137), so under the DEFAULT direction (SyncOptions.Direction = Bidirectional) nothing is written, yet `result.Processed++`, `progress.ProcessedItems++` and a knowledge row are still emitted. Identical at AsyncSyncProvider.cs:277.

#### SH-H009 — A never-uploaded bidirectional item is deleted on the next run

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:97`  ·  _restates a first-pass finding_

Because the Bidirectional Create is a no-op, the knowledge row written for a new local-only item has RemoteVersion=null hence IsRemoteDeleted=true. On run two `localExists && !remoteExists && knowledgeItem.IsRemoteDeleted` routes into the deletion branch: RemoteWins deletes the local row (line 101), NewestWins raises a Conflict that resolves to nothing, LocalWins returns Create which is again a no-op. All three outcomes destroy or lose the item.

#### SH-H010 — Conflict resolution cannot fire for any conflict the provider emits

`../Birko.Data.Sync/SyncProvider.cs:380`  ·  _restates a first-pass finding_

`case ConflictResolution.UseLocal when localItem != null:` then inner `if (remoteItem != null && ...)`; UseRemote symmetrically. Conflicts are only produced by the two one-sided-presence branches (SyncProviderBase.cs:105, :128) where the opposite item is null by construction, so every resolution falls through with no store write and no counter change. Same at AsyncSyncProvider.cs:381.

#### SH-H011 — Knowledge deletion flags are computed pre-write, so every Create marks the destination deleted

`../Birko.Data.Sync/SyncProvider.cs:344`

The knowledge row is built from the pre-action dictionaries: `CreateKnowledgeItem(guid, GetVersionHash(localItem), GetVersionHash(remoteItem), options)`. After a successful Download create, localItem is still null, so every backend sets `IsLocalDeleted = string.IsNullOrEmpty(localItemHash)` = true. The flags mean "absent when decided", not "deleted", yet the Delete branches (SyncProviderBase.cs:78, :120) read them as deletions: a later Upload run whose local read misses the row deletes the remote row that was just created. Same at AsyncSyncProvider.cs:345.

#### SH-H012 — SyncAsync persists knowledge with the run's own token, so a cancelled run loses all knowledge

`../Birko.Data.Sync/AsyncSyncProvider.cs:205`

`CreateAsync(..., options.CancellationToken)`, `UpdateAsync(..., options.CancellationToken)` and `SetLastSyncTimeAsync(..., options.CancellationToken)` run after the batch loop breaks on cancellation, so they throw immediately. The outer catch (line 228) records a generic "Sync failed" and the whole round's knowledge — for items already written to the stores — is lost, leaving the next run to re-decide them blind. SyncProvider.cs:203-207 passes no token and does persist, so sync and async diverge on the same contract.

#### SH-H013 — RavenDB/CosmosDB knowledge stores mishandle a null tenantId in opposite directions

`../Birko.Data.Sync.RavenDB/Stores/AsyncRavenSyncKnowledgeStore.cs:50`  ·  _restates a first-pass finding_

RavenDB applies the tenant predicate only inside `if (tenantId.HasValue)` — GetKnowledgeAsync (50), DeleteKnowledgeAsync (112), SetLastSyncTimeAsync (154), and the same in RavenSyncKnowledgeStore (46, 112, 152). A null tenantId returns, DELETES and rewrites LastSyncedAt across every tenant in the scope. CosmosDB instead uses an unconditional `x.TenantId == tenantId` (AsyncCosmosSyncKnowledgeStore.cs:48), where an equality against null in Cosmos SQL is Undefined, so the same call matches nothing and every run looks like an initial sync.

#### SH-H014 — Many-to-many expansion emits Insert/Delete of the child entity, never junction rows

`../Birko.Data.Aggregates/Mapping/AggregateMapper.cs:213`

ExpandCollection handles ManyToMany with the same code as OneToMany, tagging each operation with `relationship.ChildType` and the child entity; JunctionType, JunctionParentFk and JunctionChildFk are read nowhere in the mapper. Removing one category from a product yields `Delete` of the Category itself — a caller applying the operation deletes the shared child row rather than the association — and adding one yields `Insert` of a Category that already exists. IAggregateMapper.Expand's doc (IAggregateMapper.cs:36) promises junction-table operations that are never produced.

### area: entity-localization

#### SH-H015 — Create/Update on a non-default culture writes the localized text into the default-culture column

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:83`

Update(data) passes the entity to _innerStore.Update as the caller holds it, then SaveTranslations. On CurrentCulture="sk" Name still holds Slovak text, so the base default-culture column is overwritten AND a "sk" row written. The read path closes the loop: Read applies the "sk" translation in place, so a read-modify-write cycle destroys the English value; no wrapper restores the base value first. Also at :76, LocalizedBulkStoreWrapper.cs:133/152/159, AsyncLocalizedStoreWrapper.cs:78/85, AsyncLocalizedBulkStoreWrapper.cs:140/159.

#### SH-H016 — ApplyTranslations mutates the entity in place, corrupting stores that return live instances

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:218`

prop.SetValue(entity, value) overwrites properties of the instance the inner store returned. Birko.Data.InMemory's AbstractInMemoryStore.Read(Guid)/ReadCore return the object stored in _items directly (no CopyTo), so one read under CurrentCulture="sk" permanently replaces the store's default-culture values; a later default-culture read returns Slovak, and any Update(entity) persists it. Same for any caching decorator handing back a cached reference. No defensive copy on any read path (LocalizedBulkStoreWrapper.cs:334, AsyncLocalizedStoreWrapper.cs:212, AsyncLocalizedBulkStoreWrapper.cs:344).

#### SH-H017 — Filter-based Update on a non-default culture overwrites the default-culture base column

`../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs:174`  ·  _restates a first-pass finding_

Update(filter, Action<T>) lets the action mutate the entity, then the inner store persists the mutated entity (base column) while SaveTranslations also writes a "sk" row. Update(f, e => e.Name = "Stolicka") under CurrentCulture="sk" destroys the English base value. Same shape at AsyncLocalizedBulkStoreWrapper.cs:179-187, and reachable from the PropertyUpdate overload via LocalizedPropertyUpdateHelper.ToAction (LocalizedBulkStoreWrapper.cs:190).

#### SH-H018 — Filter-based Update/Delete/PropertyUpdate never rewrite the filter, so a localized predicate hits the base column

`../Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs:181`  ·  _restates a first-pass finding_

UpdateAsync(filter, action) (181), the native PropertyUpdate path (201), DeleteAsync(filter) (216), sync Update(filter, action) (LocalizedBulkStoreWrapper.cs:174), sync native PropertyUpdate (193) and sync Delete(filter) (208) pass the caller's filter straight through, while every read path calls RewriteFilter. Under CurrentCulture="sk", Delete(x => x.Name == "Stolicka") matches the untranslated column and deletes a different set than the equivalent Read(filter) returns - a destructive op disagreeing with its own read.

### area: entity-tagging

#### SH-H019 — No base-class tenant assertion on any read, update or delete path

`../Birko.Data.Tagging/Services/TagService.cs:17`  ·  _restates a first-pass finding_

All 12 data-access hooks (16-28) carry no tenant parameter and TagServiceBase never compares a loaded record's TenantGuid to GetCurrentTenantId(), which it has at line 30. UpdateTagAsync:70 and DeleteTagAsync:82 reach their target through the same unguarded GetTagByIdAsync, so a hook that omits its tenant filter exposes writes and the DeleteAllEntityTagsForTagAsync cascade, not just reads. A post-load `tag.TenantGuid == GetCurrentTenantId()` assert would turn a silent leak into a hard failure; the comment at 11-14 states the contract but nothing enforces it in any of N implementations.

### area: event-bus-and-messaging

#### SH-H020 — Redis non-group subscription reads from "$" forever and never receives any message

`../Birko.MessageQueue.Redis/RedisConsumer.cs:224`

`var lastId = state.LastReadId ?? (state.Options.FromBeginning ? "0-0" : "$")`. With no consumer group (RedisStreamSettings.ConsumerGroup null by default) and FromBeginning false (ConsumerOptions default), StreamReadAsync issues a NON-blocking XREAD from `$`, which by definition returns nothing. state.LastReadId is only ever assigned from a processed entry (line 354), so the position stays `$` forever. A DistributedEventBus over a default RedisStreamQueue delivers zero events, with no error, no log, and IsActive reporting true.

### area: filter-expression-translation

#### SH-H021 — An untranslatable AND/OR operand is read as constant TRUE, so an OR predicate loses its WHERE clause entirely

`../Birko.Data.SQL/SQL/DataBase.cs:916`

IsConstantBoolCondition treats `Values == null` (comment: "returned Array.Empty (constant true)") as constant true, but that is also the state left by an operand the parser cannot handle at all — a TypeBinaryExpression (`x.Payload is string`), an InvocationExpression, or a nested bool member (`x.Sub.Flag`, which sets Type=IsNull and no Name). Fed from lines 430/436, `x => (x.Payload is string) || x.Status == 1` then hits `isOR && leftVal` (448) and returns Array.Empty, so AddWhere emits no WHERE: Delete(filter) deletes the whole table. With `&&` the clause is silently dropped instead.

#### SH-H022 — ReturnSingleSubCondition overwrites the parent's IsNot, so `!(a && trueConst)` renders as `a` — the opposite rows

`../Birko.Data.SQL/SQL/DataBase.cs:948`

The Not branch (360) toggles IsNot on the condition it passes down as `parent`. When the AND/OR branch then collapses a constant operand it calls ReturnSingleSubCondition(parent, surviving, …), which assigns `parent.IsNot = surviving.IsNot` — false for a plain comparison — wiping the negation. `x => !(x.A == 1 && flag)` with a closure `flag == true` (TryGetLiteralBool evaluates closure bools) renders `A = @p` instead of `NOT (A = @p)`. Reads return the complement, and Delete(filter)/Update(filter,…) act on the complement of the intended rows.

#### SH-H023 — RuleConditionConverter puts rule.Field verbatim into the WHERE text — no column resolution, no quoting

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:121`

**Verdict: CONFIRMED** — The Condition is constructed as `new Condition(rule.Field, ...)` — rule.Field, an arbitrary string off the rule, becomes the condition name with no property resolution and no quoting at this layer.

ConvertLeaf does `new Condition(rule.Field, values, …)` and all five strategies interpolate Name raw (`$"{condition.Name}{op}{value}"`, EqualConditionStrategy:26). A rule tree is configuration data, so a Field of `1=1 OR 1=1 --` becomes executable SQL on a path docs/rules.md advertises as "direct WHERE clause". Even benignly it is wrong: no LoadTable/GetFieldByPropertyName lookup, so a `[NamedField]`-remapped property references a non-existent column, and no table qualifier makes it ambiguous in a join. DataBase.ResolveColumnName exists and is not used here.

#### SH-H024 — An unrecognised parameter-bound method call in an UPDATE SET value is reflectively invoked with null args and its result bound as a constant

`../Birko.Data.SQL/SQL/DataBase.cs:239`

ParseExpression's method-call `else` branch does `EvaluateExpression(callExpression)` and binds the result as `@Const{n}`. The normalizer already folded every parameter-FREE call, so only parameter-bound calls arrive here. EvaluateExpression (1232-1239) has no ContainsParameter guard: it evaluates each argument (a parameter yields null) and calls `Method.Invoke`. For a static method with reference parameters — `string.Concat(x.First, " ", x.Last)` — the invoke succeeds and returns " ", which is written to the column. No exception, no warning: the UPDATE silently persists a wrong value.

#### SH-H025 — A value that will not convert to double yields an unbounded range query (ElasticSearch)

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:246`  ·  _restates a first-pass finding_

ParseComparison routes >, >=, <, <= through TryConvertToDouble, which returns null on InvalidCastException/FormatException/OverflowException. Both field and value are non-null at that point, so a NumericRangeQuery is still emitted with a null bound — an unconstrained range matching every document that has the field. `x.CreatedAt > cutoff` (DateTime) and any string/Guid comparison hit this. The same translator backs ParseRequiredFilterQuery, so DeleteByQuery(x => x.CreatedAt < cutoff) deletes every document carrying the field.

#### SH-H026 — An unhandled expression node silently removes the whole WHERE clause (SQL)

`../Birko.Data.SQL/SQL/DataBase.cs:818`  ·  _restates a first-pass finding_

ParseConditionExpression ends with `return Array.Empty<Condition>()`. A top-level node matching no branch (TypeBinaryExpression, InvocationExpression, NewExpression) yields zero conditions, and AddWhere (AbstractConnectorBase:382) appends nothing for an empty clause — indistinguishable from `_ => true`. On a read that returns every row; on the filter-based Delete/Update it targets the whole table. ElasticSearch throws NotSupportedException for the same predicate.

#### SH-H027 — CombineBool silently drops an untranslatable AND/OR operand (ElasticSearch)

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:209`  ·  _restates a first-pass finding_

CombineBool adds only non-null operand translations and returns null only when BOTH are null. If one operand translates to null (ParseUnary falling through, ParseExpression's `_ => null`, ParseComparison's unresolved field/value), the BoolQuery keeps a single clause and the other predicate vanishes. The top-level result is non-null so the ParseFilterQuery guard never fires — the same failure mode the empty-Contains fix (603) removed, one level up. Reachable from ParseRequiredFilterQuery on the two by-query destructive paths.

#### SH-H028 — ElasticSearch String.Contains passes the raw value into a QueryStringQuery — Lucene query-syntax injection

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:555`

ParseContains builds `new QueryStringQuery { DefaultField = field, Query = (string)cVal.Value }` with no escaping of the query_string metacharacters (`: + - && || ! ( ) [ ] ^ " ~ * ? \ /`). A search term reaching `x.Name.Contains(userInput)` is parsed as a query expression, so `secretField:*` or `* OR tenantGuid:…` lets a caller query fields the predicate never mentioned, and unbalanced syntax turns into a parse failure. Nest exposes MatchPhraseQuery/WildcardQuery, which need no escaping.

### area: migrations

#### SH-H029 — ElasticSearch GetAppliedVersions returns an empty set on an INVALID search response

`../Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs:88`  ·  _restates a first-pass finding_

`if (!searchResponse.IsValid) return new HashSet<long>();` cannot distinguish "nothing applied" from "the search failed" (auth, cluster red, timeout). GetCurrentVersion() then reports 0 and Migrate() re-executes every registered migration against an already-migrated cluster. RecordMigration (line 126) throws on an invalid response, so the read path contradicts its own write path.

#### SH-H030 — InfluxDB GetAppliedVersions swallows every InfluxException, re-running everything

`../Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:113`  ·  _restates a first-pass finding_

The catch is justified as "bucket may not have data yet", but an invalid token, wrong organization or connectivity failure surface as the same exception type. Result: empty applied set, CurrentVersion == 0, and Migrate() replays every migration on a live database.

#### SH-H031 — MongoDB version rows are written without the session, surviving AbortTransaction

`../Birko.Data.Migrations.MongoDB/MongoMigrationRunner.cs:78`  ·  _restates a first-pass finding_

The runner threads the session into the context so migration bodies join the transaction, then calls store.RecordMigration(migration) — a sessionless ReplaceOne (MongoMigrationStore.cs:113). Sessionless driver calls commit immediately, so when a later migration fails and AbortTransaction() rolls the data back, the version rows remain: those migrations are permanently considered applied while their changes are gone.

#### SH-H032 — An empty operator object degrades the filter to match-all on delete/update

`../Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs:152`

With filterJson = {"status":{}} the object branch is taken but the inner EnumerateObject loop adds no condition, so ParseFilterToWhere returns "" and DeleteDocuments emits `DELETE FROM {table}` with no WHERE — the whole table is deleted, and UpdateDocuments rewrites every row. Same shape in ElasticSearchDataMigrator:167 (empty Must → match-all DeleteByQuery), RavenDBDataMigrator:145 and CosmosDBDataMigrator:216. Only null/"{}" are intentional match-alls.

#### SH-H033 — InfluxDB migration bucket has a 365-day expiry, so applied versions expire

`../Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:47`

Initialize creates `_migrations` with `BucketRetentionRules(Expire, 365*86400)`. The version points are timestamped with migration.CreatedAt, so after one year InfluxDB deletes them: GetAppliedVersions() returns empty, GetCurrentVersion() yields 0, and the next Migrate() replays every registered migration (including any destructive Up) against a fully-migrated database. Migration bookkeeping must never expire.

### area: repository-contract

#### SH-H034 — ViewModel Update writes a fresh partially-mapped model over the whole row

`../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:218`

Update calls LoadModelInstance (143), which builds a NEW TModel from CreateModelInstance() and applies only MapToModel; the row is never read first. AbstractConnector_Update.cs:34 renders the UPDATE from table.GetSelectFields()/DataBase.Write(table.Fields...) i.e. ALL columns, so any column the ViewModel omits (CreatedAt, TenantGuid, non-presentation fields) is overwritten with the default. For TModel : ITenant the TenantGuid is blanked or TenantStoreWrapper.Update:65 throws. Line 228 then pushes those defaults into the caller's VM. Same at AbstractAsyncViewModelRepository.cs:224.

#### SH-H035 — Hash-based "skip the write" is inert — no store reads StoreDataDelegate's return value

`../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:226`

StoreDataDelegate<T> is `delegate T ...(T data)` (Stores/IStore.cs:13) but every backend calls storeDelegate?.Invoke(data) and DISCARDS the result, writing data regardless: DataBaseStore.UpdateCore:133, InMemory:92, JSON:87, ES:170, Mongo:151. So returning null! for an unchanged model suppresses nothing on any backend, and a ProcessDataDelegate returning a REPLACEMENT instance is dropped on single-item Create (196) and Update while the sync bulk path honours it (AbstractBulkViewModelRepository.cs:71) — the store persists the pre-transform model.

#### SH-H036 — ReadOne extension queries the connector directly, bypassing the tenant wrapper — cross-tenant read

`../Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs:20`

ReadOne calls repository.Connector.Select<TModel,object>(typeof(TModel), filter?.Filter(), ...). Connector is resolved by GetUnwrappedStore (DataBaseRepository.cs:20), which walks IStoreWrapper down to the INNERMOST store, so every decorator is skipped. TenantStoreWrapper.Read (TenantStoreWrapper.cs:47) is what injects ModelByTenant; going straight to the connector applies no tenant predicate, so ReadOne returns the first matching row from ANY tenant. The same bypass drops soft-delete, localization and audit wrappers.

### area: schema-index-and-ddl

#### SH-H037 — long/double/float/short/byte[] properties are silently dropped — no column, values never persisted

`../Birko.Data.SQL/SQL/Fields/AbstractField.cs:235`

**Verdict: CONFIRMED** — The type dispatch maps only bool, DateTime, decimal, Guid, int and string (plus nullable variants) and enum→int; everything else hits `return null` ("Unsupported type — skip, filtered by LoadField"). So long, double, float, short and byte[] properties get no column and never persist, silently. decimal IS mapped, so money is unaffected — but any long identifier or double measurement vanishes.

CreateAbstractField handles only bool, DateTime, decimal, Guid, int, char, string and enums; every other CLR type falls to `return null` with no diagnostic, and LoadField (DataBase_Field.cs:110) turns null into Array.Empty. A [Table] model with `public long Ticks`, `public double Ratio` or `public byte[] Blob` gets a CREATE TABLE without those columns, Write() never emits them and Read() never restores them — silent write-side data loss with no exception or log. The portable FieldType enum names Long/Double/Binary, so callers expect support.

#### SH-H038 — ES reindex reports Success with Failures=0 when the response carries per-document failures

`../Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:98`

`response.Failures?.Count` is read only inside the `if (!response.IsValid)` branch. With waitForCompletion=true ES returns HTTP 200 (IsValid true) for a reindex that skipped documents on version conflicts or mapping errors, listing them in `failures`. The code refreshes the target and returns ReindexResult.Successful with Failures=0. ReindexWithAlias branches on Success, so it swaps the alias to the incomplete index and — with deleteOldIndex:true — deletes the source. Same code in all four reindex variants.

### area: security-and-authorization

#### SH-H039 — Pbkdf2 Verify returns TRUE for every password when the stored hash's salt/hash segments are empty

`../Birko.Security/Hashing/Pbkdf2PasswordHasher.cs:73`

**Verdict: CONFIRMED** — Traced by hand. `Convert.FromBase64String("")` returns an empty array without throwing, so the CR-M233 FormatException guard never fires; Pbkdf2 is asked for a zero-length key; FixedTimeEquals(empty, empty) is true. Requires a corrupted/truncated/placeholder column — Hash() never emits this shape. Two extras found while checking: an iterations segment of 0 or negative throws ArgumentOutOfRangeException out of Verify, and storedHash.Length drives the derived length so a 1-byte truncated hash matches an arbitrary password ~1 in 256.

A stored column of "PBKDF2-SHA512:600000::" splits into 4 parts and passes the algorithm/iteration guards. Convert.FromBase64String("") gives byte[0], so Pbkdf2(..., outputLength: storedHash.Length == 0) returns an empty array and FixedTimeEquals(empty, empty) is TRUE. Verified by running both APIs: outputLength 0 yields a 0-length key and FixedTimeEquals over two empty spans returns True. Any user row whose hash column was truncated, defaulted to '' or written by a half-finished migration authenticates with any password.

#### SH-H040 — AuthenticationService.ValidateToken fails open when authentication is disabled or expands to nothing

`../Birko.Security/Authentication/AuthenticationService.cs:76`  ·  _restates a first-pass finding_

ValidateToken opens with `if (!IsAuthenticationEnabled()) return true;` and IsAuthenticationEnabled is false when Enabled is false OR every configured token/binding expanded to empty at construction. A renamed ${VAR} that leaves Tokens empty silently turns an authenticating endpoint into an open one — every caller, including one presenting a null token, is accepted.

### area: specifications-and-paging

#### SH-H041 — In / NotIn / Like leaf rules translate to Expression.Constant(true) — a store filter matching every row

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:97`  ·  _restates a first-pass finding_

**Verdict: CONFIRMED** — ComparisonOperator defines Like, In, NotIn; BuildLeafExpression has no arm for any of them, so all three fall to `_ => Expression.Constant(true)`. Negated, Not(true) matches nothing.

ComparisonOperator declares Like/In/NotIn; BuildLeafExpression has no arm and falls into `_ => Expression.Constant(true)`. ToExpression() is the store filter for Read/Count AND bulk Update(filter,...)/Delete(filter), so `Status In [1,2]` becomes an unconditional match — Delete(spec.ToExpression()) empties the table. Every other degradation in the file chose Constant(false), and Birko.Rules' own RuleExpressionConverter (Expressions/RuleExpressionConverter.cs:130-132) implements all three correctly.

#### SH-H042 — A disabled root rule translates to Constant(true) — match-all — while in memory it matches nothing

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:62`  ·  _restates a first-pass finding_

BuildExpression returns Expression.Constant(true) when `!rule.IsEnabled`, but RuleEvaluator.Evaluate (RuleEvaluator.cs:13-14) returns NoMatch for the same condition. So RuleSpecification(disabledRule).ToExpression() reads/updates/deletes EVERY row while IsSatisfiedBy returns false for every entity. WrapRuleSet deliberately avoids handing a disabled rule to this path, evidence the true-arm is unintended; RuleExpressionConverter returns null (no filter) instead.

#### SH-H043 — NotContains on a non-string member yields Not(Constant(false)) = match-all, contradicting the code's own comment

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:94`

BuildStringMethod returns Constant(false) for a non-string member (line 141-142, commented "a non-string field is never a match"), but the NotContains arm wraps it in Expression.Not, so `Qty NotContains "1"` on an int property produces `x => !false` — every row matches, with no IsNegated flag required. Same widening reaches Delete(filter)/Update(filter). In memory ComparisonHelper.NotContains does ToString().Contains, giving a genuinely filtered result, so the two halves disagree as well.

#### SH-H044 — IsNegated applied to a degraded Constant(false) leaf flips an unsatisfiable filter into match-all

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:100`

**Verdict: CONFIRMED-NARROWER** — Wrong for the trigger cited. The unresolved-field branch (`if (property is null) return Expression.Constant(false)`) returns BEFORE both the switch and the IsNegated check, so that case is plain Constant(false) — match-none. The mechanism is real only for the Constant(false) that BuildStringMethod returns for a non-string member, which does reach the negation: NotContains on a non-string member, or a negated Contains, becomes match-all. Fix under that description.

BuildComparison/BuildBetween/BuildStringMethod return Constant(false) on a non-convertible value or wrong member type, documented at line 119-121 as making the leaf "unsatisfiable". Lines 100-101 then wrap it in Expression.Not, so `Qty Equal "abc"` with IsNegated=true, or `Qty Contains "1"` with IsNegated=true, produces `x => !false` — the whole filter matches every row, and this feeds bulk Delete/Update. Only the field-not-found case (line 77) escapes, because it returns before the negation.

#### SH-H045 — RuleGroup.IsNegated is silently dropped, so NOT(a AND b) is translated as (a AND b)

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:210`  ·  _restates a first-pass finding_

RuleGroup.IsNegated is documented "Negate the entire group result" (RuleGroup.cs:20-23) and leaf-level IsNegated IS honoured at lines 100-101, but BuildGroupExpression returns `combined` with no Expression.Not. A caller negating a group gets exactly the complement of the intended row set — on Delete(filter) it destroys precisely the rows meant to be kept. RuleEvaluator.EvaluateGroup ignores the flag too, while Birko.Rules' RuleExpressionConverter honours it (line 173-174), so the family disagrees with itself.

### area: store-crud-contract

#### SH-H046 — IStore.Destroy() is documented as "releases all resources" but implementations hard-delete all stored data

`../Birko.Data.Stores/IStore.cs:32`

The XML doc reads "Destroys the store and releases all resources" (IAsyncStore.cs:25 identical), i.e. it reads as disposal. Implementations are destructive: RavenDBStore.Destroy sends DeleteDatabasesOperation(dbName, hardDelete: true) dropping the whole database; CosmosDBStore deletes the container; MongoDBStore drops the collection; JsonStore calls File.Delete(Path); AbstractInMemoryStore clears _items. No store implements IDisposable, so a consumer looking for cleanup finds only Destroy() and a teardown-style call destroys production data.

### area: tenant-isolation

#### SH-H047 — Item-level write authorization trusts the caller-supplied TenantGuid, not the stored row's tenant

`../Birko.Data.Tenant/Stores/TenantStoreWrapper.cs:187`

**Verdict: CONFIRMED** — BelongsToCurrentTenant returns `item.TenantGuid == _tenantContext.CurrentTenantGuid` — it compares the in-memory item's SETTABLE ITenant.TenantGuid and never reads the persisted row. A caller supplying a victim row Guid with its own TenantGuid passes the guard, and the write is delegated with no tenant term added.

BelongsToCurrentTenant compares `item.TenantGuid` (a public settable property, often bound from a request body) against the ambient tenant, never the persisted row. The inner store then keys the write on the primary field only — DataBaseStore.UpdateCore/DeleteCore build conditions from GetPrimaryFields (Guid), AbstractInMemoryStore keys on data.Guid — with no tenant term. A caller in tenant t submitting `{ Guid = <foreign row>, TenantGuid = t }` passes the guard and overwrites or deletes another tenant's row. Same at AsyncTenantStoreWrapper.cs:189 and the bulk paths.

#### SH-H048 — The header/claim guard inspects only X-Tenant-Id; query-string, route and subdomain tenant sources are unguarded

`../Birko.Security.AspNetCore/Tenant/TenantHeaderClaimGuardMiddleware.cs:57`

**Verdict: CONFIRMED-NARROWER** — The guard reads only its hard-coded TenantGuidHeader, and genuinely unguarded alternative sources exist: TenantMiddlewareOptions.TenantQueryStringKey and SubdomainTenantResolver. But "route" is unsubstantiated — no RouteValues tenant source exists. The claim also misses that TenantMiddlewareOptions.TenantHeaderName is itself configurable, so a custom header name escapes the guard's hard-coded constant.

It reads only Headers["X-Tenant-Id"], on the stated grounds that anything else 'resolves to no tenant in HeaderTenantResolver'. But Birko.Data.Tenant's TenantMiddleware also accepts a query-string key (line 99), a route key (line 111) and a CustomTenantResolver, and SubdomainTenantResolver resolves from Request.Host. With TenantQueryStringKey configured, `?tenant={victim}` (optionally beside a garbage header, waved through at line 81) scopes every tenant-scoped read and write to the victim while permissions stay home-tenant. Under Subdomain resolution nothing is correlated.

#### SH-H049 — UseTenantMiddleware binds ITenantContext from the root provider, so a Scoped registration is never observed

`../Birko.Data.Tenant/Middleware/TenantMiddleware.cs:222`  ·  _restates a first-pass finding_

**Verdict: CONFIRMED-NARROWER** — UseTenantMiddleware does resolve ITenantContext from builder.ApplicationServices (the root provider) and pass that one instance as a middleware ctor argument, so a Scoped registration is never observed. But severity is overstated: the shipped TenantContext holds tenant state in AsyncLocal, so the default registration is safe per-request. Only a consumer registering a scoped ITenantContext with per-request state in fields is bitten.

`builder.ApplicationServices.GetService<ITenantContext>()` resolves once from the root provider and is passed as a constructor argument, so the singleton middleware holds that instance for the app's lifetime. With the documented AddTenantContextScoped(), request-scoped stores receive a different TenantContext, so the middleware's SetTenant is invisible and Permissive wrappers read/write across every tenant (in Development ValidateScopes throws at wiring instead). Birko.Security.AspNetCore.TenantMiddleware avoids this via InvokeAsync injection.

#### SH-H050 — TenantSyncProvider scopes knowledge by options.TenantGuid but scopes saves by the ambient tenant only

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:147`

**Verdict: CONFIRMED** — Same root cause as SH-H(delete): the doc comment confirms only save filters are tenant-scoped.

ApplyTenantFiltering wraps CanSaveToLocal/CanSaveToRemote only `if (_tenantContext.HasTenant ...)` and filters on CurrentTenantGuid, while the knowledge/last-sync calls and CreateKnowledgeItem use `GetTenantGuid(options)` first (lines 280, 363, 813). SyncAsync(new TenantSyncOptions { TenantGuid = u }) with no ambient tenant — the documented way to sync one tenant from a background job — keys knowledge to u while no save predicate exists at all, so every tenant's items are written to both stores. With ambient t plus options u, knowledge is keyed u while writes are filtered to t.

#### SH-H051 — TenantSyncProvider deletes with no tenant check at all

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:475`  ·  _restates a first-pass finding_

**Verdict: CONFIRMED** — ApplyTenantFiltering's own XML doc says it "only modifies save filters, not fetch predicates". Fetches are unscoped, so the localItem/remoteItem reaching the SyncAction.Delete arm can belong to another tenant and are deleted with no tenant check.

Tenant filtering lives exclusively in CanSaveToLocal/CanSaveToRemote, but `case SyncAction.Delete:` calls _localStore.DeleteAsync / _remoteStore.DeleteAsync with no predicate consultation. Because the fetch predicates are unscoped, localDict/remoteDict hold every tenant's items, so an item belonging to tenant u that resolves to Delete under ambient tenant t is deleted. Create, Update and ApplyConflictResolutionAsync all consult the predicates, so this reads as an omission.

#### SH-H052 — TenantSyncProvider reads, compares and previews across all tenants; only saves are scoped

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:298`  ·  _restates a first-pass finding_

**Verdict: CONFIRMED** — Same root cause: fetch predicates carry no tenant term, so reads/compares/previews span tenants.

GetAllItemsAsync is called with only LocalFetchPredicate/RemoteFetchPredicate, which ApplyTenantFiltering explicitly leaves untouched ('only modifies save filters, not fetch predicates', line 137). Every tenant's rows enter localDict/remoteDict; AnalyzeItem never consults BelongsToTenant, so PreviewAsync under tenant t enumerates and version-hashes other tenants' entities and reports them as ToCreate/ToUpdate/ToDelete, and those foreign guids also reach the knowledge store.

#### SH-H053 — AddEventTenantScope() binds Tenant.Current, which AddTenantContext* never registers, so events lose their tenant

`../Birko.EventBus.Tenant/Extensions/EventTenantScopeServiceCollectionExtensions.cs:28`

The doc calls Tenant.Current 'the same context AddBirkoSecurity / AddTenantContext* register'. True only of AddBirkoSecurity (SecurityServiceExtensions.cs:128); every AddTenantContext overload registers typeof(TenantContext), a distinct instance. In that wiring TenantEventEnricher sees HasTenant == false, leaves EventContext.TenantGuid null, and TenantEventScopeAccessor then dispatches inside WithAllTenantsAsync — a tenant-scoped event runs with IsAllTenantsScope true and Strict repositories operate across all tenants.

#### SH-H054 — A nested WithTenant does not narrow reads inside an all-tenants scope, so the per-tenant admin loop reads every tenant

`../Birko.Data.Tenant/Models/TenantContext.cs:79`

**Verdict: CONFIRMED** — TenantStoreWrapper.cs:152 (async :150) computes `effectiveTenant = IsAllTenantsScope ? null : CurrentTenantGuid` — all-tenants is tested FIRST — and WithTenant never clears _allTenantsScope. So WithAllTenants(() => foreach t: WithTenant(t, read)) reads every tenant. Note the asymmetry: item writes test HasTenant first (:184), so writes narrow while reads do not.

WithTenant/WithTenantAsync save and restore only _currentTenantGuid/_currentTenantName and never touch _allTenantsScope, while TenantFilter (TenantStoreWrapper.cs:152) prefers the flag: `IsAllTenantsScope ? null : CurrentTenantGuid`. So `WithAllTenants(() => { foreach (var t in tenants) ctx.WithTenant(t, null, () => store.Read(...)); })` — the maintenance pattern the admin scope exists for, and the shape TenantEventScopeAccessor produces for system events — returns every tenant's rows on each iteration.

### area: views-and-aggregation

#### SH-H055 — CosmosFilterTranslator.Translate catches everything and returns "", silently dropping the WHERE clause

`../Birko.Data.CosmosDB.Views/CosmosViewStore.cs:358`  ·  _restates a first-pass finding_

`catch { return string.Empty; }`. Any untranslatable predicate (collection Any, constant-on-the-left, column-vs-column — `Expression.Lambda(expression)` over a parameter-dependent operand throws inside TranslateValue, method calls other than instance Contains) yields no WHERE at all. BuildAggregateSql/BuildCountAggregateSql then aggregate EVERY document, so a filter like `v => v.TenantGuid == x` returns other tenants' rows. Same fail-open CR-H047 closed on the ES side.

### area: workflow-state-machine

#### SH-H056 — FindByState/FindByStatus return other workflows' rows and deserialize them into TData

`../Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs:76`  ·  _restates a first-pass finding_

On SQL/JSON/XML/ES/Mongo/Raven the filter is CurrentState (or Status) alone, while every workflow of every TData shares one table/collection (SQL: __WorkflowInstances). A store typed SqlWorkflowInstanceStore<DB,OrderData> asked for "Submitted" also gets InvoiceApproval rows and calls ToInstance<OrderData>() on them. System.Text.Json ignores unknown members by default, so a foreign DataJson usually does NOT throw — it yields an OrderData with every property defaulted. Same shape in Workflow.JSON:75, .XML:67, .ElasticSearch:71, .MongoDB:63, .RavenDB:69.

#### SH-H057 — SaveAsync overwrites a record's WorkflowName and payload without checking it belongs to this workflow

`../Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs:52`

The update branch does `existing.UpdateFromInstance(instance); existing.WorkflowName = workflowName;` with no comparison of the persisted WorkflowName. Chained with the unscoped Find*: a consumer enumerates FindByStateAsync("Submitted"), gets an InvoiceApproval row restored as a defaulted OrderData, fires and saves — DataJson/HistoryJson/CurrentState of the foreign row are overwritten with defaults and its WorkflowName reassigned. Silent cross-workflow data loss. Same in all seven backends (ES:47, JSON:51, XML:43, Mongo:39, Raven:45, Cosmos:58).

## Medium severity

### area: background-jobs

#### SH-M001 — CosmosDB DequeueAsync claims via an unguarded read-then-write, so two workers get the same job

`../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs:71`  ·  _restates a first-pass finding_

Lines 71-79 read the top candidate, mutate Status/AttemptCount/LastAttemptAt in memory and call `_store.UpdateAsync(model)`. There is no ClaimToken (the model has no such field), no status-guarded conditional update, no MaxClaimAttempts loop and no lock - unlike the four store-backed siblings hardened for CR-H011. Two concurrent workers both return the same JobDescriptor, the job body runs twice, and AttemptCount ends at 1 instead of 2 so the retry budget is wrong too.

#### SH-M002 — CosmosDB CancelAsync applies no status guard and returns true for any existing job

`../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs:119`  ·  _restates a first-pass finding_

Every other backend reads with `j.Guid == jobId && (Status == Pending || Status == Scheduled)` and returns false otherwise; IJobQueue.CancelAsync is documented as 'Cancels a pending or scheduled job'. Cosmos reads by id only, so CancelAsync on a Completed, Processing or Dead job overwrites Status to Cancelled, restamps CompletedAt and reports success. A completed job's terminal record is destroyed, and a running job reports cancelled while it keeps running.

#### SH-M003 — MaxRetries==0 bypasses the injected RetryPolicy on six of nine backends

`../Birko.BackgroundJobs.SQL/SqlJobQueue.cs:152`  ·  _restates a first-pass finding_

InMemory (reference, line 86), JSON (134) and RavenDB (141) compute `maxRetries = model.MaxRetries > 0 ? model.MaxRetries : _retryPolicy.MaxRetries`. SQL:152, MongoDB:137, ElasticSearch:138, XML:132, Cosmos:100 and Redis:204 compare the bare `AttemptCount < MaxRetries`. A descriptor built with MaxRetries left at 0 goes to Dead on its first failure on those six, and the RetryPolicy passed to the constructor is silently never consulted - the same defect CR-L025/L029 fixed on the other three.

#### SH-M004 — JobSerializationHelper.DeserializeMetadata throws ArgumentNullException when the JSON yields null

`../Birko.BackgroundJobs/Serialization/JobSerializationHelper.cs:35`  ·  _restates a first-pass finding_

`result as Dictionary<string,string> ?? new Dictionary<string,string>(result!)` - for a stored metadata string of literal "null" (or any JSON deserializing to null) the `as` yields null and the fallback passes null to the Dictionary copy constructor. Only string.IsNullOrEmpty is guarded, and the `!` suppression is provably wrong. It fires inside ToDescriptor(), so one malformed row makes GetAsync, DequeueAsync and GetByStatusAsync throw on SQL, MongoDB, ElasticSearch, RavenDB and CosmosDB.

#### SH-M005 — A job left in Processing is never recovered, never re-offered and never purged

`../Birko.BackgroundJobs/Core/IJobQueue.cs:53`  ·  _restates a first-pass finding_

If a worker dies between DequeueAsync and Complete/FailAsync (kill, OOM, or on Redis where the Lua script already ZREMed the queue member), the job sits in Status=Processing permanently: no backend's eligibility predicate admits Processing, none implements a lease/visibility timeout, and every PurgeAsync deletes only Completed|Dead|Cancelled with a non-null CompletedAt. The work is silently lost and the row/document/hash leaks forever.

#### SH-M006 — Both lock providers return true for a lock name they do not hold when already locked

`../Birko.BackgroundJobs.SQL/SqlJobLockProvider.cs:44`  ·  _restates a first-pass finding_

SqlJobLockProvider.TryAcquireAsync (44) and RedisJobLockProvider.TryAcquireAsync (80) early-return true whenever IsLocked is set, without comparing lockName to the held name. A caller holding "queue-a" that asks for "queue-b" is told it owns "queue-b" and enters a critical section nothing guards. SqlJobLockProvider.ReleaseAsync (113) compounds it by building the unlock statement from the passed lockName, so it can issue pg_advisory_unlock/RELEASE_LOCK for a key it never acquired while clearing IsLocked.

#### SH-M007 — A single EnqueueAsync failure permanently kills the recurring scheduler loop

`../Birko.BackgroundJobs/Processing/RecurringJobScheduler.cs:74`  ·  _restates a first-pass finding_

Only the Task.Delay is wrapped in try/catch (80-87). A transient queue error (connection blip, timeout, Cosmos 429) thrown by EnqueueAsync propagates out of RunAsync, so every registered recurring job stops firing until the host restarts. NextRunAt was not advanced for the failing definition and nothing records that runs were missed, so the outage is silent.

#### SH-M008 — A job that succeeded is recorded as failed when the processor is stopped mid-completion

`../Birko.BackgroundJobs/Processing/BackgroundJobProcessor.cs:103`

On success ProcessJobAsync calls `_queue.CompleteAsync(descriptor.Id, cancellationToken)` with the processor token. Every store-backed queue funnels through AbstractAsyncStore.EnsureInitializedAsync, which calls ct.ThrowIfCancellationRequested() first, so a Stop() during that call throws OperationCanceledException before anything is written. The catch at 110 then calls FailAsync(id, "Job cancelled due to processor shutdown") - a job whose body completed successfully is rescheduled for a re-run, or marked Dead if it was on its last attempt.

#### SH-M009 — Faulted job tasks are dropped by RemoveAll, so queue-reporting failures are swallowed entirely

`../Birko.BackgroundJobs/Processing/BackgroundJobProcessor.cs:41`

`tasks.RemoveAll(t => t.IsCompleted)` removes faulted tasks too (IsCompleted is true for Faulted). If the catch handlers at 116/120 themselves throw - e.g. FailAsync fails because the DB is down - the job task faults, its handle is dropped on the next iteration, and the exception is never observed or logged: the job stays in Processing forever and the operator sees nothing. There is no logging anywhere in this class.

#### SH-M010 — Stop() lets OperationCanceledException escape RunAsync instead of returning cleanly

`../Birko.BackgroundJobs/Processing/BackgroundJobProcessor.cs:43`

`await _concurrencySemaphore.WaitAsync(_cts.Token)` is not inside any catch, unlike the DequeueAsync (46-54) and Task.Delay (59-66) awaits which both handle OCE and break. When Stop() (or the linked token) fires while the loop is waiting for a permit - the normal state at MaxConcurrency - the OCE propagates through the finally and out of RunAsync, faulting the caller's hosted-service task even though the host's own stoppingToken was never cancelled.

#### SH-M011 — InMemoryJobQueue mutates descriptors outside its dequeue lock, letting a retry be claimed before its backoff

`../Birko.BackgroundJobs/Processing/InMemoryJobQueue.cs:80`

DequeueAsync reads Status and ScheduledAt under `_lock`, but FailAsync/CompleteAsync/CancelAsync mutate the shared JobDescriptor with no lock at all. FailAsync writes Status=Scheduled (91) before ScheduledAt (92), so a concurrent dequeue can observe Scheduled with the *previous* (already elapsed) ScheduledAt and claim the job immediately, skipping the backoff and burning AttemptCount. The fields are non-volatile, so the reordering is also permitted by the memory model.

#### SH-M012 — The claim guard is Status-only (ABA), so a rescheduled job can be re-claimed early with a stale AttemptCount

`../Birko.BackgroundJobs.SQL/SqlJobQueue.cs:112`

The conditional update is `j.Guid == claimId && j.Status == originalStatus` and sets AttemptCount to the *stale* `candidate.AttemptCount + 1`. Worker B can read a Scheduled candidate, stall while worker A claims (Processing), runs and fails it back to Scheduled, then issue its UPDATE - the WHERE matches again, B's ClaimToken verifies, and B executes the job immediately in defiance of ScheduledAt while rewriting AttemptCount downward, granting extra retries. Identical code at Mongo:99, ES:100, Raven:100.

#### SH-M013 — Complete/Fail/Cancel are unguarded full-model read-modify-writes that clobber a concurrent claim

`../Birko.BackgroundJobs.SQL/SqlJobQueue.cs:136`

CompleteAsync/FailAsync/CancelAsync read the whole model, mutate two fields and call `UpdateAsync(model)`, which writes every column including Status, AttemptCount and ClaimToken from the stale snapshot. A double-dispatched worker (see the ES refresh window and the ABA claim above) reporting Complete therefore overwrites the newer claim's Processing/AttemptCount/ClaimToken, so the job currently running is marked Completed and its later FailAsync resurrects it. Same shape in Mongo, ES, Raven, Cosmos, JSON, XML.

#### SH-M014 — Redis dequeue eligibility is score-only with no Status predicate, diverging from every other backend

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:41`

The Lua script picks any queue member with score <= now and never inspects the hash's Status. Enqueue (108-113) scores from `ScheduledAt ?? EnqueuedAt` regardless of status, so: a descriptor enqueued with Status=Cancelled/Completed/Dead is dequeued and executed on Redis (never on the others); a Scheduled job with ScheduledAt=null is immediately eligible on Redis but permanently ineligible everywhere else; and a Pending job with a future ScheduledAt is deferred on Redis but immediate elsewhere.

#### SH-M015 — Redis priority is only a ~0.1 ms tiebreaker, so priority ordering is effectively absent

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:393`

GetQueueScore subtracts at most MaxPriorityBonus = 0.999 score units, and one unit is TimeScale = 1e4 ticks = 0.1 ms. So Priority=999 only outranks a Priority=0 job enqueued less than ~0.1 ms earlier. On every other backend the order is ByDescending(Priority).ThenBy(EnqueuedAt) and priority strictly dominates. The same IJobQueue call therefore drains a mixed-priority queue in FIFO order on Redis and in priority order everywhere else.

#### SH-M016 — Redis enqueue is three non-atomic round trips; a partial failure leaves an unrunnable or phantom job

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:104`

EnqueueAsync writes the job hash (104), then the queue sorted set (110), then the status set (113) as three separate commands with no MULTI/Lua. A connection drop or timeout between them leaves a hash with no queue membership - the job is stored, reported by GetAsync, and never dequeued by anyone - or a status-set entry with no queue entry. Nothing reconciles the three keys afterwards, and no other backend can half-enqueue.

#### SH-M017 — Redis Complete/Fail/Cancel move status-set membership non-atomically, so a job is listed under two statuses

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:171`

CompleteAsync reads oldStatus (171), SREMs that set (172), HSETs the new status (174) and SADDs the new set (180) as four commands; FailAsync (196-222) and CancelAsync (238-259) are the same shape. A concurrent transition (or a failure between commands) removes the id from a set it is no longer in and leaves it in the one it left, so GetByStatusAsync(Processing) returns Completed/Cancelled jobs and the terminal set misses them - and PurgeAsync, which only walks the status sets, never reclaims those hashes.

#### SH-M018 — Redis CancelAsync has a TOCTOU on the status guard and can cancel a Processing job

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:238`

The guard reads Status via HGET (238), checks Pending|Scheduled (240) and only then flips the hash (253). A concurrent DequeueAsync script can claim the job between those two round trips, so the job is flipped to Cancelled while a worker is executing it and `true` is returned, breaking the documented 'only Pending or Scheduled' contract. The subsequent SREM also targets the stale status set, so the id is left in status:2 as well.

#### SH-M019 — SqlJobLockProvider.Dispose never issues the unlock statement, unlike the Redis provider

`../Birko.BackgroundJobs.SQL/SqlJobLockProvider.cs:206`

DisposeAsync (206) and Dispose (216) only clear IsLocked and close/dispose the connection, relying on the ReleaseAsync comment 'Lock released when connection closes'. With ADO.NET pooling, Close() returns the connection to the pool rather than closing the session, so a MySQL GET_LOCK and an MSSql session-owned sp_getapplock can outlive the provider and block every other worker until the pooled connection is evicted. RedisJobLockProvider runs its safe-release script on both dispose paths.

#### SH-M020 — RedisJobLockProvider keeps IsLocked true after the lock key's TTL expires

`../Birko.BackgroundJobs.Redis/RedisJobLockProvider.cs:96`

TryAcquireAsync sets the key with the caller's `timeout` as the expiry (89-94) and IsLocked = acquired, but nothing tracks the expiry and there is no renewal. Once Redis drops the key another provider acquires it while this one still reports IsLocked == true; a subsequent TryAcquireAsync on the same name then early-returns true at line 80 without contacting Redis. Two workers believe they hold the same lock, and ReleaseAsync's token check silently deletes nothing.

#### SH-M021 — JSON/XML dequeue serialization is instance-private while the store rewrites the whole file, so jobs are lost

`../Birko.BackgroundJobs.JSON/JsonJobQueue.cs:27`

`_dequeueLock` is a per-instance SemaphoreSlim, and AbstractAsyncJsonStore keeps all rows in an in-memory `_items` dictionary that SaveDataAsync rewrites as a whole file. Two JsonJobQueue instances in one process (two hosted services, or a queue plus a schema helper) each hold an independent snapshot, so the second save silently discards every job the first added, and both can claim the same job. XmlJobQueue:28 is identical. Only cross-*process* use is documented as unsupported.

### area: bulk-filter-operations

#### SH-M022 — Bulk read passes CLR property names as ORDER BY column names, so a remapped column silently yields no rows

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:44`

**Verdict: CONFIRMED** — AbstractConnectorBase.cs:558 builds `" ORDER BY " + string.Join(", ", orderFields.Select(kvp => string.Format("{0} {1}", kvp.Key, ...)))` — the key is interpolated verbatim with no QuoteIdentifier and no whitelist. OrderBy<T>.ByName(string) accepts an arbitrary string, so a consumer forwarding a user-supplied sort column (?sort=) has an injection sink.

OrderBy<T>.ToDictionary() keys by member.Member.Name (OrderBy.cs:81/90) and ReadCore hands it to the string-keyed Connector.Select overload, which renders keys verbatim. The expression-keyed overload (AbstractConnector_Select.cs:15) maps through DataBase.GetField().GetSelectName() and the aggregate path uses ResolveSqlName; the store path does neither. Ordering by a [NamedField("col")]-remapped property emits ORDER BY on a nonexistent column, which RunReaderCommand swallows (InitException/yield break) - the read returns empty, not an error.

#### SH-M023 — Portable Delete(filter)/UpdateAsync(filter,...) accept a null filter and act on every entity in the store

`../Birko.Data.Stores/AbstractBulkStore.cs:99`

`Read(filter, null, null, null)` takes a nullable filter and treats null as read-everything, but Delete(filter)'s parameter is declared non-nullable and unguarded. Delete(null!) materialises the whole table and passes it to Delete(items) -> DeleteCore. Same at AbstractAsyncBulkStore.cs:146 and at 76/117 for Update(filter, Action)/UpdateAsync. No StoreException, no argument check - the destructive scope is decided by a silently tolerated null.

#### SH-M024 — Task.Run over Connector.Select does no work off-thread: the query runs synchronously on the caller's thread

`../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs:68`

Connector.Select is an iterator method (AbstractConnector_Select.cs:13-127 down to RunReaderCommand), so `Task.Run(() => Connector!.Select(...), ct)` only constructs the enumerator and completes at once. `results.OfType<T>()` is enumerated later - on the awaiting caller's thread, opening the connection and blocking there. So this async read is not asynchronous, ct never reaches the I/O, and query exceptions surface after `await ReadAsync(...)` returns, escaping the caller's try/catch.

#### SH-M025 — Sync bulk ReadCore returns a lazy iterator that holds an open DbConnection outside the store

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:44`

**Verdict: CONFIRMED** — AbstractConnectorBase.cs:558 builds `" ORDER BY " + string.Join(", ", orderFields.Select(kvp => string.Format("{0} {1}", kvp.Key, ...)))` — the key is interpolated verbatim with no QuoteIdentifier and no whitelist. OrderBy<T>.ByName(string) accepts an arbitrary string, so a consumer forwarding a user-supplied sort column (?sort=) has an injection sink.

`Connector?.Select(...)?.OfType<T>()` is deferred: RunReaderCommand (AbstractConnector.cs:227) does `using var db = CreateConnection(...); db.Open();` inside the iterator, so connection and DbDataReader stay open while the caller holds the sequence and close only on enumerator disposal. Read() hands that out unmaterialised, so an abandoned enumerator leaks a pooled connection and a write inside `foreach (var x in store.Read(f))` runs against a live reader. The async twin materialises a List (60-65).

#### SH-M026 — A nested member selector in PropertyUpdate writes to a same-named column on the wrong table

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:124`

GetFieldFromLambda takes `memberExpression.Member as PropertyInfo` with no check that the member is declared on T (DataBase_Field.cs:151-153). `Set(x => x.Owner.Name, "v")` resolves Owner's `Name`, LoadField builds a field called `Name`, and the statement becomes `UPDATE <T's table> SET Name = @SETName WHERE ...` - silently overwriting T's own Name column. ApplyTo (PropertyUpdate.cs:45) throws a reflection TargetException for the same input, so one backend throws and the other corrupts a different column.

#### SH-M027 — Set() on an [IgnoreField]/[NotMapped]/unsupported-type property throws InvalidOperationException from .First()

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:124`

GetFieldFromLambda ends in `LoadField(propInfo).First()` (DataBase_Field.cs:167-168) and LoadField returns Array.Empty when CreateAbstractField yields null - which it does for [IgnoreField] and [NotMapped] (AbstractField.cs:62-65). `Set(x => x.NotMapped, v)` therefore throws 'Sequence contains no elements' from inside the store without naming the property, while the portable path applies it in memory and never persists it - no error at all. Same call at AsyncDataBaseBulkStore.cs:169.

#### SH-M028 — Bulk collection ops are per-item loops with no transaction, so a mid-batch failure leaves a partial batch

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:75`

CreateCore/UpdateCore/DeleteCore (75/93/143, and AsyncDataBaseBulkStore.cs:105/129/193) call the single-entity operation per item; each opens its own connection and DoCommandWithTransaction unless an external SqlTransactionContext was set. An exception or cancelled token on item k commits items 0..k-1 with no rollback and no way for the caller to learn how far it got, while the class XML doc (line 12) advertises 'optimized bulk operations'.

#### SH-M029 — PropertyUpdate.ApplyTo silently drops assignments whose member is not a PropertyInfo

`../Birko.Data.Stores/PropertyUpdate.cs:45`  ·  _restates a first-pass finding_

`memberExpr.Member as PropertyInfo` + `prop?.SetValue(entity, value)` skips a field selector (or any non-property member) with no exception and no return value, so the caller believes the update landed. The SQL native path throws ArgumentException from GetFieldFromLambda for the same input (DataBase_Field.cs:157): one throws, one silently no-ops. Re-confirmed as reported.

#### SH-M030 — Duplicate Set() on one property throws in the SQL path but is last-wins on the portable path

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:126`  ·  _restates a first-pass finding_

`values.Add(field.Name, value ?? DBNull.Value)` throws ArgumentException on the duplicate dictionary key when two assignments resolve to the same column, and `fields` would carry the column twice. ApplyTo applies both in order (last wins). Same code at AsyncDataBaseBulkStore.cs:171. Two properties remapped onto one column via [NamedField] collide the same way. Re-confirmed as reported.

#### SH-M031 — Null Connector turns filter-based Update/Delete/Read/Aggregate into a silent success

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:155`  ·  _restates a first-pass finding_

`if (Connector == null) return;` in Delete(filter) (also 116 in Update(filter,PropertyUpdate); 161/209 in AsyncDataBaseBulkStore) reports success while writing nothing, so a store whose SetSettings was never called appears to delete/update rows. The read side is the same shape: ReadCore returns Enumerable.Empty (44), Aggregate Array.Empty (169), AggregateAsync 228. StoreException exists for this and is thrown nowhere. Re-confirmed as reported.

#### SH-M032 — Empty PropertyUpdate rewrites every matching row on the portable path

`../Birko.Data.Stores/AbstractBulkStore.cs:70`  ·  _restates a first-pass finding_

`Update(filter, entity => updates.ApplyTo(entity))` runs before any emptiness check, so a zero-assignment PropertyUpdate reads all matching entities and calls the single-entity Update on each. Under the timestamp/audit/versioned wrappers that bumps UpdatedAt, audit fields and concurrency versions for rows nobody changed. The SQL override returns immediately (DataBaseBulkStore.cs:116). Same in AbstractAsyncBulkStore.cs:108. Re-confirmed as reported.

#### SH-M033 — Portable PropertyUpdate path clobbers columns it was never asked to change

`../Birko.Data.Stores/AbstractBulkStore.cs:68`  ·  _restates a first-pass finding_

The fallback persists the entire entity as read (Update(item) at line 80), not just the named columns, so a concurrent modification to an unnamed column between the read (76) and the write is silently overwritten. PropertyUpdate exists to express a narrow write, which the SQL override honours; a backend without a native override quietly widens it to a full-row write. Same in AbstractAsyncBulkStore.cs:108/121. Re-confirmed as reported.

#### SH-M034 — AggregateAsync returns empty on a sync-only connector, unlike every other async path

`../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs:228`  ·  _restates a first-pass finding_

`if (AsyncConnector == null || Connector == null) return Array.Empty<AggregateResult>();` - AsyncConnector is `Connector as AbstractAsyncConnector` (AsyncDataBaseStore.cs:37), so a DB that is a plain AbstractConnector always takes this branch. ReadCoreAsync (68), UpdateAsync (178) and DeleteAsync (213) fall back to Task.Run over the sync connector; this does not, so the caller gets zero rows, indistinguishable from an empty GROUP BY. Sync Aggregate (DataBaseBulkStore.cs:169) needs only Connector. Re-confirmed.

### area: caching

#### SH-M035 — Redis-backed store throws ArgumentNullException when caching a not-found single read

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:62`  ·  _restates a first-pass finding_

ReadCoreAsync unconditionally calls _cache.SetAsync(key, result) even when result is null (negative caching). MemoryCache stores that as a legitimate hit, but RedisCache.SetAsync -> CacheSerializer.Serialize -> SystemJsonSerializer.SerializeToBytes begins with ArgumentNullException.ThrowIfNull. On a RedisCache or Hybrid(L2=Redis), every single-result read whose filter matches no row throws out of the store instead of returning null.

#### SH-M036 — Single-result and limit-1 collection reads collide on one cache key with incompatible payloads

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:52`  ·  _restates a first-pass finding_

The single overload builds BuildKey(table, filter, null, 1, null) and stores a T; the collection overload with orderBy==null, limit==1, offset==null builds the identical string and stores a List<T>. On MemoryCache the mismatch degrades to Miss (churn); on RedisCache GetAsync<T> over a stored JSON array (or GetAsync<List<T>> over a stored object) throws JsonException out of the read path.

#### SH-M037 — HybridCache's untyped L2 exception filter swallows cancellation as a cache miss

`../Birko.Caching.Hybrid/HybridCache.cs:59`  ·  _restates a first-pass finding_

`catch when (_options.FallbackToL1OnL2Failure)` has no exception type and the flag defaults to true. An OperationCanceledException raised by the caller's own token inside _l2.GetAsync/ExistsAsync/RemoveAsync/RemoveByPrefixAsync/ClearAsync is caught and turned into Miss()/false/silent success, so a cancelled operation is indistinguishable from a normal miss and Remove/Clear report success without having run.

#### SH-M038 — MemoryCache leaks a KeyLock refcount (and its semaphore) when WaitAsync throws

`../Birko.Caching/Memory/MemoryCache.cs:104`

AcquireKeyLock increments Refs, then `await keyLock.Semaphore.WaitAsync(ct)` runs OUTSIDE the try/finally. If the token is cancelled while waiting (or the semaphore was disposed by Dispose()), the exception propagates and ReleaseKeyLock is never called: Refs never returns to 0, so the lock is never marked Removed, never removed from _locks and its SemaphoreSlim is never disposed. One cancelled GetOrSetAsync per key permanently pins an entry — defeating the CR-M030 bounded-map fix. HybridCache's equivalent finally is unconditional and does not leak.

#### SH-M039 — A stored null is a Hit for ANY requested T, so a filtered collection read can silently return zero rows

`../Birko.Caching/Memory/MemoryCache.cs:55`

GetAsync tests `entry.Value is null` BEFORE the `is T` type test and returns Hit(default!). So after the single-result read caches a null under a key (line 62 of the cached store), a collection read for the same colliding key does GetAsync<List<T>>, gets HasValue==true with Value==null, and returns `Enumerable.Empty<T>()` (cached store line 89) — a query over a table that does contain matching rows reports no rows, with no error and no database round-trip, for the whole 5-minute TTL. Distinct from the key-collision finding: the defect is the type-blind null branch.

#### SH-M040 — Stale Redis __meta hash from an earlier sliding write hijacks a later entry's expiration policy

`../Birko.Caching.Redis/RedisCache.cs:82`

SetAsync writes the `__meta` hash only when SlidingExpiration is set and never deletes an existing one. Write k with Sliding(5m) (meta {sliding:300, absoluteDeadline:-1}, meta TTL 5m), then overwrite k with Absolute(1h): the value gets a 1h TTL but the meta survives, so the next GetAsync calls RefreshSlidingExpirationAsync, sees sliding=300/deadline=-1 and re-expires the value to 5 minutes on every read — the entry silently behaves as the old sliding policy forever. With a stale deadline already past, the same path DELETES the freshly written key on its first read.

#### SH-M041 — RedisCache treats the removal prefix as an unescaped Redis glob pattern; MemoryCache treats it literally

`../Birko.Caching.Redis/RedisCache.cs:163`

RemoveByPrefixAsync builds `pattern: $"{fullPrefix}*"` with no escaping of glob metacharacters, while MemoryCache does a literal StartsWith(prefix, Ordinal). A caller prefix containing `*`, `?`, `[` or `\` (e.g. "user:*:sessions") therefore deletes a much wider key set on Redis than on memory — same ICache call, different (destructive) blast radius per backend.

#### SH-M042 — RedisCache mixes two database indexes: the manager's for data, settings.Database for SCAN and FLUSHDB

`../Birko.Caching.Redis/RedisCache.cs:163`

All data operations use _connectionManager.GetDatabase(), which binds the index captured by RedisConnectionManager, but RemoveByPrefixAsync passes `database: _settings.Database` to KeysAsync and ClearAsync flushes _settings.Database. With the shared-manager constructor (line 43) the two RedisSettings need not agree, so invalidation scans a database the cache never wrote (silent no-op — RemoveByPrefixAsync reports success having deleted nothing, leaving the SQL query cache stale) and ClearAsync FLUSHDBs an unrelated database.

#### SH-M043 — Prefix removal and FLUSHDB target only one endpoint, so invalidation silently misses cluster shards

`../Birko.Caching.Redis/RedisCache.cs:156`

Both RemoveByPrefixAsync and ClearAsync obtain a single IServer (RedisConnectionManager.GetServer() returns multiplexer.GetServer(endpoints[0])). On a multi-node/cluster deployment the SCAN only enumerates that node's keyspace and FlushDatabaseAsync only clears that node, so entries on the other shards survive a table invalidation and keep being served, while the call completes successfully.

#### SH-M044 — Cache invalidation runs before commit and uncommitted rows are cached under globally shared keys

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:107`

The store inherits IAsyncTransactionalStore/SetTransactionContext, so its writes can be inside an outer SqlUnitOfWork transaction, yet InvalidateCacheAsync fires immediately after the base write. Two consequences: (a) a concurrent reader can repopulate the cache with the pre-commit state right after the invalidation, and that stale entry then survives the commit for the whole TTL; (b) a read issued after a write inside the transaction caches UNCOMMITTED rows under a key with no session identity, so other callers read rows that a rollback later erases.

#### SH-M045 — A cancelled token after a committed write skips invalidation entirely, leaving the cache stale

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:175`

Every write override awaits base.*Async(...) and then InvalidateCacheAsync(ct). Both MemoryCache.RemoveByPrefixAsync and RedisCache.RemoveByPrefixAsync begin with ct.ThrowIfCancellationRequested(), so if the token is cancelled after the row was written the invalidation throws before removing anything — the write is durable but the cached reads for that table keep serving pre-write rows until DefaultExpiration. RedisCache also aborts mid-SCAN (line 165), dropping the buffered partial batch, giving a half-invalidated table.

#### SH-M046 — DestroyAsync (DROP TABLE) is not overridden, so the cache keeps serving rows of a dropped table

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:20`

AsyncDataBaseStore.DestroyAsync is public virtual and issues DROP TABLE directly through the connector, bypassing the *Core template. CachedAsyncDataBaseBulkStore overrides the six *Core writes plus UpdateAsync(filter,PropertyUpdate)/DeleteAsync(filter) but not DestroyAsync, so after a Destroy the sql:{table}: entries survive and every cached read returns rows from a table that no longer exists — the exact class of gap the CR-C16 comment at line 146 was written about.

#### SH-M047 — Cached entities and lists are shared mutable references with no copy on store or retrieve

`../Birko.Data.SQL.Caching/Stores/CachedAsyncDataBaseBulkStore.cs:92`

The collection overload caches the materialized List<T> instance and returns that same instance to every subsequent hit (MemoryCache stores object references, no cloning); the single overload does the same with the entity. Any caller that mutates a returned entity, or casts the IEnumerable<T> back to List<T> and adds/removes, silently rewrites what the cache serves to all other callers. The framework's own ICopyable exists for this and is unused here. On a Redis backend the same code path round-trips through JSON and does NOT share state — so the two backends differ observably.

#### SH-M048 — HybridCache.RemoveAsync lets a concurrent read re-backfill L1 from L2 during the removal

`../Birko.Caching.Hybrid/HybridCache.cs:125`

RemoveAsync starts the L1 removal and the L2 removal without ordering or a tombstone. A concurrent GetAsync that misses L1 (just cleared) reads the value still present in L2 and writes it back into L1 (line 73); the L2 delete then completes, leaving L1 holding a value for a key the caller explicitly removed, served for up to L1DefaultExpiration.

### area: data-sync

#### SH-M049 — ConflictResolution.Merge is silently inert

`../Birko.Data.Sync/SyncProvider.cs:378`  ·  _restates a first-pass finding_

The switch in ApplyConflictResolution has cases for UseLocal, UseRemote and Skip only. A CustomConflictResolver returning Merge produces no write, no error and no skip count — the item is counted in TotalProcessed with the conflict unresolved and a knowledge row written as if reconciled. Same at AsyncSyncProvider.cs:379.

#### SH-M050 — SyncQueue releases a semaphore permit it never acquired on cancellation

`../Birko.Data.Sync/SyncQueue.cs:84`  ·  _restates a first-pass finding_

`await _semaphore.WaitAsync(cancellationToken)` (line 66) is inside the try and the finally unconditionally calls `_semaphore.Release()`. When the wait is cancelled, Release runs anyway: with a permit held elsewhere the count is inflated past maxConcurrentSyncs (permitting over-concurrency); with all permits free `SemaphoreSlim(max,max)` throws SemaphoreFullException. The enqueued QueuedSync record also leaks, inflating GetQueueLength forever.

#### SH-M051 — SyncQueue does not serialise per scope, despite its documented contract

`../Birko.Data.Sync/SyncQueue.cs:15`  ·  _restates a first-pass finding_

The class doc says "Ensures only one sync operation runs per scope at a time", but one shared SemaphoreSlim governs all scopes and the per-scope `_queues` are bookkeeping only (DequeueNext just pops a record). With maxConcurrentSyncs > 1 two runs of the SAME scope execute concurrently against the same knowledge rows; with the default 1, unrelated scopes are needlessly serialised.

#### SH-M052 — Preview converts structural data errors into a phantom conflict

`../Birko.Data.Sync/SyncProvider.cs:94`  ·  _restates a first-pass finding_

The bare `catch { preview.Conflicts++; return preview; }` swallows everything except OperationCanceledException — including the InvalidOperationException BuildEntityDictionary throws for an empty or duplicate Guid, the ArgumentException from the knowledge ToDictionary, and any store failure. The caller receives a partially filled preview reporting one conflict, indistinguishable from a real conflict. Same at AsyncSyncProvider.cs:95.

#### SH-M053 — Initial sync mutates the caller's SyncOptions and misreports the direction used

`../Birko.Data.Sync/SyncProvider.cs:130`  ·  _restates a first-pass finding_

`options.Direction = SyncDirection.Download` writes to the passed-in object, so a reused SyncOptions instance stays Download for every later run and for any later Preview. `result.Direction` was captured at line 111 before the override, so the result reports Bidirectional for a run that executed as Download. Same at AsyncSyncProvider.cs:131.

#### SH-M054 — BatchSize unvalidated: 0 throws DivideByZeroException, negative hangs in an infinite loop

`../Birko.Data.Sync/SyncProvider.cs:153`

`var batchNumber = (i / options.BatchSize) + 1;` divides by the caller-supplied BatchSize (public settable int, SyncOptions.cs:29) — 0 throws DivideByZeroException, swallowed by the outer catch as a generic "Sync failed". With a negative BatchSize the loop `for (i = 0; i < allGuids.Count; i += options.BatchSize)` decrements i forever while `Take(negative)` yields nothing, so the run never terminates and OnBatchStarting fires endlessly. Same at AsyncSyncProvider.cs:154.

#### SH-M055 — MaxItems truncates an unordered Guid union and still stamps the scope as synced

`../Birko.Data.Sync/SyncProvider.cs:143`

`allGuids = allGuids.Take(max).ToList()` slices `localDict.Keys.Union(remoteDict.Keys)` with no ordering, so which items are processed depends on dictionary enumeration order and can differ between runs — a capped sync need never converge. SetLastSyncTime is still stamped at line 207, so a capped FIRST run marks the scope non-initial; the untaken remainder is then decided by the Bidirectional rules whose Create is a no-op, leaving those entities permanently unsynced. Same at AsyncSyncProvider.cs:144.

#### SH-M056 — Delete actions bypass CanSaveToLocal/CanSaveToRemote entirely

`../Birko.Data.Sync/SyncProvider.cs:316`

`case SyncAction.Delete:` calls `_localStore.Delete(localItem)` / `_remoteStore.Delete(remoteItem)` with no save-filter consultation, unlike the Create and Update cases. A caller who sets `CanSaveToLocal = x => !x.Protected` to guard rows still has them deleted by the Delete branch — the one destructive path is the only ungated one. Same at AsyncSyncProvider.cs:317.

#### SH-M057 — A save-filter-blocked Update is a silent no-op counted as a successful sync

`../Birko.Data.Sync/SyncProvider.cs:302`

In `case SyncAction.Update:`, when winner == "remote" and CanSaveToLocal returns false the first branch fails, the `else if (winner == "local" ...)` test also fails, and nothing else runs: no write, no SkippedItems++, and under the default OnSaveFilterBlock = Skip no callback either. Control falls through to `result.Processed++` and a knowledge row with both hashes non-null (both deletion flags false) — the item is recorded as reconciled and result.Success stays true. Same at AsyncSyncProvider.cs:303.

#### SH-M058 — Failures during conflict resolution never reach result.Errors, so Success stays true

`../Birko.Data.Sync/SyncProvider.cs:401`

ApplyConflictResolution's catch reports only through `options.OnError?.Invoke(...)`; the SyncError is not added to the batch result, so `result.Success = result.Errors.Count == 0` (line 219) still evaluates true. A store write that throws while resolving a conflict — or an InvalidOperationException from a ThrowException save filter — leaves a run reporting complete success with the conflict unresolved. Same at AsyncSyncProvider.cs:402.

#### SH-M059 — Version hashes are persisted but never compared, so there is no change detection at all

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:391`

LocalVersion/RemoteVersion are written by every CreateKnowledgeItem and surfaced in SyncItemPreview, but no code reads them back for comparison (only `string.IsNullOrEmpty` for the deletion flags). So DetermineSyncAction's both-sides-present arms return an unconditional Update (lines 65, 77, 91): a Download of 100k unchanged rows issues 100k local Update calls every run, and a genuine both-sides-modified conflict can never be detected — the CR-M156 comment at line 84 concedes this.

#### SH-M060 — CosmosDB knowledge writes are silent no-ops when Container is null

`../Birko.Data.Sync.CosmosDB/Stores/AsyncCosmosSyncKnowledgeStore.cs:97`

`UpdateKnowledgeAsync` (97), `DeleteKnowledgeAsync` (128) and `SetLastSyncTimeAsync` (168) all begin `if (Container == null) return;` — an unconfigured container makes a knowledge write complete successfully while persisting nothing, and GetKnowledgeAsync (43) returns an empty dictionary indistinguishable from "scope has no knowledge", which the caller reads as initial-sync. Same in CosmosSyncKnowledgeStore.cs lines 41, 80, 111, 151.

#### SH-M061 — MongoDB knowledge rows can never be updated: [BsonId] Id is never assigned

`../Birko.Data.Sync.MongoDb/Models/MongoSyncKnowledgeItem.cs:28`

`[BsonId] [BsonRepresentation(ObjectId)] public string Id { get; set; } = string.Empty;` is never set by AsyncMongoSyncKnowledgeStore.CreateKnowledgeItem (40-53). The first run's insert-time id generator fills it, but the update path (AsyncSyncProvider.cs:207 → AsyncMongoDBStore.UpdateCoreAsync) issues `ReplaceOneAsync(filter on Guid, item)` with a freshly built item whose Id is empty again, so the replacement's `_id` differs from the stored ObjectId and the server rejects it for altering the immutable `_id`. Every run after the first loses its knowledge round.

#### SH-M062 — RavenDB's deterministic knowledge Guid never becomes the document id, so upserts are inserts

`../Birko.Data.Sync.RavenDB/Stores/AsyncRavenSyncKnowledgeStore.cs:185`

ConvertToRavenItem derives `Guid = item.Guid ?? DeterministicGuid(...)` "so re-syncing the same natural key reuses the same document identity and upserts", but UpdateKnowledgeAsync calls `session.StoreAsync(ravenItem, ct)` — the overload supplying no document id — and RavenSyncKnowledgeItem (via AbstractModel) has no `Id` property for Raven's default FindIdentityProperty convention, while Birko.Data.RavenDB registers no identity convention. The Guid is just a field, so each round stores a new document.

#### SH-M063 — An already-Raven knowledge item bypasses both the deterministic Guid and the tenant stamp

`../Birko.Data.Sync.RavenDB/Stores/AsyncRavenSyncKnowledgeStore.cs:173`

`if (item is RavenSyncKnowledgeItem ravenItem) return ravenItem;` returns before the CR-H103 deterministic-Guid derivation and before the ITenant copy block (line 199). A RavenSyncKnowledgeItem with a null Guid is stored as-is, so the only path meant to make re-syncs idempotent is skipped exactly for the store's own model type.

#### SH-M064 — Duplicate EntityGuid inside one scope makes the RavenDB/CosmosDB knowledge read throw

`../Birko.Data.Sync.RavenDB/Stores/AsyncRavenSyncKnowledgeStore.cs:55`

`items.ToDictionary(x => x.EntityGuid, ...)` throws ArgumentException on a duplicate key, and duplicates are exactly what the identity defects above produce (a new Raven document per round; a new Cosmos document per round for foreign items). Once two rows share an EntityGuid, GetKnowledge/GetLastSyncTime/DeleteKnowledge/SetLastSyncTime all throw and the scope becomes unreadable with no repair path. Same at AsyncCosmosSyncKnowledgeStore.cs:58, CosmosSyncKnowledgeStore.cs:49, RavenSyncKnowledgeStore.cs:51.

#### SH-M065 — ISyncKnowledgeStore has no implementation anywhere, though two stores' docs claim to be one

`../Birko.Data.Sync/Stores/ISyncKnowledgeStore.cs:13`

No type in the repository declares ISyncKnowledgeStore. AsyncRavenSyncKnowledgeStore's summary says "Async RavenDB implementation of ISyncKnowledgeStore" (line 14) and RavenSyncKnowledgeStore's says the same (line 9), but neither declares it; CosmosDB's `UpdateKnowledgeAsync(items, Guid? tenantId = null, ct)` (AsyncCosmosSyncKnowledgeStore.cs:92) does not even match the interface's `UpdateKnowledgeAsync(items, ct)`. The interface's only consumer, TenantSyncProvider, has no constructible knowledge store.

#### SH-M066 — Sync knowledge is tenant-blind in five of seven backends

`../Birko.Data.Sync.Sql/Models/SqlSyncKnowledgeItem.cs:34`

SqlSyncKnowledgeItem, JsonSyncKnowledgeItem, XmlSyncKnowledgeItem, MongoSyncKnowledgeItem and ElasticSyncKnowledgeItem carry no tenant column, and ISyncKnowledgeItemStore / IAsyncSyncKnowledgeItemStore have no tenant parameter, so queries are `x.Scope == scope` only. Two tenants syncing the same scope name share one knowledge set: GetLastSyncTime returns the other tenant's timestamp and SetLastSyncTime rewrites every tenant's rows. RavenSyncKnowledgeItem (ITenant) and CosmosSyncKnowledgeItem (TenantId) do model a tenant — isolation depends on the backend chosen.

#### SH-M067 — SetLastSyncTime echoes back a timestamp it did not persist for an empty scope

`../Birko.Data.Sync.Sql/Stores/SqlSyncKnowledgeStore.cs:40`

Last-sync-time is derived as `items.Max(x => x.LastSyncedAt)` and "set" stamps existing rows, so for a scope with zero rows the method writes nothing yet returns `lastSyncTime` — the caller cannot distinguish persisted from discarded. A run that processed nothing (MaxItems = 0, or every item errored so no knowledge row was emitted) stays "never synced" and repeats as an initial sync forever. Identical in AsyncSqlSyncKnowledgeStore.cs:44, AsyncJsonSyncKnowledgeStore.cs:45, AsyncXmlSyncKnowledgeStore.cs:23, AsyncElasticSyncKnowledgeStore.cs:23, AsyncMongoSyncKnowledgeStore.cs:23.

#### SH-M068 — SQL SetLastSyncTime writes rows while a lazy SELECT reader is still open

`../Birko.Data.Sync.Sql/Stores/SqlSyncKnowledgeStore.cs:44`

`var items = Read(x => x.Scope == scope);` returns the connector's `Select` iterator, a `yield return` sequence holding an open DbConnection and DbDataReader for the whole enumeration (AbstractConnector.RunReaderCommand). The `foreach` then issues one `Update(item)` per row on a second connection while that reader scans the same rows. AsyncSqlSyncKnowledgeStore materialises into a List (AsyncDataBaseBulkStore.ReadCoreAsync) and AsyncJsonSyncKnowledgeStore calls `.ToList()` explicitly, so the sync SQL store is alone in interleaving.

#### SH-M069 — Aggregate definitions are unvalidated: duplicate navigations double every operation

`../Birko.Data.Aggregates/Core/AggregateDefinition.cs:43`

HasMany/HasOne append to `_relationships` with no check that the navigation property is already registered and no check that Via/Through was ever called. Registering the same navigation twice yields two descriptors: Flatten overwrites `NestedCollections[nav]` (AggregateMapper.cs:49) so the second read wins, and Expand iterates both descriptors and emits every Insert and Delete for that navigation twice. Omitting Via leaves ForeignKeyProperty null and hands that null straight to the data provider.

### area: entity-localization

#### SH-M070 — Localized filters ignore the read-time base-value fallback, excluding untranslated entities

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:165`

ResolveMatchingGuids derives the GUID set only from translation rows, but ApplyTranslations leaves the base value when no row exists. On culture "sk" an entity whose displayed Name is the untranslated "Chair" is invisible to Read(x => x.Name == "Chair") / Contains / StartsWith / EndsWith - filter and projection disagree for exactly the entities that fall back. The class remarks (LocalizedExpressionVisitor.cs:61-66) document this only for `!=`. Same code at LocalizedBulkStoreWrapper.cs:278, AsyncLocalizedStoreWrapper.cs:159, AsyncLocalizedBulkStoreWrapper.cs:288.

#### SH-M071 — ApplyInMemoryOrderBy throws NullReferenceException when the first OrderBy field's property is missing but a later one exists

`../Birko.Data.Localization/Expressions/LocalizedOrderByHelper.cs:65`  ·  _restates a first-pass finding_

The branch is chosen by loop index (`if (i == 0)`) rather than by whether `ordered` was assigned. A missing property at index 0 hits `continue`, so index 1 takes the else-branch and runs `ordered!.ThenBy(...)` on a null `ordered`. OrderBy<T>.ByName accepts any string, so any order-by whose FIRST field name does not resolve on typeof(T) crashes the localized bulk read, while the same name in a later position is skipped harmlessly. The framework's own OrderByHelper.ApplyTo (Birko.Data.Stores/OrderByHelper.cs:66) uses the correct null guard.

#### SH-M072 — EntityType is written from the runtime type but queried from typeof(T), so localized filters resolve to nothing

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:153`

SaveTranslations stamps EntityType = entity.GetType().Name (242) while ResolveMatchingGuids filters on typeof(T).Name (153). When T is a base type of the stored instances (or the store returns a subclass), no row matches, BuildGuidFilter gets an empty set and returns `x => false`, so a filtered read/Count silently returns zero rows - while ApplyTranslations, which filters by GUID+culture only, still translates the same entities on an unfiltered read. Same at LocalizedBulkStoreWrapper.cs:266/358, AsyncLocalizedStoreWrapper.cs:147/236, AsyncLocalizedBulkStoreWrapper.cs:276/368.

#### SH-M073 — Setting a localizable field to null cannot clear its translation; the stale value is re-applied on the next read

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:254`  ·  _restates a first-pass finding_

SaveTranslations does `if (value == null) continue;`, skipping the field rather than deleting the row. After `entity.Name = null; Update(entity);` on a non-default culture the old row survives and the next read writes it back onto the entity, so the clear silently fails. Empty string IS persisted, so "" is the only way to blank a translation. Present in all four wrappers (LocalizedBulkStoreWrapper.cs:370, AsyncLocalizedStoreWrapper.cs:248, AsyncLocalizedBulkStoreWrapper.cs:380).

#### SH-M074 — Translation upsert is an unguarded read-then-create, producing permanent duplicate rows under concurrency

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:261`  ·  _restates a first-pass finding_

SaveTranslations reads (EntityGuid, FieldName, Culture), takes FirstOrDefault and creates when absent - with no uniqueness constraint on EntityTranslationModel and no guard. Two concurrent writers both see no row and both Create. ApplyTranslations then resolves the field via `translationDict[t.FieldName] = t.Value` (201), i.e. whichever duplicate is enumerated last, and every later upsert only updates FirstOrDefault() - so one duplicate stays permanently stale and reads flip between the two values. Same in all four wrappers.

#### SH-M075 — Sync bulk Update(filter, Action) writes the translation before the inner store persists the entity

`../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs:174`

The wrapper hands the inner store `item => { updateAction(item); SaveTranslations(item); }`. AbstractBulkStore.Update(filter, Action) (Birko.Data.Stores/AbstractBulkStore.cs:74-82) invokes the callback and only then calls Update(item), so the translation row is committed before, and independently of, the entity write; if the inner Update throws the translation persists for an update that never happened. Every other path saves translations AFTER the inner write, and the async twin (AsyncLocalizedBulkStoreWrapper.cs:184-186) does read-modify-write per entity instead.

#### SH-M076 — Async SaveAsync discards the created GUID and returns Guid.Empty when the inner store does not assign data.Guid

`../Birko.Data.Localization/Decorators/AsyncLocalizedStoreWrapper.cs:105`  ·  _restates a first-pass finding_

SaveAsync awaits CreateAsync (which returns the new GUID) but ignores the result and returns `data.Guid ?? Guid.Empty`; sync Save returns `Create(...)`'s value (LocalizedStoreWrapper.cs:97). Against an inner store that returns a GUID without writing it onto the instance, sync callers get the real key and async callers get Guid.Empty. The same assumption silently disables translation persistence, since SaveTranslations bails on a null Guid. Same at AsyncLocalizedBulkStoreWrapper.cs:234.

#### SH-M077 — A localizable-field condition inside || or ! is silently evaluated against the base column

`../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs:134`  ·  _restates a first-pass finding_

FlattenAndAlso only splits AndAlso chains, so an OrElse or Not node is one indivisible residual part and reaches the inner store untouched. `x => x.Name == "Stolicka" || x.Code == "C1"` compares the Slovak term against the English column and returns wrong rows. Static string calls (string.IsNullOrEmpty(x.Name)) and chained receivers (x.Name.ToLower().Contains(q)) degrade the same way, since methodCall.Object must be the property itself (180). The class remarks document the null-compare and `!=` limits but not these.

#### SH-M078 — StringComparison argument on Contains/StartsWith/EndsWith is discarded, turning a case-insensitive filter case-sensitive

`../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs:186`  ·  _restates a first-pass finding_

Extraction matches on methodCall.Method.Name and only inspects Arguments[0], so `x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)` IS extracted but the predicate built is the case-sensitive `v.Contains(q)`. The same query behaves differently on the default culture (inner store, comparison honoured) and a non-default one (in-memory, comparison dropped) - the same species as the SQL Contains(q, StringComparison) bug already recorded in CHANGELOG.

#### SH-M079 — EvaluateExpression swallows every exception and returns null, degrading the condition to a base-column predicate

`../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs:268`  ·  _restates a first-pass finding_

The compile-and-invoke fallback is wrapped in `catch { return null; }`. A failure to evaluate the compared operand yields no constant, so the condition is not extracted and lands in the residual filter, where it queries the untranslated column. The failure is indistinguishable from "this field is not localizable" - the query returns wrong rows rather than reporting that it could not be localized.

#### SH-M080 — In-memory ordering of translated text sorts with the thread culture, not the localization context's culture

`../Birko.Data.Localization/Expressions/LocalizedOrderByHelper.cs:92`

SafeObjectComparer.Compare uses `comparable.CompareTo(y)`, which for string is culture-sensitive on CultureInfo.CurrentCulture. The values sorted are the translations just fetched for _context.CurrentCulture, which this static helper is never given, so a Slovak list ordered by Name is collated with the ambient thread culture (often Invariant on a server), mis-ordering c/s/z-caron. This is the one path whose purpose is localized ordering, and the only sort where the data's culture is known and ignored.

#### SH-M081 — Ordering by a localizable field loads the whole matching set and issues one translation query per entity

`../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs:106`

`_innerStore.Read(rewritten)` runs with no limit/offset, then ApplyTranslations runs per entity, each issuing its own _translationStore.Read (313). Sorting a 100k-row catalogue by localized Name materializes all 100k entities and fires 100k translation queries before Skip/Take discards all but one page; the caller's limit/offset - the only bound available - is deliberately dropped. Same at AsyncLocalizedBulkStoreWrapper.cs:113-117; the per-entity query also applies to the other bulk read paths (72-81, 97-101).

#### SH-M082 — Localized-condition resolution materializes the whole field+culture translation slice, un-tenant-scoped

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:157`

ResolveMatchingGuids builds a filter with EntityType/FieldName/Culture but no EntityGuid, reads the whole result and applies condition.ValuePredicate client-side (165). Every filtered read on a non-default culture pulls that entire slice into memory - and since EntityTranslationModel does not implement ITenant, TenantBulkStoreWrapper (which requires T : ITenant) cannot wrap the translation store, so the slice spans every tenant. Only GUIDs escape, so results stay tenant-correct, but the scan and the in-process exposure of other tenants' text are unbounded.

#### SH-M083 — Entity write and translation write have no transactional boundary, silently desynchronizing the two stores

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:77`

Every path is two independent store operations: Create/Update then SaveTranslations (76-78, 83-84), Delete then DeleteTranslations (89-90), and the bulk variants loop per entity after one inner bulk call (LocalizedBulkStoreWrapper.cs:196-214). A throw from the second half leaves the entity written with no translation, or deleted with its rows orphaned forever - DeleteTranslations is the only deletion site, so nothing ever cleans them. No ITransactionalStore/IUnitOfWork participation, and no failure is reported for the entities already processed.

### area: entity-tagging

#### SH-M084 — Tag/EntityTag omit ITenant, so the framework's own tenant wrapper cannot be applied

`../Birko.Data.Tagging/Models/Tag.cs:9`

Tag (9-15) and EntityTag (9-15) declare a bare `Guid TenantGuid` but do not implement Birko.Data.Tenant.Models.ITenant and lack its `string? TenantName`. TenantStoreWrapper<TStore,T> is constrained `where T : AbstractModel, ITenant`, so a consumer cannot wrap the Tag/EntityTag stores to get the automatic read filtering, TenantMismatchException on update/delete and Strict-mode refusal the rest of the framework relies on. The 'implementations MUST filter every read' contract exists only because the models opted out of the mechanism that would have enforced it.

#### SH-M085 — Guid.Empty is stamped as a real tenant when no tenant is in scope

`../Birko.Data.Tagging/Services/TagService.cs:41`

GetCurrentTenantId() returns a non-nullable Guid, so an implementation whose ambient ITenantContext has no tenant (HasTenant false) must return Guid.Empty, and lines 41, 110 and 138 stamp it without any check. Every unscoped request then writes into one shared TenantGuid = Guid.Empty bucket that a later unscoped read sees in full. The framework's own TenantStoreWrapper.SetTenantGuidIfNeeded explicitly refuses this ('refusing to stamp Guid.Empty') in Strict mode; the tagging base has no equivalent and no way to express 'no tenant'.

#### SH-M086 — AttachTagAsync creates links to nonexistent or other-tenant tags

`../Birko.Data.Tagging/Services/TagService.cs:103`  ·  _restates a first-pass finding_

AttachTagAsync checks only whether the link already exists (105-106); it never calls GetTagByIdAsync to confirm tagId is real and in-tenant, and SetEntityTagsAsync:140 writes links the same way. Any arbitrary Guid produces a persisted EntityTag stamped with the CURRENT tenant but pointing at a deleted or foreign tag. The bad row is invisible because both read paths (98, 193) silently skip links that fail to resolve, so the junction accumulates garbage nothing ever reports or cleans up.

#### SH-M087 — CreateTagAsync silently discards color/group when the name already exists

`../Birko.Data.Tagging/Services/TagService.cs:36`  ·  _restates a first-pass finding_

`if (existing is not null) return ToDto(existing);` at 37 runs before color and group are read. CreateTagAsync("urgent", "#00ff00", "workflow") against an existing "urgent" performs no update and returns a DTO carrying the OLD colour and group. The return type has no created/existing discriminator, so the caller cannot detect that it got something other than what it asked for, and a UI that immediately renders the returned DTO shows a colour the user did not choose.

#### SH-M088 — UpdateTagAsync does not re-check name uniqueness, so renames create duplicate names

`../Birko.Data.Tagging/Services/TagService.cs:73`

`if (name is not null) tag.Name = name.Trim();` writes the new name with no FindTagByNameAsync probe, while CreateTagAsync:36 treats trimmed name as the de-duplication key. Renaming tag B to "urgent" when tag A is already "urgent" yields two tags with the same (TenantGuid, Name); afterwards CreateTagAsync and AttachTagByNameAsync:151 resolve "urgent" to whichever row the hook returns first, so subsequent quick-tags attach to an arbitrary one and the tag list shows a duplicate the user cannot distinguish.

#### SH-M089 — UpdateTagAsync persists a blank name instead of rejecting it or leaving it alone

`../Birko.Data.Tagging/Services/TagService.cs:73`  ·  _restates a first-pass finding_

Line 73 applies `name.Trim()` with no null-or-whitespace handling, while lines 74-75 directly below map null-or-whitespace to null for Color and TagGroup. UpdateTagAsync(id, name: "   ") therefore persists Name = string.Empty, producing an unnamed tag that SearchTagsByNameAsync cannot usefully find and that renders as blank in every UI. The inconsistency inside three adjacent lines indicates the whitespace handling was never extended to name; CreateTagAsync:42 accepts a blank name the same way.

#### SH-M090 — Create and update normalize Color/TagGroup differently in the same class

`../Birko.Data.Tagging/Services/TagService.cs:43`

CreateTagAsync assigns `Color = color` and `TagGroup = group` verbatim (43-44), so CreateTagAsync("x", "  ", "  ") stores two whitespace strings; UpdateTagAsync (74-75) maps exactly those inputs to null. A tag created with a blank colour is thus non-null-but-blank until someone happens to patch it, and any consumer written as `tag.Color is null ? fallback : tag.Color` renders whitespace instead of the fallback. Two normalization policies for the same two fields, 30 lines apart.

#### SH-M091 — DeleteTagAsync destroys all links before the tag with no transaction or compensation

`../Birko.Data.Tagging/Services/TagService.cs:85`  ·  _restates a first-pass finding_

DeleteAllEntityTagsForTagAsync (85) and DeleteTagInternalAsync (86) are two independent awaits. If the second throws (store failure, connectivity, a tenant filter that does not match), the exception propagates while every attachment is already permanently gone and the tag survives — the worst possible ordering, and nothing restores the links. Deleting the tag first would degrade to orphan links, which both read paths already tolerate by skipping unresolvable tags.

#### SH-M092 — DetachTagAsync removes only the first of duplicate links, so the tag still reads as attached

`../Birko.Data.Tagging/Services/TagService.cs:120`  ·  _restates a first-pass finding_

`links.FirstOrDefault(l => l.TagId == tagId)` deletes a single row. Duplicates are reachable because AttachTagAsync's guard (105-106) is a non-atomic read-then-write and EntityTag declares no uniqueness. After a double-attach a user clicking 'remove tag' sees it still present in GetEntityTagsAsync's output and must click again; `links.Where(...)` with a loop would converge in one call.

#### SH-M093 — Attach/Set idempotency is read-then-write with no lock or unique constraint

`../Birko.Data.Tagging/Services/TagService.cs:105`  ·  _restates a first-pass finding_

AttachTagAsync reads links at 105 and writes at 108 with nothing in between; SetEntityTagsAsync does the same across 126-146. Two concurrent attaches of the same tag both pass the `links.Any(...)` guard and both insert, with no unique index behind them to reject the second. The duplicate then propagates: GetEntityTagsAsync:98 lists the same tag twice, GetEntityTagsBatchAsync:192 duplicates it per entity, DetachTagAsync:120 removes only one, and SetEntityTagsAsync:131 keeps both when the tag is still desired.

#### SH-M094 — Neither model declares the unique index its invariants depend on

`../Birko.Data.Tagging/Models/EntityTag.cs:9`

EntityTag (9-15) and Tag (Tag.cs:9-15) carry no [Table], [IndexedField] or [CompositeIndex] attributes, so there is no storage backstop for either invariant the service enforces optimistically: unique (TenantGuid, Name) for CreateTagAsync's de-duplication and unique (TenantGuid, EntityType, EntityId, TagId) for AttachTagAsync's idempotency. Birko.Data.SQL ships [CompositeIndex(..., IsUnique = true)] for exactly this per-tenant case. Absent it, every race above silently persists a duplicate, and on SQL backends every link read is an unindexed scan of the shared junction.

#### SH-M095 — GetEntityTagsBatchAsync returns keys for entities the caller never requested

`../Birko.Data.Tagging/Services/TagService.cs:189`  ·  _restates a first-pass finding_

`links.GroupBy(l => l.EntityId)` populates the result from whatever the hook returned; the grouping is never intersected back against entityIds (the loop at 199-202 only ADDS missing keys), and the base also never asserts `l.EntityType == entityType`. An implementation that over-fetches — a too-broad EntityType-only query, a missing tenant filter, or an ignored entityIds argument — leaks other entities' and other tenants' tag sets to any caller that enumerates the dictionary instead of indexing it.

#### SH-M096 — entityType is neither validated nor canonicalized before being persisted

`../Birko.Data.Tagging/Services/TagService.cs:114`

Lines 114 and 145 assign the raw argument to EntityTag.EntityType (non-nullable string) with no null check, no emptiness check and no Trim, while tag names ARE trimmed at 36, 64, 73 and 151. Passing " Building" or "building" writes links that GetEntityTagsAsync("Building") never returns on a case-sensitive backend (PostgreSQL, MongoDB) but DOES return under MSSQL's default case-insensitive collation, so identical code partitions an entity's tags differently per provider. `null` is stored through a non-nullable property, surfacing as a distant NRE or NOT NULL violation.

#### SH-M097 — SetEntityTagsAsync can leave the entity in a state that is neither the before nor after set

`../Birko.Data.Tagging/Services/TagService.cs:131`

The removals (131-132) are committed one by one and only then the additions (139-146), with no transaction and no compensation. If any CreateEntityTagAsync throws — or the cancellation token fires mid-loop — the caller gets an exception after the old tags are already gone and before the new ones exist, so SetEntityTagsAsync("Building", B, [T2, T3]) on {T1, T2} can leave exactly {T2}. A retry is not equivalent either, since nothing records how far it got.

#### SH-M098 — Delete hooks take the whole entity, so a link whose Guid was left null silently no-ops

`../Birko.Data.Tagging/Services/TagService.cs:121`

DeleteEntityTagAsync(link) at 121/132 and DeleteTagInternalAsync(tag) at 86 pass the object a read hook returned rather than an identifying tuple. AbstractModel.Guid is `Guid?` defaulting to null and nothing asserts it is populated, so an implementation whose read projection drops Guid issues a delete keyed on null — either matching nothing while DetachTagAsync still returns successfully, or, on a store treating a null key as 'no predicate', matching far more than intended. The hooks return void, so the base cannot tell the cases apart.

### area: event-bus-and-messaging

#### SH-M099 — Redis non-group mode re-reads an expired or unparseable entry forever

`../Birko.MessageQueue.Redis/RedisConsumer.cs:319`  ·  _restates a first-pass finding_

ProcessEntryAsync returns early when ParseStreamEntry yields null (line 302) or the ttl_ms check expires the entry (line 319), skipping `if (!useConsumerGroup) state.LastReadId = entry.Id;` at line 354. Without a consumer group the read position IS state.LastReadId, so XREAD returns the same entry on every poll — an unbounded hot loop on one poisoned or expired entry, hammering Redis with no progress.

#### SH-M100 — Redis non-group mode advances the read cursor even when the handler throws

`../Birko.MessageQueue.Redis/RedisConsumer.cs:354`  ·  _restates a first-pass finding_

The `state.LastReadId = entry.Id` assignment sits AFTER the try/catch at 333-350, so a failed handler still advances the cursor. With no consumer group there is no PEL and no XAUTOCLAIM reclaim, so the message is silently lost — while the identical failure under a consumer group is redelivered. Same code, opposite guarantee, decided only by whether ConsumerGroup is set, and the bare `catch` at 343 logs nothing.

#### SH-M101 — Redis pending-entry reclaim only runs on an empty poll, so a busy stream never redelivers failures

`../Birko.MessageQueue.Redis/RedisConsumer.cs:250`

ReclaimPendingEntriesAsync is called only in the `else` branch taken when the poll returned zero entries. On a continuously-busy destination every poll returns entries, so XAUTOCLAIM never runs and an entry left unacked by a throwing handler or by RejectAsync(requeue:true) is never redelivered for the subscription's whole life. The spec's promise that an empty poll reclaims it only holds on an idle stream.

#### SH-M102 — Redis auto-created consumer group starts at 0-0, replaying the whole stream despite FromBeginning=false

`../Birko.MessageQueue.Redis/RedisConsumer.cs:501`

EnsureConsumerGroupAsync always calls StreamCreateConsumerGroupAsync(streamKey, groupName, "0-0", createStream: true), so the group's last-delivered-id is 0-0 and the first `>` read delivers every retained entry. ConsumerOptions.FromBeginning is documented as "When false, only new messages are received" (ConsumerOptions.cs:26-29) and is not consulted here at all — attaching a new group to an existing stream reprocesses the entire history, duplicating side effects.

#### SH-M103 — A transient Redis error during the FromBeginning pending drain kills the subscription permanently

`../Birko.MessageQueue.Redis/RedisConsumer.cs:395`

ProcessPendingEntriesAsync (and EnsureConsumerGroupAsync) run BEFORE the `while` loop's try/catch. A RedisException there propagates out of RunPollLoopAsync into PollLoopAsync's generic catch (line 173), which fires PollError and calls RemoveSubscription — the subscription is gone for good. The identical RedisException one line later, inside the loop, is retried after 2 s (line 263). A connection blip at subscribe time is permanently fatal; mid-run it is not.

#### SH-M104 — Redis _pendingAck grows without bound and a repeated message id loses the earlier stream entry id

`../Birko.MessageQueue.Redis/RedisConsumer.cs:325`

`_pendingAck[message.Id] = new PendingMessage{...}` is an indexer assignment with no eviction. A ManualAck handler that neither acks nor rejects leaves the entry forever (only Dispose clears it), leaking one entry per message. Worse, when the same message id reappears (duplicate publish, or the same entry re-processed by the reclaim pass) the assignment overwrites the previous PendingMessage, discarding its StreamEntryId — that PEL entry can then never be XACKed by AcknowledgeAsync.

#### SH-M105 — MqttSubscription never removes the consumer's local handler, so a disposed subscription resurrects

`../Birko.MessageQueue.MQTT/MqttSubscription.cs:40`  ·  _restates a first-pass finding_

UnsubscribeAsync/Dispose only issue a broker UNSUBSCRIBE; neither calls MqttConsumer.RemoveHandler (verified dead — grep finds only its own declaration at MqttConsumer.cs:133). The handler stays in `_handlers`, so ResubscribeAllAsync (MqttConsumer.cs:65) re-subscribes that filter on the next reconnect and the supposedly-disposed handler starts receiving again.

#### SH-M106 — MqttConsumer.SubscribeAsync overwrites an existing handler for the same topic filter

`../Birko.MessageQueue.MQTT/MqttConsumer.cs:92`

`_handlers[destination] = handler` is a dictionary indexer assignment keyed by filter. A second SubscribeAsync("sensors/#", otherHandler) silently replaces the first handler, which stops receiving while its ISubscription still reports IsActive == true. The in-memory backend fans out to every subscriber and Redis starts an independent poll loop per subscription — three backends, three answers to "two subscribers on one destination".

#### SH-M107 — MqttConsumer ignores ConsumerOptions entirely and its ack/reject are success-reporting no-ops

`../Birko.MessageQueue.MQTT/MqttConsumer.cs:119`

The `options` parameter of both SubscribeAsync overloads is never read, so AckMode, PrefetchCount, GroupId and FromBeginning are all silently dropped. AcknowledgeAsync (119) and RejectAsync (126) return Task.CompletedTask unconditionally, so a caller running the documented ManualAck flow — or calling RejectAsync(requeue:true) for a poison message — gets a success result while nothing happened and the message is gone.

#### SH-M108 — A throwing Disconnected event subscriber prevents MQTT auto-reconnect from ever starting

`../Birko.MessageQueue.MQTT/MqttMessageQueue.cs:136`

OnDisconnectedAsync awaits `Disconnected.Invoke()` before the `if (_disposed || !_options.AutoReconnect) return;` check and before StartNewReconnectCts(). If any subscriber to the public Disconnected event throws, the exception propagates into MQTTnet's event invocation and the reconnect loop is never launched — the client stays offline permanently even with AutoReconnect true.

#### SH-M109 — InMemory transport swallows the fault the distributed bus raises for transport retry

`../Birko.MessageQueue.InMemory/InMemoryChannel.cs:149`  ·  _restates a first-pass finding_

The dispatch loop's bare `catch { }` discards every handler exception. DistributedEventBus's consume callback deliberately rethrows (CR-H114, DistributedEventBus.cs:205) so the transport can retry / dead-letter, but over InMemoryMessageQueue that rethrow is unobservable: no redelivery, no DLQ, no log, no hook. Any test of the distributed bus's retry contract on the in-memory transport passes vacuously.

#### SH-M110 — InMemory ManualAck drops the pending entry on handler failure, so requeue is impossible

`../Birko.MessageQueue.InMemory/InMemoryConsumer.cs:52`  ·  _restates a first-pass finding_

The wrapped handler removes message.Id from _pendingAck in its catch before rethrowing. A poison-message flow that then calls RejectAsync(id, requeue: true) finds nothing (TryRemove fails at line 89) and the CR-M202 requeue write never runs — the message is lost precisely in the failure case requeue exists for. Redis leaves the PEL entry so reclaim still redelivers it.

#### SH-M111 — InMemoryConsumer.Dispose leaves its handlers registered on the shared channel

`../Birko.MessageQueue.InMemory/InMemoryConsumer.cs:108`

Dispose sets `_disposed = true` and clears _pendingAck but never removes the subscriber ids it registered via _channel.AddSubscriber (line 58). The wrapped handlers keep being invoked by the dispatch loop after disposal, and any AcknowledgeAsync/RejectAsync those handlers call now throws ObjectDisposedException from lines 80/87 — a disposed consumer that still delivers but can no longer ack.

#### SH-M112 — InMemory delayed send uses the caller's cancellation token for work that outlives the call

`../Birko.MessageQueue.InMemory/InMemoryProducer.cs:38`

The detached `Task.Run(async () => { await Task.Delay(message.Delay.Value, cancellationToken); await _channel.WriteAsync(destination, message, cancellationToken); }, cancellationToken)` captures the per-call token, then SendAsync returns success immediately. In the normal ASP.NET case that token is the request's and is cancelled as soon as the response completes, so a message with Delay = 2s is silently dropped in the ContinueWith fault sink after the caller was told the send succeeded.

#### SH-M113 — QueueMessage.Delay is honoured only by the in-memory backend

`../Birko.MessageQueue.MQTT/MqttProducer.cs:35`

MqttProducer.SendAsync and RedisProducer.SendAsync (RedisProducer.cs:36) never read message.Delay — verified by grep: the only reads in the area are InMemoryProducer.cs:34. Setting Delay on MQTT or Redis publishes immediately with no error, so the same publishing code silently changes semantics with the configured transport, and Delay is not carried on the wire either.

#### SH-M114 — AddDistributedEventBus + AddOutboxEventBus throws InvalidOperationException at host startup

`../Birko.EventBus.MessageQueue/DistributedEventBusHostedService.cs:23`  ·  _restates a first-pass finding_

The ctor does `bus as DistributedEventBus ?? throw`. AddOutboxEventBus decorates IEventBus with OutboxEventBus, so the documented outbox-over-distributed composition resolves an OutboxEventBus and the hosted service (registered by AddDistributedEventBus whenever AutoSubscribe is true) fails to construct, killing startup. Unwrapping OutboxEventBus.Inner — as AddOutbox does for the processor — would fix it, but Inner is `internal` to Birko.EventBus.Outbox so this assembly cannot reach it.

#### SH-M115 — AutoSubscriber instantiates every registered handler from the root provider as a registration probe

`../Birko.EventBus.MessageQueue/AutoSubscriber.cs:88`

DiscoverEventTypes calls `_serviceProvider.GetService(IEnumerable<IEventHandler<T>>)` and enumerates it purely to test "is it registered". Handlers are AddTransient (EventBusServiceCollectionExtensions.cs:61), so every handler in the app is constructed at startup, its ctor side effects run, and the instances are discarded while staying rooted for disposal. Any handler with a scoped dependency makes GetService throw InvalidOperationException, which propagates out of StartAsync and aborts host startup.

#### SH-M116 — Distributed delivery silently acknowledges undecodable messages

`../Birko.EventBus.MessageQueue/DistributedEventBus.cs:149`

The transport callback returns plainly when the envelope deserializes to null (149), when Type.GetType(envelope.EventType) fails or the type is not assignable to TEvent (154), or when the payload deserializes to null (161). Returning normally tells the transport the delivery succeeded, so the message is acked and dropped: a rolling deployment where the consumer lacks the new event type discards every message of that type with no log, no DLQ and no counter.

#### SH-M117 — DistributedEventBus never uses IEventScopeAccessor despite being its stated target

`../Birko.EventBus/Core/IEventScopeAccessor.cs:13`  ·  _restates a first-pass finding_

The interface doc names "the outbox processor loop and the message-queue consumer callback" as the two tenant-less dispatch sites, but only OutboxProcessor consumes it (grep: the remaining references are Birko.EventBus.Tenant and its DI extension). SubscribeToTransportAsync builds an EventContext carrying envelope.TenantGuid then invokes handlers directly, so handlers relying on the ambient tenant break under TenantIsolationMode.Strict on the distributed path.

#### SH-M118 — Deduplication marks before handlers run, defeating the transport retry the distributed bus enables

`../Birko.EventBus/Deduplication/DeduplicationBehavior.cs:31`

On the consume path the pipeline runs inside the delivery callback (DistributedEventBus.cs:183) and DeduplicationBehavior reserves the EventId via TryMarkProcessedAsync BEFORE calling next(). When a handler then throws, the callback deliberately rethrows so the transport redelivers (CR-H114) — but the redelivered message hits the already-marked EventId and is dropped. Registering AddEventDeduplication on a distributed bus turns every handler failure into permanent event loss, silently disabling the retry configured alongside it.

#### SH-M119 — No-handler publish skips the pipeline, so deduplication never records the EventId

`../Birko.EventBus/Local/InProcessEventBus.cs:61`  ·  _restates a first-pass finding_

PublishAsync returns as soon as handlers.Count == 0, before _pipeline.ExecuteAsync. DeduplicationBehavior therefore never marks the EventId; if a handler is registered later (or another bus instance handles the retry) the same EventId is processed as first-seen. RuleFilterBehavior and any auditing/metrics behaviour are likewise bypassed for handler-less event types, so a publish that should have been recorded leaves no trace.

#### SH-M120 — Singleton in-process bus resolves handlers from the root provider, so scoped handler deps fail

`../Birko.EventBus/Extensions/EventBusServiceCollectionExtensions.cs:32`

AddEventBus registers `new InProcessEventBus(sp, ...)` where sp is the ROOT provider, and GetHandlers calls _serviceProvider.GetService (InProcessEventBus.cs:109) with no scope. A handler depending on a scoped service — the normal shape for a DbContext, a repository, or ITenantContext — throws InvalidOperationException("Cannot resolve scoped service from root provider") on every publish. Nothing in the publish path creates an IServiceScope.

#### SH-M121 — Outbox: cancellation during publish is recorded as a failed attempt and can permanently fail the entry

`../Birko.EventBus.Outbox/Publishing/OutboxProcessor.cs:109`

The `catch (Exception ex)` has no OperationCanceledException filter. On host shutdown the inner bus's PublishAsync observes the stopping token and throws OCE, which becomes MarkFailedAsync(entry.Id, "...canceled...", MaxAttempts, cancellationToken) — incrementing Attempts and, at the cap, pinning a perfectly good event to Failed forever. It also passes the already-cancelled token to MarkFailedAsync, so a real store's call throws, escaping ProcessBatchAsync and leaving remaining claimed entries stuck in Publishing.

#### SH-M122 — EventStoreEventBus range append publishes one at a time with no record of a partial publish

`../Birko.EventBus.EventSourcing/EventStoreEventBus.cs:42`

AppendRangeAsync appends the whole list, then loops publishing each event; a throw on event N propagates with events 1..N-1 published and N..end not, all already durably appended. There is no retry, no record of the boundary and no way for the caller to know where it stopped, so projections silently diverge from the store. The single-append path (line 34) has the same append-then-publish gap for one event.

### area: filter-expression-translation

#### SH-M123 — Comparison with the constant operand on the LEFT is not flipped (SQL)

`../Birko.Data.SQL/SQL/DataBase.cs:523`  ·  _restates a first-pass finding_

`x => 5 > x.Age` takes Greather from the node type, binds 5 as the condition's value (via the ConstantExpression branch at 665) and Age as its Name, rendering `Age > @p(5)` — the inverse of the C# meaning. Only the value-expression path flips, via BuildValueComparison/FlipComparison (1176-1179), so the fix pattern already exists in the same file. Reversed-operand comparisons are idiomatic C# and produce silently wrong rows on reads and on filter-based writes.

#### SH-M124 — Comparison with the constant operand on the LEFT is not flipped (ElasticSearch)

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:227`  ·  _restates a first-pass finding_

ParseComparison takes `field` from whichever operand yielded an ITermQuery with a Field and `value` from whichever yielded a Value, then switches on binary.NodeType with no compensation for the swap. `x => 18 > x.Age` emits NumericRangeQuery{Field="age", GreaterThan=18}, i.e. Age > 18. Identical defect to the SQL side, and the two backends are wrong in the same direction, so a cross-backend test would not catch it.

#### SH-M125 — Column-versus-column comparison renders as `B = NULL` and matches no rows (SQL)

`../Birko.Data.SQL/SQL/DataBase.cs:813`

For `x => x.StartDate == x.EndDate` neither operand is a value-expression, so both go through the member branch and each assigns `parent.Name = name` (813) — the right operand overwrites the left. Values is never populated, so EqualConditionStrategy.BuildValueExpression returns the literal "NULL" and the SQL is `Table.EndDate = NULL`, UNKNOWN for every row. Same shape for `!=`, for `x.Name.StartsWith(x.Prefix)` (pattern operand overwrites the object's Name), and for `x.TagsA.Contains(x.TagB)`. No error is raised.

#### SH-M126 — Column-versus-column comparison becomes "field does not exist" (ElasticSearch)

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:230`

For `x => x.A == x.B`, ParseMember returns TermQuery{Field} with Value unset for both operands, so `field = "a"` and `value = null`. The null-value branch then treats it as a null comparison and emits BoolQuery{MustNot=[ExistsQuery a]} — documents where A is absent. A perfectly sensible predicate silently returns an unrelated set instead of being refused; ParseContains rejects the analogous both-operands-bound case (580) by returning null.

#### SH-M127 — string.IsNullOrEmpty(x.Col) renders as `Col = NULL` and matches no rows (SQL)

`../Birko.Data.SQL/SQL/DataBase.cs:546`  ·  _restates a first-pass finding_

IsNullOrEmpty matches no case in the method-name switch, so Type stays Equal; the single argument resolves the column into Name and no value is ever bound, leaving Values null. EqualConditionStrategy emits the literal "NULL", giving `Col = NULL` — UNKNOWN for every row, so the filter silently matches nothing, and inside an AND the whole predicate matches nothing. ElasticSearch translates the same call to BoolQuery.MustNot(WildcardQuery "*").

#### SH-M128 — An OR rule group is rendered as AND (RuleConditionConverter)

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:59`  ·  _restates a first-pass finding_

ConvertGroup marks children via SetOr, then wraps them with Condition.AndSubCondition, which passes isOr: false. AppendSubConditionsTo (AbstractConnectorBase:280) picks its separator from the PARENT's IsOr and explicitly ignores the children's flags, so the group renders `(a AND b)` — strictly fewer rows than the rule specified. Child marking only takes effect in a top-level condition list. Condition.OrSubCondition exists and is the correct wrapper.

#### SH-M129 — A Between rule keeps only its lower bound (RuleConditionConverter)

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:134`  ·  _restates a first-pass finding_

MapOperator maps ComparisonOperator.Between to (ConditionType.GreatherAndEqual, WrapValue(rule.Value)) — one `>=` against a single value. ConditionType has no Between member and no two-value range is built, so the upper bound is discarded and the generated predicate matches strictly more rows than the rule specifies, with no error.

#### SH-M130 — A rule with a null value renders `Field = NULL` instead of IS NULL (RuleConditionConverter)

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:150`

WrapValue returns null for a null rule.Value, so an Equal/NotEqual/comparison rule with no value produces a Condition with Values = null. EqualConditionStrategy then emits `Field = NULL`, UNKNOWN for every row. The converter already has ConditionType.IsNull (used for the IsNull/IsNotNull operators), and DataBase.ParseConditionExpression converts a null operand to IsNull for exactly this reason (687, 723) — the rules path does not.

#### SH-M131 — An `In` rule whose value is a string enumerates it as characters (RuleConditionConverter)

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:142`

`rule.Value as IEnumerable ?? WrapValue(rule.Value)` succeeds for a string, because string implements IEnumerable. A rule `Status In "ABC"` therefore renders `Status IN (@p0, @p1, @p2)` bound to 'A','B','C' rather than the single value "ABC". DataBase.InvokeExpression (1320) special-cases string for precisely this reason; the rules converter does not, so the same intent gives different rows depending on which entry point built the condition.

#### SH-M132 — ComparisonOperator.Like double-wraps the pattern in %…%, destroying an anchored LIKE pattern

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:141`

Like maps to ConditionType.Like, the same type used for Contains. LikeConditionStrategy runs the value through SqlBuilderContext.FormatValue, which returns `%{str}%` for ConditionType.Like. An explicit rule pattern such as `FV2026%` becomes `%FV2026%%`, so an anchored prefix rule silently matches unanchored, returning more rows than specified. ConditionType has no raw-LIKE member, so an author cannot express an unwrapped pattern at all.

#### SH-M133 — An IRule that is neither Rule nor RuleGroup silently contributes no condition

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:26`

ToConditions' switch ends with `_ => []`, so any other IRule implementation (Birko.Rules is interface-based and extensible) is dropped without error. Combined with ConditionDefinition/AddWhere, a rule set consisting only of such rules produces no WHERE clause at all — every row. RuleSpecification's analogous degradations at least choose an explicit constant; here the outcome is a widened filter that reaches Delete/Update by filter.

#### SH-M134 — LIKE patterns are not escaped, so % and _ in a search term match extra rows

`../Birko.Data.SQL/SQL/Connectors/SqlBuilderContext.cs:49`

FormatValue wraps the bound value as `%{str}%` / `{str}%` / `%{str}` with no escaping of the LIKE metacharacters `%` and `_` (and no ESCAPE clause is ever emitted). `x.Name.Contains("100%")` becomes the pattern `%100%%`, which matches any name containing "100", and `x.Code.StartsWith("A_1")` matches `A21`. C# Contains/StartsWith treat both characters literally, so the SQL backend returns a strict superset of the correct rows.

#### SH-M135 — RenderBoolFragment emits `(col <> 0)` for a bool column, which is a type error on PostgreSQL

`../Birko.Data.SQL/SQL/DataBase.cs:1108`

A bare boolean column inside a CASE test in a WHERE is rendered as `({column} <> 0)`. BooleanField maps to DbType.Boolean and PostgreSQLConnector.ConvertType returns BOOLEAN (line 138), so PostgreSQL rejects the comparison with `operator does not exist: boolean <> integer`. `x => (x.Vip ? x.Premium : x.Score) > 100` therefore works on SQLite/MySQL/MSSQL and fails at execution on one of the four supported providers; there is no provider hook for the boolean literal form.

#### SH-M136 — Value-position CASE renders a bool column as a bare column, which is invalid in MSSQL

`../Birko.Data.SQL/SQL/DataBase.cs:135`

ParseExpression renders a ConditionalExpression as `CASE WHEN {test} THEN … END` where the test goes through ParseExpression, so a bare bool member yields just the column name: `CASE WHEN Table.Vip THEN …`. SQL Server's BIT is not a boolean predicate and rejects this with error 4145. The mirror-image of the previous finding: the WHERE-side renderer (RenderBoolFragment) forces `<> 0` and breaks PostgreSQL, while the value-side renderer omits it and breaks MSSQL. The spec (line 735) records the broken form as expected output.

#### SH-M137 — WHERE columns are emitted as unquoted Table.Column while the FROM table is quoted — breaks on PostgreSQL

`../Birko.Data.SQL/SQL/DataBase.cs:862`

ResolveColumnName returns AbstractField.GetSelectName(true) = `Table.Column`, unquoted, and the strategies interpolate it raw. CreateSelectCommand (AbstractConnectorBase:527) writes the table through QuoteIdentifier, i.e. "Invoice". PostgreSQL folds the unquoted qualifier to `invoice`, which does not match the quoted range-table entry, so every filtered read on a non-lowercase table fails with `missing FROM-clause entry`. Unquoted column names also break for reserved-word identifiers (Order, Group) on every provider.

#### SH-M138 — MSSQL LimitOffsetDefinition emits FETCH NEXT with no OFFSET when offset is null — invalid T-SQL

`../Birko.Data.SQL.MSSql/Database/Connector/MSSqlConnector.cs:241`

The override appends ` OFFSET @OFFSET ROWS` only inside `if (offset != null)`, then unconditionally appends ` FETCH NEXT @LIMIT ROWS ONLY`. T-SQL requires OFFSET to precede FETCH, so any `Read(filter, limit: 10)` with no offset produces a syntax error on MSSQL while working on the other three providers. (Separately, OFFSET/FETCH also requires ORDER BY, and CreateSelectCommand appends the fragment even when orderFields is empty.) The spec's only scenario covers limit+offset, so the broken shape is unrecorded.

#### SH-M139 — InlineConstant escapes only single quotes, so a string constant in a CASE/COALESCE fragment can inject SQL on MySQL

`../Birko.Data.SQL/SQL/DataBase.cs:1132`

Constants inside a raw value fragment cannot be parameterised, so InlineConstant emits `'` + s.Replace("'", "''") + `'`. MySQL/MariaDB honour backslash escapes by default, so the value `\' OR 1=1 -- ` becomes `'\'' OR 1=1 -- '`: the backslash consumes the first quote, the doubled quote closes the literal, and the remainder is executed. Reached by any predicate with a string constant inside a fragment, e.g. `x => (x.Code ?? fallback) == target`, where fallback is application input. The same predicate is safe on SQLite/PostgreSQL.

#### SH-M140 — ElasticSearch String.Contains is an exact term match on a .keyword field, not a substring match

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:546`

ParseExpression on call.Object appends `.keyword` (676), so the QueryStringQuery runs against a non-analysed keyword field: the term must equal the whole field value. `x.Name.Contains("abc")` therefore matches only documents whose Name is exactly "abc", while SQL renders `LIKE '%abc%'` and C# means substring. The code comment on line 546 claims it "mirrors SQL LIKE '%x%'" — a doc/behaviour contradiction, and the most common cross-backend filter silently returns different rows.

#### SH-M141 — ElasticSearch IsNullOrEmpty does not match an indexed empty string

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:517`

ParseIsNullOrEmpty emits BoolQuery.MustNot(WildcardQuery{Value="*"}). A Lucene `*` wildcard matches zero or more characters, so a keyword field indexed with the empty string still matches the wildcard and is excluded by MustNot. The translation therefore means "field is missing", while C# string.IsNullOrEmpty is true for "" as well — so rows with a blank value are silently omitted. The correct shape is a bool with MustNot exists OR a term on "".

#### SH-M142 — A nested member silently resolves to its parent field (ElasticSearch)

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:691`

ParseMember's final branch recurses into `member.Expression` for a parameter-bound sub-expression. For `x => x.Sub.Prop == 1`, `x.Sub.Prop` is not a direct member of the parameter, so it recurses to `x.Sub`, which is, and returns TermQuery{Field="sub"}. The comparison then becomes `sub == 1` instead of `sub.prop == 1` — ES supports the dotted path, so the correct field exists and is simply not used. The query runs and returns the wrong documents rather than being refused.

#### SH-M143 — Any() with no predicate builds a NestedQuery around a valueless TermQuery

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:643`

ParseAny uses Arguments.First() as the collection and Arguments.Last() as the inner predicate. For `x => x.Lines.Any()` there is only one argument, so First and Last are the same node: the inner query becomes TermQuery{Field="lines"} with no Value, wrapped in NestedQuery{Path="lines"}. A term query with no value is not a valid "collection is non-empty" test; the parameterless Any overload needs an ExistsQuery. No guard on Arguments.Count.

#### SH-M144 — ElasticSearch sort fields bypass the camelCase/.keyword naming that filter fields go through

`../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs:485`

ReadStream builds `new FieldSort { Field = field.PropertyName }` — the raw C# property name. Every filter field goes through FormatFieldName (camelCase) and gets `.keyword` for strings, so documents are indexed as `name` while the sort asks for `Name`, and a text field sorted without `.keyword` is rejected outright. Ordering is therefore dropped or errors while the filter on the same property works. Identical code at AsyncElasticSearchStore.cs:454 and in both SearchWithHighlights overloads.

#### SH-M145 — Delete(filter)/Update(filter,…) discard the by-query response, so a failed destructive op reports success

`../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs:281`

The DeleteByQuery / UpdateByQuery return values are never inspected, unlike every other call in the file, which throws on `!IsValid || OriginalException != null`. A version conflict, a missing index, a script compilation error or a partial failure (`response.Failures`) leaves the caller believing the delete/update ran. Same in AsyncElasticSearchStore.DeleteAsync (253) and UpdateAsync (270).

#### SH-M146 — A null Connector turns ElasticSearch Delete(filter)/Update(filter) into a silent success

`../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs:278`

`if (Connector == null) return;` in Delete(filter) (278) and Update(filter, updates) (292) reports success while writing nothing — Connector is only assigned when SetSettings receives a Settings instance (51), so a store configured with any other ISettings silently no-ops. The read paths degrade the same way (ReadStream:466 yields nothing, Aggregate:872 returns empty), so a misconfigured store is indistinguishable from an empty index. Mirrors the SQL null-connector no-op already recorded for DataBaseBulkStore.

#### SH-M147 — A CROSS join carrying conditions drops its ON clause, producing a cartesian product

`../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs:541`

`if (joingroup.Key.JoinType != JoinType.Cross && … Any())` renders ` ON (…)`, so a Join whose JoinType is Cross has its conditions silently discarded. JoinType.Cross is the DEFAULT of both Join.Create overloads and the Join constructor (Join.cs:22/30), so a caller who supplies conditions but forgets the join type gets an unrestricted cross product instead of the restricted join — every row of one table paired with every row of the other.

#### SH-M148 — A join group with no conditions is dropped entirely rather than emitted

`../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs:531`

`joingroups.Where(x => x.Value.Any())` skips any (Right, JoinType) group whose flattened conditions are empty. A conditionless CROSS JOIN is legitimate and the only way to express it, so it disappears from the FROM clause; if the SELECT list or WHERE references a column of the dropped table the statement fails, and if it does not, the result set is quietly narrower than requested. An Inner/LeftOuter join whose conditions failed to translate vanishes the same way.

#### SH-M149 — ParseExpression drops exprType when recursing into a nested member, emitting a malformed fragment

`../Birko.Data.SQL/SQL/DataBase.cs:296`

`return ParseExpression(memberExpression.Expression, parameters, withTableName);` (comment: "not resending type here") recurses without exprType, so column resolution is impossible in the recursion. For a value expression `x => x.Sub.Prop` it descends to `x.Sub` (no type → no name → not a ConstantExpression) and then to the ParameterExpression, which matches no branch and returns null. The caller interpolates that null, so the emitted SQL is `( = @Const0)` — a syntax error at execution rather than a translation failure.

#### SH-M150 — ContainsParameterCore returns false for NewArray/New/Invocation/TypeBinary nodes

`../Birko.Data.SQL/SQL/DataBase.cs:1312`

The recursion covers only Lambda/Parameter/Member/MethodCall/Unary/Binary/Conditional and falls through to `return false`. A parameter-bearing NewArrayExpression, NewExpression, InvocationExpression or TypeBinaryExpression is therefore classified parameter-free, so EvaluateExpression takes the compile path (1263) and Compile() throws "variable 'x' … is not defined". Callers catch that (TryGetLiteralBool:965, line 499) and continue, which is how an `is` test or an Expression.Invoke-composed predicate reaches the constant-true widening at line 916.

#### SH-M151 — ElasticSearch caches compiled delegates in an unbounded static dictionary keyed by expression text

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:828`

`_expressionCache` is a static ConcurrentDictionary<string, Func<object>> keyed by `expr.ToString()`, never trimmed, so long-lived processes accumulate one entry (plus a compiled delegate and its DynamicMethod) per distinct predicate text. Worse, ToString() is not identity: two closures over the same display-class field render identically (`value(C+<>c__DisplayClass0_0).ids`), so a cache hit can return the first closure's captured value. DataBase.cs:37 uses a ConditionalWeakTable keyed by reference specifically to avoid both problems.

### area: llm-provider-and-agents

#### SH-M152 — Streaming fallback re-runs the whole task, re-executing every tool side effect already performed

`../Birko.AI/Agents/Agent.cs:178`

The catch-when wraps the ENTIRE streaming loop, but only iteration 1 streams — iterations 2..N call SendMessageAsync. So any later failure (provider HttpRequestException on iteration 5, or a custom tool throwing) is caught, the conversation is rewound to the pre-streaming snapshot and RunWithHistorySyncAsync re-runs the task from the original prompt. The snapshot restores only the message list; files already written by write_file/edit_file and commands already run by run_command are executed a second time with no record that the first pass happened.

#### SH-M153 — Tool failure counter only matches "Error:", so every exception-path tool failure counts as success

`../Birko.AI/Agents/Agent.cs:338`

errorCount increments only when the result starts with "Error:". Every built-in tool's exception path returns a different prefix — "Error reading file:", "Error writing file:", "Error editing file:", "Error appending to file:", "Error listing files:", "Error searching code:", "Error running command:", "Error getting user input:" — none of which match. A batch where all three tools threw is therefore treated as fully successful, the "All tool executions failed" guard never fires, and the loop burns iterations retrying against silent failures.

#### SH-M154 — A tool_use response with no tool_use blocks appends a user message whose content is an empty array

`../Birko.AI/Agents/Agent.cs:352`

conversation.Add(new Message { Role = "user", Content = toolResults }) runs unconditionally, even when the filtered tool_use loop produced nothing. BuildOpenAiStyleMessages then hits `m.Content is IEnumerable<object> objs && objs.Any()` == false, falls through every branch and emits `{role:"user", content:[]}`. OpenAI-shaped and Anthropic-shaped endpoints both reject an empty content array on a user turn, so the next iteration fails on a message the agent itself fabricated.

#### SH-M155 — Agent constructor unconditionally overwrites the shared provider's MessageCallback

`../Birko.AI/Agents/Agent.cs:22`

`_llmProvider.MessageCallback = messageCallback;` runs with no null check and no save/restore. Providers are created once via LlmProviderFactory and commonly shared across agents: constructing a second Agent over the same provider silently re-points every provider-level retry warning and API-error message at the second agent's callback, and constructing one with messageCallback = null discards the previously configured sink entirely, so retry/error telemetry disappears.

#### SH-M156 — AgentFactory uses a plain Dictionary and hands out live key collections, unlike LlmProviderFactory

`../Birko.AI/Factories/AgentFactory.cs:13`

_factories and _aliases are plain Dictionary instances mutated by Register/RegisterAlias and read by Create/IsRegistered with no synchronization — the exact hazard LlmProviderFactory was deliberately converted to ConcurrentDictionary to remove (CR-L006 comment, LlmProviderFactory.cs:12). A Register racing a Create can corrupt the buckets or spin forever. GetRegisteredAgentTypes()/GetRegisteredAliases() also return the live collections, so enumerating them while another thread registers throws InvalidOperationException.

#### SH-M157 — A JSON-null `error` property turns every valid completion into an error response

`../Birko.AI/Providers/LlmProviderBase.cs:397`

`result.TryGetProperty("error", out var error)` returns true for `"error": null` (the property exists), then `error.TryGetProperty("message", ...)` throws InvalidOperationException because the element is Null, not Object. The outer catch converts the whole thing into LlmResponse.Error("Error parsing OpenAI-style response: …"). Any OpenAI-compatible gateway that always includes `"error": null` alongside a good `choices` array has 100% of its responses discarded. ExtractErrorFromResponseBody guards the same probe with a ValueKind check; this path does not.

#### SH-M158 — `"tool_calls": null` throws out of ParseOpenAiStyleResponse and discards the completion

`../Birko.AI/Providers/LlmProviderBase.cs:411`

`toolCalls.GetArrayLength()` requires ValueKind == Array; providers that emit `"tool_calls": null` on plain text turns make TryGetProperty succeed and GetArrayLength throw InvalidOperationException. The outer catch turns a perfectly good text answer into StopReason "error", which the agent reports as "LLM request failed" and stops on. Every other property probe in this method is shape-guarded except this one.

#### SH-M159 — Unguarded usage GetInt32 discards an otherwise valid response when a token count is null or fractional

`../Birko.AI/Providers/LlmProviderBase.cs:439`

`pt.GetInt32()` / `ct.GetInt32()` are only guarded for property presence, not ValueKind. `"usage": {"prompt_tokens": null}` throws InvalidOperationException and a fractional count throws FormatException; either way the outer catch replaces the already-parsed content and tool calls with LlmResponse.Error. Telemetry that could not be parsed destroys the payload the caller actually needed.

#### SH-M160 — Sync parser ignores finish_reason, so a truncated or filtered completion is reported as "end_turn"

`../Birko.AI/Providers/LlmProviderBase.cs:430`

ParseOpenAiStyleResponse derives StopReason purely from the presence of tool_calls; `choices[0].finish_reason` is never read (grep confirms the only finish_reason read is line 643, in the streaming parser). A response cut off by max_tokens (`finish_reason: "length"`) or blocked (`content_filter`) therefore arrives as "end_turn", the agent emits it as assistant_final and stops as if the task completed. The streaming parser surfaces the raw finish_reason, so the two paths disagree on the same server response.

#### SH-M161 — Streaming forces StopReason "tool_use" even when finish_reason is "length", executing truncated tool arguments

`../Birko.AI/Providers/LlmProviderBase.cs:713`

`if (toolCalls.Count > 0) stopReason = "tool_use";` is checked before finishReason, so a stream truncated mid-arguments still reports a tool call. The accumulated fragment fails to deserialize and becomes `Input = {"_raw": "…"}` (line 700), and the agent executes the tool with that dictionary. Tools with all-optional inputs then run on defaults — list_files with no `directory` lists the workspace root and returns a normal listing — so a corrupted, truncated tool call is reported to the model as a successful execution.

#### SH-M162 — Unguarded usage GetInt32 inside the streaming enumerator throws mid-stream

`../Birko.AI/Providers/LlmProviderBase.cs:632`

The try/catch at line 625 guards only JsonSerializer.Deserialize; `pt.GetInt32()` at 632-633 sits outside it. A usage chunk carrying a null or fractional token count throws InvalidOperationException/FormatException out of the async enumerator, which surfaces in Agent's `await foreach` (Agent.cs:219). With StreamingFallbackToSync on (the default) that triggers the full re-run at Agent.cs:178 — a second billed request plus repeated tool side effects, all because a usage field had an unexpected shape.

#### SH-M163 — ParseOpenAiStreamChunks throws KeyNotFoundException on a chunk whose choice has no `delta`

`../Birko.AI/Providers/LlmProviderBase.cs:595`  ·  _restates a first-pass finding_

`choices[0].GetProperty("delta")` is outside the try/catch (which guards only deserialization), so a final usage/finish-only chunk — a shape the sibling ParseOpenAiStreamChunksWithToolCapture explicitly guards with TryGetProperty at line 646 — throws out of the async enumerator mid-stream, aborting the response after partial text was already yielded to the caller.

#### SH-M164 — SendStreamingWithRetryAsync never disposes the HttpRequestMessage it builds per attempt

`../Birko.AI/Providers/LlmProviderBase.cs:473`

`var request = requestFactory();` with no `using`, whereas SendWithRetryAsync at line 61 uses `using var request`. Every attempt — up to MaxRetries + 1 per streaming call — leaks the request and its HttpContent (the serialized prompt body, which for a long conversation is large) until GC; the terminal non-retryable and exhausted-retry paths dispose the response but never the request. Same helper family, opposite disposal discipline.

#### SH-M165 — BuildOpenAiStyleMessages drops all but the first text block on an assistant tool_use turn

`../Birko.AI/Providers/LlmProviderBase.cs:238`  ·  _restates a first-pass finding_

In the tool_calls branch only textBlocks[0].Text is carried into `content`. An assistant turn whose content is [text "plan…", text "note…", tool_use] silently loses "note…" when replayed, so part of the model's own reasoning disappears from the history on every subsequent turn — while the non-tool branch at line 259 newline-joins all text blocks.

#### SH-M166 — BuildOpenAiStyleMessages silently drops a whole message when an object-list's items lack tool_use_id/content

`../Birko.AI/Providers/LlmProviderBase.cs:281`  ·  _restates a first-pass finding_

Once the first element of an IEnumerable<object> content exposes a `type` property the branch is entered and ends with `continue`. If no element carries BOTH tool_use_id and content (e.g. results supplied as Dictionary<string,object> instead of anonymous objects, where GetProperty returns null), nothing is appended and the message vanishes from the request — leaving the preceding assistant tool_use unanswered, which providers reject.

#### SH-M167 — search_code ignores Options.AllowedExternalPaths, unlike every other file tool

`../Birko.AI/Tools/SearchCodeTool.cs:41`  ·  _restates a first-pass finding_

It calls the two-argument PathHelper.IsPathSafe(targetDir, workingDirectory) while read/write/edit/append/list all pass Options?.AllowedExternalPaths. An agent explicitly granted an external path can read and even write files there but cannot search them, and the error text ("Directory must be in {workingDirectory}") contradicts the granted configuration.

#### SH-M168 — search_code compiles a model-supplied Regex with no match timeout and never checks the cancellation token

`../Birko.AI/Tools/SearchCodeTool.cs:52`

`new Regex(query, …)` is constructed without a matchTimeout, and rx.IsMatch is then applied to every line of every file (lines 64-75) with no cancellationToken check anywhere in the tool. A catastrophically backtracking pattern the model invented (e.g. `(a+)+$` against a long line) pins a CPU indefinitely, and because the token is ignored neither the agent's cancellation nor the caller's can stop it — the whole RunAsync hangs.

#### SH-M169 — run_command reports caller cancellation as "Error: Process timed out" and never inspects the exit code

`../Birko.AI/Tools/RunCommandTool.cs:65`  ·  _restates a first-pass finding_

The linked CTS conflates the caller's cancellation with the timeout, so a cancelled agent run yields a misleading timeout string and the OperationCanceledException is swallowed instead of propagating. Separately (line 74) the exit code is never read: a command failing with empty stderr and empty stdout returns the empty string, which does not start with "Error:", so the agent's failure counter is not incremented and the model is told nothing went wrong.

#### SH-M170 — ask_user reports cancellation as a prompt timeout and leaves the prompt task unobserved

`../Birko.AI/Tools/AskUserTool.cs:53`  ·  _restates a first-pass finding_

Task.Delay is created with the cancellationToken, so on cancellation the delay task wins the WhenAny race and the code returns "Error: Prompt timed out after N seconds." instead of surfacing cancellation. The abandoned promptTask is never awaited, so a later fault on it becomes an unobserved task exception.

#### SH-M171 — AgentOptions.Merge resets scalar settings to the other instance's defaults

`../Birko.AI.Contracts/AgentOptions.cs:101`  ·  _restates a first-pass finding_

Reference/collection fields are guarded (copy only if non-empty/non-null) but every value-typed field is copied unconditionally. Merging a partially populated AgentOptions (e.g. one that only sets WorkingDirectory) silently resets MaxIterations to 10, Verbose to true, Interactive to true and PromptTimeout to 300 on the target — the opposite of the merge semantics the guarded fields imply.

### area: migrations

#### SH-M172 — SQL transactional runner indexes past the end of the list when Commit() throws

`../Birko.Data.Migrations.SQL/SqlMigrationRunner.cs:113`  ·  _restates a first-pass finding_

transaction.Commit() (line 99) is inside the try. If it throws, executed.Count == migrations.Count, so `migrations[executed.Count]` raises ArgumentOutOfRangeException from inside the catch — the commit failure is replaced by an index error and no MigrationException is built. MongoMigrationRunner guards exactly this (line 104); SQL and ExecuteWithoutTransaction (line 141) kept the unguarded form.

#### SH-M173 — SqlIndexBuilder discards Unique() on the connector path

`../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:320`  ·  _restates a first-pass finding_

The connector path builds `new IndexDefinition { Name = _indexName }` and never sets Unique, although AbstractConnectorBase.CreateIndexSql:342 honours `index.Unique`. The raw-SQL fallback (line 336) does emit CREATE UNIQUE INDEX. So CreateIndex(...).Unique().Build() yields a non-unique index whenever a connector is supplied — and SqlMigrationRunner always supplies one, so the constraint is silently absent in production.

#### SH-M174 — CosmosCollectionBuilder records a partition key that can never be applied

`../Birko.Data.Migrations.CosmosDB/Context/CosmosDBSchemaBuilder.cs:146`  ·  _restates a first-pass finding_

CreateCollection creates the container with the hard-coded path "/id" (line 22) BEFORE returning the builder, and CosmosCollectionBuilder never overrides Build(). `_partitionKeyPath` is written by both WithField overloads (lines 164, 173) and read by nothing, so WithField(..., isPrimary: true) silently yields a container partitioned on /id — and a Cosmos partition key cannot be changed afterwards.

#### SH-M175 — Unrecognised filter operators degrade to equality instead of failing

`../Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs:163`  ·  _restates a first-pass finding_

`_ => "="` appears in SqlDataMigrator:163, ElasticSearchDataMigrator:208, RavenDBDataMigrator:156 and CosmosDBDataMigrator:227. Combined with ExtractValue's `_ => element.ToString()` for arrays, {"tags":{"$in":["a","b"]}} becomes tags = '["a","b"]' (matches nothing), while $exists/$regex/$not become equality against their operand. UpdateDocuments/DeleteDocuments then hit the wrong rows with no error.

#### SH-M176 — SQL $ne against null emits `col <> NULL`, which is never true

`../Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs:162`  ·  _restates a first-pass finding_

ParseFilterToWhere maps $ne to `<>` and AddParameter turns null into DBNull.Value, so {"deletedAt":{"$ne":null}} renders `"deletedAt" <> @p0` bound to NULL — three-valued logic matches zero rows instead of IS NOT NULL. MongoDB handles $ne null natively and CosmosDB's `!= null` evaluates correctly, so the identical filter means different things per backend.

#### SH-M177 — SQL tracking table is ANSI-double-quoted on every provider, breaking MySQL

`../Birko.Data.Migrations.SQL/Settings/SqlMigrationSettings.cs:56`

SqlMigrationSettings.QuoteIdentifier hardcodes `"` and is never overridden (no provider-specific migration settings type exists); SqlMigrationStore's _quoteOpen/_quoteClose also default to `"` and SqlMigrationRunner:46 builds the store without provider quotes, though AbstractConnectorBase.QuoteIdentifier is available and MySQLConnector:89 uses backticks. On MySQL (ANSI_QUOTES off by default) `CREATE TABLE "__Migrations" ("Version" ...)` is a syntax error, so Initialize() cannot provision the table.

#### SH-M178 — Tracking-table DDL uses non-portable types (TIMESTAMP DEFAULT CURRENT_TIMESTAMP)

`../Birko.Data.Migrations.SQL/SqlMigrationStore.cs:266`

CreateMigrationsTable emits `CreatedAt TIMESTAMP NOT NULL` / `AppliedAt TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP` for every provider. On MSSql, TIMESTAMP is a synonym for rowversion — it cannot be NOT NULL with a CURRENT_TIMESTAMP default — so the CREATE TABLE fails and MSSql consumers cannot initialize the store, despite SqlMigrationRunner accepting any AbstractConnector. The DDL is only valid on SQLite/PostgreSQL.

#### SH-M179 — SqlSchemaBuilder sets the connector's external transaction and never clears it

`../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:116`

EnsureExternalTransaction (also called from SqlCollectionBuilder.Build:225 and SqlIndexBuilder.Build:319) calls _connector.SetExternalTransaction(connection, transaction), which stores connector-wide state (AbstractConnector.cs:85). Nothing resets it. The connector is the shared instance from the caller's store, so after the runner disposes its connection/transaction, every later connector command takes the RunCommandWithExternalTransaction path against a disposed connection — normal store CRUD then fails for the rest of the process.

#### SH-M180 — Fallback DDL literal formatting is culture-sensitive

`../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:162`

FormatValue falls through to `value.ToString()` for numerics, so a decimal/double DefaultValue renders as "1,5" under a comma-decimal culture (this repo's own machine is sk-SK), producing `DEFAULT 1,5` — a syntax error or a two-column parse. The DateTime branch (line 160) uses `:` in the custom format, which is the culture's TimeSeparator placeholder, so the timestamp literal also varies by culture. No CultureInfo.InvariantCulture anywhere.

#### SH-M181 — CollectionExists queries INFORMATION_SCHEMA.TABLES with no schema/catalog filter

`../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:53`

`SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName` matches any schema (MSSql/PostgreSQL) or any database on the server (MySQL, where INFORMATION_SCHEMA spans all schemas). A migration guarded by `if (!Schema.CollectionExists("Orders")) CreateCollection(...)` skips creation because an unrelated database has an Orders table, then records the migration as applied with no table created.

#### SH-M182 — Version-range selection means a back-dated pending migration is never applied

`../Birko.Data.Migrations/AbstractMigrationRunner.cs:239`

GetMigrationsToExecute selects `Version > fromVersion && Version <= toVersion` where fromVersion is Store.GetCurrentVersion() (the MAX applied version). With applied {1,5} and a newly added migration v3, GetPendingMigrations() (line 203, computed against the applied SET) lists v3, but Migrate() computes current=5, target=5 and returns the already-at-target success result — v3 never executes. The two selection rules in the same class disagree.

#### SH-M183 — Rollback executes Down for migrations that were never applied

`../Birko.Data.Migrations/AbstractMigrationRunner.cs:240`

The Down branch selects `Version <= fromVersion && Version > toVersion` and never consults Store.GetAppliedVersions(). With applied {1,5} and registered {1,3,5}, Rollback(0) runs Down for 5, 3 and 1 — v3's Down executes destructive DDL/deletes for changes its Up never made, and on backends with no batch atomicity (ES/Raven/Cosmos/Influx, and Mongo outside a replica set) the earlier steps stay committed.

#### SH-M184 — CosmosDB CountDocuments reads only the first page and can silently return 0

`../Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.cs:138`

It calls iterator.ReadNextAsync() exactly once (no `while (iterator.HasMoreResults)` loop, unlike every other method in the file) and returns response.FirstOrDefault(). A cross-partition `SELECT VALUE COUNT(1)` aggregate can return an empty first page while the query pipeline drains partitions, so the count silently comes back 0 and a migration guarded by `if (CountDocuments(...) == 0)` proceeds as though the container were empty.

#### SH-M185 — CosmosDB SQL literals use SQL-style '' escaping and culture-sensitive numbers

`../Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.cs:246`

FormatSqlValue escapes a quote by doubling it (`s.Replace("'", "''")`), but Cosmos DB SQL follows JSON string rules and escapes with a backslash — `'O''Brien'` parses as two adjacent literals and the query fails. Line 249 falls through to `value.ToString()` for the long/double produced by ExtractValue, so a non-invariant culture emits `c["x"] = 1,5`. Both paths feed the WHERE clause of DeleteDocuments/UpdateDocuments.

#### SH-M186 — CosmosDB migration state is read-modify-written with no ETag, losing version records

`../Birko.Data.Migrations.CosmosDB/CosmosMigrationStore.cs:136`

RecordMigration reads the state item, mutates the dictionary and calls ReplaceItemAsync with no ItemRequestOptions.IfMatchEtag. Two processes (or two pods at startup) recording different versions concurrently overwrite each other's document wholesale, so one applied version is lost and that migration is re-executed on the next Migrate(). RemoveMigration (line 166) has the same hole.

#### SH-M187 — RavenDB migration state is saved without optimistic concurrency, losing records

`../Birko.Data.Migrations.RavenDB/RavenMigrationStore.cs:120`

RecordMigration loads the single MigrationsStateDocument, sets one key and SaveChanges() without enabling session.Advanced.UseOptimisticConcurrency or passing a change vector (Raven's default is last-write-wins). Two concurrent recorders each write the document they loaded, so one version silently disappears from AppliedMigrations and that migration is re-run.

#### SH-M188 — InfluxDB RemoveMigration deletes over a window that excludes the recorded point

`../Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:176`

The delete range is [migration.CreatedAt - 1 minute, UtcNow], but AbstractMigration.CreatedAt is DateTime.UtcNow at object construction — in a later process (the normal rollback case) that is "now", while the point was written with the original run's CreatedAt, days earlier. The delete matches nothing, RemoveMigration reports success, and the version stays applied so Rollback can never progress past it (and re-running Rollback re-executes Down).

#### SH-M189 — InfluxDB version points are keyed by name tag, not version, so records collide

`../Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:146`

RecordMigration puts `name` in a tag and `version` in a field, so point identity is measurement+name+timestamp. Only Version must be unique across registered migrations (AbstractMigrationRunner:58 checks Version only), so two migrations sharing a Name and constructed in the same millisecond overwrite each other and one applied version is silently lost. Conversely RemoveMigration's predicate filters on `name` alone (line 174), deleting the records of every version sharing that name.

#### SH-M190 — InfluxDB RemoveMigration swallows InfluxException, so a failed delete reports success

`../Birko.Data.Migrations.InfluxDB/InfluxMigrationStore.cs:183`

The catch is justified as "already deleted", but an auth failure, missing bucket or server error is the same exception type. On rollback the runner treats the removal as done, so the version row survives while Down has already run — the database is downgraded but still reports the migration as applied, and the next Migrate() will not re-apply it.

#### SH-M191 — InfluxDB UpdateDocuments is a silent no-op that records the migration as applied

`../Birko.Data.Migrations.InfluxDB/Context/InfluxDBDataMigrator.cs:22`

The method body is only a comment. A data migration written against the neutral IDataMigrator contract (`context.Data.UpdateDocuments(...)`) performs no write, throws nothing, and InfluxMigrationRunner then records the version — so the migration is permanently marked applied with none of its effect. Every other backend either performs the update or throws (RavenDB.CopyData and ElasticSearch.CopyData use NotSupportedException for exactly this reason).

#### SH-M192 — InfluxDB CountDocuments ignores filterJson entirely

`../Birko.Data.Migrations.InfluxDB/Context/InfluxDBDataMigrator.cs:56`

The Flux query hardcodes `filter(fn: (r) => true)` and the filterJson parameter is never read, so CountDocuments("readings", "{\"host\":\"a\"}") returns the total record count of the bucket. A migration branching on that count ("only backfill if the filtered set is empty") takes the wrong branch with no error. DeleteDocuments in the same class throws NotSupportedException for a JSON filter rather than ignoring it, so the class is internally inconsistent.

#### SH-M193 — InfluxDB DeleteDocuments silently does nothing when the bucket is not found

`../Birko.Data.Migrations.InfluxDB/Context/InfluxDBDataMigrator.cs:38`

`if (bucket != null)` wraps the entire delete; a misspelled or not-yet-created collection name means the whole call is skipped with no exception, and the runner records the migration as applied. The bucket lookup is an exact-name FirstOrDefault over FindBucketsAsync(), so any lookup shortfall reads as "nothing to delete" — a destructive step reporting success without acting.

#### SH-M194 — InfluxDB CopyData copies Flux metadata columns and loses the original field names

`../Birko.Data.Migrations.InfluxDB/Context/InfluxDBDataMigrator.cs:108`

The loop over record.Values skips only _measurement/_time/_start/_stop, so `_field` (a string) is written as a TAG named "_field", `_value` becomes a field literally named "_value", and the pivot columns `result`/`table` are copied too. The target bucket ends up with points whose real field name is a tag value under a reserved key rather than a reconstruction of the source series. BulkInsert (line 151) correctly skips every key starting with "_"; CopyData does not.

#### SH-M195 — InfluxDB BulkInsert silently drops documents whose values are all strings

`../Birko.Data.Migrations.InfluxDB/Context/InfluxDBDataMigrator.cs:175`

ApplyValue maps every string to point.Tag(...) and only bool/integral/floating values to Field(...). A document of purely string values therefore produces a point with tags and no fields, which the InfluxDB client renders as no line protocol at all — the document is dropped with no exception and the migration is recorded as applied. Every other backend writes a string-only document intact.

#### SH-M196 — CopyData silently discards transformJson on SQL, CosmosDB and InfluxDB

`../Birko.Data.Migrations.SQL/Context/SqlDataMigrator.cs:86`

SqlDataMigrator.CopyData accepts transformJson and never reads it (emitting a plain INSERT INTO ... SELECT *); CosmosDBDataMigrator:142 and InfluxDBDataMigrator:83 do the same. MongoDataMigrator:56 honours it and ElasticSearch/RavenDB throw NotSupportedException. So the identical neutral call yields transformed data, an exception, or untransformed data presented as success depending on backend — the third case writes wrong data and records the migration as applied.

#### SH-M197 — ElasticSearch CountDocuments returns 0 on an invalid response

`../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchDataMigrator.cs:85`

`return response.Count;` is the only call in the file that skips EnsureValid. A missing index, auth failure or malformed query yields an invalid response whose Count is 0, so a migration guarded by `if (context.Data.CountDocuments(...) == 0)` proceeds as though the index were empty — e.g. re-seeding data that already exists.

#### SH-M198 — ElasticSearch by-query calls ignore VersionConflicts and per-document Failures

`../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchDataMigrator.cs:47`

EnsureValid only checks response.IsValid. An _update_by_query / _delete_by_query that completes with version conflicts or shard-level Failures returns a VALID response with a non-empty Failures list, so a partially-applied bulk mutation passes the check and the migration is recorded as applied. BulkInsert (line 132) explicitly inspects Errors/ItemsWithErrors, so the same file enforces the invariant on one path and not the others.

#### SH-M199 — ElasticSearch RenameField discards the _update_by_query response

`../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchSchemaBuilder.cs:72`

The UpdateByQuery result is never assigned or checked for IsValid, so a rejected script (missing index, mapping conflict, painless compile error) leaves every document unrenamed while the migration completes and its version is recorded. ElasticSearchDataMigrator.EnsureValid exists for exactly this reason but the schema builder does not use it. The field names are also interpolated raw into the Painless source, so a name containing a quote breaks or alters the script.

#### SH-M200 — CreateIndex(...).Build() creates nothing on ES, RavenDB, CosmosDB and InfluxDB

`../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchSchemaBuilder.cs:146`

IIndexBuilder.Build() is a default no-op interface method (Birko.Data.Patterns/Schema/IIndexBuilder.cs:19) and none of ElasticIndexBuilder, RavenIndexBuilder (RavenDBSchemaBuilder.cs:142), CosmosIndexBuilder (CosmosDBSchemaBuilder.cs:216) or InfluxIndexBuilder (InfluxDBSchemaBuilder.cs:131) overrides it. A migration that declares an index runs to completion, records its version, and creates nothing; the _fields list and Unique() flag go into an `internal bool IsUnique` nothing reads.

#### SH-M201 — Migrations index maps Version as keyword, so the descending sort is lexicographic

`../Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs:45`

`.Keyword(k => k.Name(n => n.Version))` maps the long Version as a string, so the `.Sort(sort => sort.Descending(f => f.Version))` at line 85 orders "9" above "10". The CR-M105 comment states the sort exists so a Size(10000) truncation keeps the NEWEST versions; with keyword ordering it keeps the lexicographically largest instead, so past 10 000 applied migrations GetCurrentVersion() can report a lower version and Migrate() re-runs the gap.

#### SH-M202 — A failed Indices.Exists probe is read as "index absent" in the ES store

`../Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs:73`

`_client.Indices.Exists(indexName).Exists` returns false both when the index is missing and when the request itself fails (auth, cluster unavailable), because Nest surfaces failures as an invalid response with Exists == false. GetAppliedVersions then returns an empty set (a second path to the same wrong answer as line 88), and Initialize() at line 35 attempts to create an index that already exists.

#### SH-M203 — MongoDB index builder discards Sparse(), WithProperty() and the field type

`../Birko.Data.Migrations.MongoDB/Context/MongoSchemaBuilder.cs:131`

Sparse() and WithProperty() are `=> this` and IndexFieldType is recorded but never used (Build at line 147 only sets Name and Unique, always Ascending/Descending). MongoDB natively supports sparse, TTL, partial and text/geo/hashed indexes, so `CreateIndex(...).WithField("email").Unique().Sparse().Build()` silently produces a DENSE unique index — which then rejects every second document missing that field with a duplicate-key error on null.

#### SH-M204 — Continuous-aggregate DDL hardcodes a bucket column literally named "time"

`../Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs:139`

BuildContinuousAggregateSql emits `time_bucket('{timeBucket}', time)` with no parameter for the time column, so CreateContinuousAggregate fails on any hypertable whose timestamp column is named recorded_at/ts/created_at. BuildCompressionPolicySql immediately above was fixed for exactly this class of hardcoding (CR-H070 made orderByColumn a parameter); the aggregate helper was left behind.

### area: repository-contract

#### SH-M205 — ReadOne extension also bypasses the store's lazy Init, so it can query an uncreated table

`../Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs:20`

Every store CRUD entry point runs EnsureInitialized() → InitCore(), which for SQL is Connector?.CreateTable(new[]{typeof(T)}) plus Connector?.DoInit() (DataBaseStore.cs:95-99). ReadOne never touches the store, so on a repository whose store has not been used yet the SELECT runs against a table that was never created (provider error, e.g. "no such table") and the OnInit hooks registered via the repository's own AddOnInit have not fired. Order-dependent: the same call succeeds if any other repository operation ran first.

#### SH-M206 — _modelHash is a plain Dictionary mutated with no synchronization on singleton repositories

`../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:24`

_modelHash is a plain Dictionary written from StoreHash (83), RemoveHash (96) and CheckHashChange (115) and cleared from the ReadMode setter (39) with no lock. AddRepositorySingleton and RepositoryLocator hand one repository instance to all callers, and LoadInstance → StoreHash runs on every read, so two concurrent reads can resize the Dictionary simultaneously — corrupted buckets / lost entries / spin, not an exception. _isReadMode (23) is a non-volatile bool read by every write guard. Identical in AbstractAsyncViewModelRepository.cs:26, where concurrency is the norm.

#### SH-M207 — _modelHash is never pruned: RemoveHash is dead code and Delete does not untrack

`../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:92`

RemoveHash has no call site anywhere in Birko.Data.ViewModel / Birko.Data.SQL.ViewModel / Birko.Data.Repositories (grepped), and Delete/DeleteAsync do not remove the deleted model's entry; the only pruning is the ReadMode=true setter. Every read adds a SHA-256 entry, including bulk reads (AbstractBulkViewModelRepository.cs:48, AbstractAsyncBulkViewModelRepository.cs:91 call LoadInstance per row), so paging a large table through a scoped or singleton repository hashes every row and retains one entry per Guid for the repository's lifetime, unbounded.

#### SH-M208 — DataBaseRepository accepts a non-bulk wrapper its own bulk methods then reject at runtime

`../Birko.Data.SQL.ViewModel/Repositories/DataBaseRepository.cs:29`

The constructor validates via IsStoreOfType, which inspects the INNERMOST store (StoreExtensions.cs:63-87), while every inherited bulk method tests the OUTERMOST reference (Store is not IBulkStore<TModel> — AbstractBulkViewModelRepository.cs:41/60/80/96/107/118/129). TenantStoreWrapper<TStore,T> implements only IStore<T>/IStoreWrapper<T> (TenantStoreWrapper.cs:12), so a repository wrapping a DataBaseBulkStore in it constructs fine and then throws ArgumentException from Read/Create/Update/Delete. The async sibling can't: its parameter is IAsyncBulkStore (AsyncDataBaseRepository.cs:54).

#### SH-M209 — Async bulk repositories swallow a wrong-store-type misconfiguration the sync ones throw on

`../Birko.Data.Repositories/AbstractAsyncBulkRepository.cs:46`  ·  _restates a first-pass finding_

BulkStore is `Store as IAsyncBulkStore<T>` (24); when the cast fails every method returns early — ReadAsync yields Array.Empty<T>(), ReadFirstAsync null, and CreateAsync/UpdateAsync/DeleteAsync including the filter overloads (87/94/112) complete successfully having done nothing. AbstractBulkRepository throws InvalidOperationException for the identical state. A bulk delete-by-filter that silently deletes nothing is indistinguishable from one that matched no rows. Same in AbstractAsyncBulkViewModelRepository.cs:58/105/120/130/140/150.

#### SH-M210 — RepositoryLocator default keys don't match between create and destroy — cached repositories leak

`../Birko.Data.Repositories/RepositoryLocator.cs:158`  ·  _restates a first-pass finding_

GetRepository<TStore,TRepository>(store) defaults its key to typeof(TStore).FullName (34) but Destroy<TRepository>() defaults to typeof(TRepository).FullName (158). A repository created through the common no-key overload can never be found by the no-key Destroy: TryGetValue misses, the method returns silently, and the repository plus its store/connection stays in the static cache for the process lifetime.

#### SH-M211 — GetRepository<TStore,TRepository> silently discards the store argument on a cache hit

`../Birko.Data.Repositories/RepositoryLocator.cs:44`  ·  _restates a first-pass finding_

The cache key is the store TYPE, not the instance, and the store parameter is consulted only inside the `if (!ContainsKey(type))` miss branch. GetRepository<MyStore,MyRepo>(storeB) after GetRepository<MyStore,MyRepo>(storeA) returns the repository built around storeA with no signal that storeB was ignored, so two differently configured stores of the same type (different tenant, connection or settings) silently collapse onto whichever was seen first.

#### SH-M212 — Destroy<TRepoA>(key) destroys the store shared by every repository cached under that key

`../Birko.Data.Repositories/RepositoryLocator.cs:172`

The cache is key → (repository type → instance), so GetRepository<MyStore,RepoA>(store) and GetRepository<MyStore,RepoB>(store) share one key and one store instance. Destroy<RepoA>(key) removes only RepoA's entry and then calls repository.Destroy() (172), which is Store?.Destroy() (AbstractRepository.cs:119) — for SQL that is Connector.DropTable (DataBaseStore.cs:104). RepoB remains cached pointing at a destroyed store and its next call fails or no-ops with no indication why.

#### SH-M213 — Destroy() is documented as releasing resources but DROPs the SQL table

`../Birko.Data.Repositories/IBaseRepository.cs:27`

IBaseRepository.Destroy is documented "Destroys the repository and releases all resources" and AbstractRepository.Destroy (117) / AbstractViewModelRepository.Destroy forward to Store?.Destroy(). For the SQL family that is DataBaseStore.Destroy → Connector?.DropTable(new[]{typeof(T)}) (DataBaseStore.cs:102-105). A consumer calling repository.Destroy() from a Dispose/teardown path — or RepositoryLocator.Destroy<TRepository>(), whose doc frames it as cache eviction — drops the entity's table and all its rows. No repository-layer doc says the call destroys data.

#### SH-M214 — Bulk ViewModel paging cannot express an order, so limit/offset pages are unstable

`../Birko.Data.ViewModel/Repositories/AbstractBulkViewModelRepository.cs:46`

Read hard-codes null for the store's orderBy — Read(filter?.Filter(), null, limit, offset) — and IBulkViewModelReadRepository.Read (IBulkViewModelRepository.cs:26) has no orderBy parameter, so callers cannot supply one. SQL/ES/Mongo return unordered rows for an unordered query, so successive pages (offset 0 then 20) can repeat and skip rows. The plain model bulk repository can pass Stores.OrderBy<T> (IBulkRepository.cs:31) and the ReadOne extension can pass orderByExpr, so only the ViewModel bulk family cannot. Same at AbstractAsyncBulkViewModelRepository.cs:85.

#### SH-M215 — AbstractBulkViewModelRepository.Read defers its store-type validation to enumeration

`../Birko.Data.ViewModel/Repositories/AbstractBulkViewModelRepository.cs:41`  ·  _restates a first-pass finding_

Read is a `yield return` iterator, so the `Store is not IBulkStore<TModel>` guard and its ArgumentException do not execute when Read is called — they execute on the first MoveNext(). A caller that builds the sequence in one place and enumerates it elsewhere gets the exception from an unexpected frame, and a caller that never enumerates never learns the store is wrong.

#### SH-M216 — ViewModel Delete has no null-argument guard while Create and Update do

`../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:232`  ·  _restates a first-pass finding_

Create (190) and Update (213) return early on data == null; Delete checks only Store, then calls LoadModelInstance(data) → MapToModel(null, target), so any MapToModel reading source members throws NullReferenceException instead of the siblings' no-op. Same asymmetry in AbstractAsyncViewModelRepository.DeleteAsync:241 and in sync bulk Create(IEnumerable):57, Update(IEnumerable):77, Delete(IEnumerable):126, which reach data.Select(...) unguarded while every async bulk twin checks data == null (AbstractAsyncBulkViewModelRepository.cs:58/105/150).

#### SH-M217 — Update signals "skip" by returning null! from a StoreDataDelegate<T> declared to return T

`../Birko.Data.ViewModel/Repositories/AbstractViewModelRepository.cs:226`  ·  _restates a first-pass finding_

When CheckHashChange reports no change the delegate returns null! through a non-nullable T, relying on every store to null-check a result none of them even read; a store that did dereference it would throw NullReferenceException. data.LoadFrom(item) on the next line runs unconditionally, so the caller's ViewModel is refreshed from a model whose persistence was nominally skipped. CheckHashChange also defaults update:true, advancing the stored hash before the store persists. Same shape in AbstractAsyncViewModelRepository.cs:232.

#### SH-M218 — AddRepository overloads capture one shared store instance regardless of requested lifetime

`../Birko.Data.Repositories/ServiceCollectionExtensions.cs:49`  ·  _restates a first-pass finding_

AddRepository<TRepository>(IBaseStore store, lifetime) closes over the single instance passed at registration, so even a Scoped or Transient repository gets the same store on every resolution — defeating per-scope isolation for stateful stores (open connection, change tracking, tenant scoping). All three overloads use services.Add rather than a TryAdd variant, so repeated registration silently duplicates descriptors, and the store passed to this overload is never registered in the container, so nothing ever disposes it or calls its Destroy().

#### SH-M219 — A null store makes every repository write a silent no-op that reports success

`../Birko.Data.Repositories/AbstractRepository.cs:55`

With Store == null, Create returns Guid.Empty (55), Save returns Guid.Empty (97), Update/Delete/Destroy are Store?.X(...) no-ops (66/76/119) and Count returns 0 (86); the async family is identical (AbstractAsyncRepository.cs:59/70/81/103/125). Guid.Empty is exactly what a caller sees for "nothing persisted", so a mis-wired repository — e.g. DataBaseRepository constructed with an explicit null, which does NOT self-provision a store — accepts and discards every write while reporting success.

#### SH-M220 — Async single-item DeleteAsync bypasses the LoadModelInstance extension point

`../Birko.Data.ViewModel/Repositories/AbstractAsyncViewModelRepository.cs:242`

DeleteAsync inlines `TModel item = CreateModelInstance(); MapToModel(data, item);` instead of calling the public virtual LoadModelInstance(data) that Create/Update and every bulk path use. A subclass overriding LoadModelInstance (the documented ViewModel→Model hook, e.g. to set a discriminator or the key needed to locate the row) has its override skipped on exactly that one path, so the model handed to Store.DeleteAsync differs from the one used everywhere else. The sync Delete does call LoadModelInstance (AbstractViewModelRepository.cs:242).

### area: schema-index-and-ddl

#### SH-M221 — CharField reads a string into a `char` property, so any `char` column throws at materialisation

`../Birko.Data.SQL/SQL/Fields/CharField.cs:26`  ·  _restates a first-pass finding_

CreateAbstractField maps typeof(char) to `new CharField(property, name, primary, unique, 1)` (AbstractField.cs:199), but CharField.Read does `Property.SetValue(value, reader.GetString(index))`. Assigning a string to a char property fails inside reflection. CharField only round-trips string properties; the one CLR type it is named for cannot be read back.

#### SH-M222 — DataBase.LoadTable returns null through a non-nullable return type for any unmapped type

`../Birko.Data.SQL/SQL/DataBase_Table.cs:68`  ·  _restates a first-pass finding_

ComputeTable returns null when the type has neither a table attribute nor a RegisterTableName override (or when its Fields come out empty); LoadTable then does `return table!`, laundering the null past nullable analysis. Callers that do not null-check (unlike GetPrimaryFields, which does) dereference it, so a model missing [Table] fails with an opaque NullReferenceException instead of a TableAttributeException. The null is also not cached, so every call re-reflects.

#### SH-M223 — [IndexedField] on an unmapped property emits an index over a column that does not exist

`../Birko.Data.SQL/SQL/DataBase_Table.cs:191`  ·  _restates a first-pass finding_

`var columnName = field?.Name ?? prop.Name;` — when no field matched (property carries [IgnoreField], or has an unsupported CLR type such as `long` and was skipped by CreateAbstractField), the index silently names the property. CREATE INDEX then fails at DDL time far from the declaration. The [CompositeIndex] path (line 250) throws TableAttributeException for exactly this case, so the inconsistency looks unintentional.

#### SH-M224 — Composite index column order is nondeterministic when Order values tie

`../Birko.Data.SQL/SQL/DataBase_Table.cs:266`  ·  _restates a first-pass finding_

`idx.Columns.Sort((a, b) => a.Order.CompareTo(b.Order))` uses List<T>.Sort, an unstable introsort. [IndexedField(name)] defaults Order to 0, so the attribute on two properties yields two columns with Order 0 whose emitted order is unspecified — and column order determines which queries the index can serve. OrderBy would at least pin it to reflection order.

#### SH-M225 — SqLiteIndexManager.ExistsAsync ignores the table name, so an index on any table reports as existing

`../Birko.Data.SQL.SqLite/IndexManagement/SqLiteIndexManager.cs:24`  ·  _restates a first-pass finding_

The query is `SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = '<index>'` — the tableName parameter is accepted and never used, unlike ListAsync (line 48) which does filter on tbl_name. A migration guarding CREATE INDEX with ExistsAsync will skip creating an index on table B because a same-named index exists on table A.

#### SH-M226 — SQLite infers index uniqueness from `sql LIKE '%UNIQUE%'`

`../Birko.Data.SQL.SqLite/IndexManagement/SqLiteIndexManager.cs:48`  ·  _restates a first-pass finding_

Any index whose stored DDL text contains the substring UNIQUE — including inside a column or index identifier such as IX_UNIQUE_CODE — is reported Unique == true by both ListAsync and the (dead) ListIndexesSql. PRAGMA index_list exposes a real `unique` column and would be exact. A drift check comparing desired against actual uniqueness gets the wrong answer.

#### SH-M227 — MySQL/PostgreSQL index existence and listing queries are not schema-qualified

`../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs:157`  ·  _restates a first-pass finding_

IndexExistsSql filters information_schema.statistics only by table_name and index_name (PostgreSqlIndexManager.cs:21 likewise filters pg_indexes only by tablename/indexname, and its ListIndexesSql filters only t.relname). With several schemas reachable from the connection, an index on a same-named table in another schema satisfies the count and ExistsAsync returns a false positive. ListIndexesSql has the same gap and no primary-key filter, so MySQL reports PRIMARY while the others exclude it.

#### SH-M228 — IndexManager.UpdateMapping validates indexName then never uses it

`../Birko.Data.ElasticSearch/IndexManagement/IndexManager.cs:304`  ·  _restates a first-pass finding_

`_client.Map(mappingDescriptor)` / `MapAsync` is called with no `.Index(indexName)`, so the mapping lands on whatever index the caller's descriptor or the ElasticClient default resolves — not the one named in the argument. The parameter only ever reaches the failure message, which makes a mapping applied to the wrong index look like a success.

#### SH-M229 — CREATE UNIQUE INDEX IF NOT EXISTS is invalid on MySQL and MySqlIndexManager does not override it

`../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs:181`

The base CreateUniqueIndexSql emits `CREATE UNIQUE INDEX IF NOT EXISTS ...`. MySQL has no IF NOT EXISTS clause for CREATE INDEX. MSSqlIndexManager and PostgreSqlIndexManager override this method; MySqlIndexManager.cs:17 declares only a constructor and comments that the base implementation 'is already correct'. Every CreateAsync with definition.Unique == true against MySQL therefore fails with a parse error wrapped in IndexManagementException — the one flag SqlIndexManager branches on is unusable on that provider.

#### SH-M230 — DropAsync emits `DROP INDEX IF EXISTS <name>` with no ON clause — invalid on MySQL

`../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs:84`

DropAsync builds a name-only Tables.IndexDefinition and delegates to Connector.DropIndexSql. The base implementation (AbstractConnectorBase.cs:352) returns `DROP INDEX IF EXISTS "<name>"` — no table. MySQL requires `DROP INDEX <name> ON <table>` and rejects IF EXISTS; MSSqlConnector overrides DropIndexSql to add `ON <table>`, MySQLConnector does not override it at all. So DropAsync (and DropIndexes) can never drop an index on MySQL; every call fails, wrapped as IndexManagementException.

#### SH-M231 — MSSQL ListAsync reads the tinyint key_ordinal with GetInt32, which SqlClient rejects

`../Birko.Data.SQL.MSSql/IndexManagement/MSSqlIndexManager.cs:32`

ListIndexesSql selects `ic.key_ordinal AS ordinal_position`; sys.index_columns.key_ordinal is tinyint. The shared reader loop at SqlIndexManager.cs:115 calls `reader.GetInt32(4)`, and Microsoft.Data.SqlClient's GetInt32 performs no widening — a tinyint column raises InvalidCastException. ListAsync (and GetInfoAsync, which delegates to it) therefore throws for any MSSQL table with at least one non-primary index, and since inspection paths are deliberately unwrapped the caller sees a raw cast error.

#### SH-M232 — PostgreSQL descending detection subscripts indoption by table attnum instead of index position

`../Birko.Data.SQL.PostgreSQL/IndexManagement/PostgreSqlIndexManager.cs:30`

`pg_index.indoption[a.attnum - 1] & 1 = 1` indexes indoption by the column's position in the *table*, but indoption is a per-index vector ordered by position in the *index*. For a descending index on the table's 5th column, indoption has one element and indoption[4] is NULL, so the CASE yields 0 and IsDescending reports false; for multi-column indexes the flags are read off the wrong slots and can report DESC for an ascending column. PostgreSQL is one of only two dialects claimed to report real direction.

#### SH-M233 — ES existence checks ignore response validity, so a failed request answers 'does not exist'

`../Birko.Data.ElasticSearch/IndexManagement/IndexManager.cs:44`

IndexExists/IndexExistsAsync return `response.Exists` without ValidateResponse. On a transport failure, auth rejection or unreachable node, Exists is false. So DeleteIndexAsync (line 144) short-circuits and returns as though the index were deleted — a destructive op reporting success having done nothing; CreateIndexAsync (line 76) skips its already-exists guard; and ElasticSearchIndexManagerAdapter.ExistsAsync surfaces the same false to IIndexManager callers guarding migrations.

#### SH-M234 — Reindex with waitForCompletion:false reports Success and 0 documents while the copy still runs

`../Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:69`

With waitForCompletion=false ES returns immediately with a task handle and response.Created == 0; IsValid is true, so the method refreshes the target and returns ReindexResult.Successful(source, target, 0, elapsed). The caller cannot distinguish 'copied nothing' from 'still copying' and gets no task id to poll, so any follow-up step (alias swap, dropping the source) runs against a partially populated index.

#### SH-M235 — ReindexWithAlias reports Failed after a successful alias swap when the cleanup step throws

`../Birko.Data.ElasticSearch/IndexManagement/ReindexHelper.cs:264`

Steps 3 and 4 (SwapAlias, then DeleteIndex(oldIndexName) when deleteOldIndex is true) run inside the same try. If the delete throws — old index closed, in-flight snapshot, permissions — the catch returns ReindexResult.Failed even though the alias already points at the new index and the migration is complete. A caller that retries on failure then dies at step 1 with "Index '<new>' already exists."

#### SH-M236 — Adapter ListAsync returns an empty list when the _cat request fails

`../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs:111`

`if (!catResponse.IsValid) return Array.Empty<IndexInfo>();` — a rejected, unauthorised or timed-out _cat/indices call is indistinguishable from a cluster with no indexes, with no exception and no diagnostic. SqlIndexManager.ListAsync lets provider failures propagate, so the same IIndexManager contract fails loudly on SQL and silently on ES; a reconciliation loop reading ES would conclude every index is missing and try to recreate them.

#### SH-M237 — Adapter GetInfoAsync collapses every InvalidOperationException to null

`../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs:155`

The catch is untyped by cause: IndexManager.ValidateResponse throws InvalidOperationException for a missing index, a cluster error, a bad request and an auth failure alike, so all of them return null. The caller reads 'index absent' and may create or recreate an index that in fact exists, and no context (DebugInformation, inner exception) survives.

#### SH-M238 — ES adapter CreateAsync silently drops Unique/Sparse/ExpireAfter/Fields

`../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs:51`

Only Properties["NumberOfShards"]/["NumberOfReplicas"] are read, and only when boxed as int (a "3" string is ignored with no error). definition.Fields, Unique, Sparse and ExpireAfter are never inspected. SqlIndexManager.CreateAsync branches on Unique and rejects an empty Fields list, so the same IIndexManager call that creates an enforced unique constraint on SQL creates an unconstrained container on ES and reports success — a divergence a portable migration cannot detect at runtime.

#### SH-M239 — BuildIndexInfo keys stats/settings by the requested name, so querying via an alias yields all zeros

`../Birko.Data.ElasticSearch/IndexManagement/IndexManager.cs:271`

`statsResponse.Indices.TryGetValue(indexName, ...)` and the settings equivalent look up the argument string, but ES keys both responses by the concrete index name. Call GetIndexInfoAsync("invoices") where `invoices` is an alias for `invoices_v3` and both TryGetValue calls miss: DocumentCount, SizeInBytes, NumberOfShards and NumberOfReplicas stay 0 and RefreshInterval stays null, with IsValid true and no exception. The adapter republishes those zeros as portable IndexInfo.

#### SH-M240 — Get-only computed properties become columns and break row materialisation

`../Birko.Data.SQL/SQL/DataBase_Field.cs:22`

LoadFields enumerates via DataBase.GetProperties (DataBase.cs:80), which accepts a property when any public accessor is non-static — a getter alone qualifies. So `public string Display => First + " " + Last;` on a [Table] model is mapped to a StringField, appears in CREATE TABLE and in the SELECT list, and then AbstractField.Read calls Property.SetValue on a property with no setter, throwing ArgumentException for every row read. Only [IgnoreField]/[NotMapped] avoid it, and nothing warns.

#### SH-M241 — IntegerField.Write unboxes any enum to int, throwing for non-int-backed enums

`../Birko.Data.SQL/SQL/Fields/IntegerField.cs:32`

CreateAbstractField (AbstractField.cs:227) maps every enum — whatever its underlying type — to IntegerField/NullableIntegerField. Write then does `return (int)val;` on the boxed value. The CLR permits unboxing a boxed enum only to its exact underlying type, so `enum Status : byte` or `: long` raises InvalidCastException on every INSERT/UPDATE of that entity. Read is safe (Enum.ToObject), so the failure is write-only and provider-independent.

#### SH-M242 — Base-class [IndexedField] yields one global index name per derived table; IF NOT EXISTS skips all but the first

`../Birko.Data.SQL/SQL/DataBase_Table.cs:144`

LoadIndexes scans `type.GetProperties(Public|Instance)`, which includes inherited properties, and [IndexedField] is Inherited = true — so `[IndexedField("IX_Tenant")]` on a shared base property produces an index literally named IX_Tenant in every derived table. AbstractConnector_Create.CreateIndexes feeds each to CreateIndexSql, which emits `CREATE INDEX IF NOT EXISTS`. Index names are schema-global on PostgreSQL/SQLite/MSSQL, so the first table wins and later tables silently ship without the index their model declares.

#### SH-M243 — Table.Fields keys and Table.Indexes columns freeze at load and go stale when a mapping renames a column

`../Birko.Data.SQL/SQL/Tables/Table.cs:59`

ComputeTable keys Fields by the load-time column name and resolves index column names in the same pass (DataBase_Table.cs:104/112). ModelMapRegistry.ApplyToDatabase (Birko.Models.SQL/Mapping/ModelMapRegistry.cs:112) later mutates `sqlField.Name` in place for HasColumnName. After that GetField("<mapped column>") returns null while GetField("<original>") returns a field whose Name is the mapped one, and Table.Indexes still names the pre-rename column so CREATE INDEX references a column the table lacks.

#### SH-M244 — MySQL and SQLite listing hard-code IsDescending=false, so direction never round-trips

`../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs:167`

The default (MySQL) ListIndexesSql selects the literal `0 AS is_descending` and SqLiteIndexManager.ListAsync sets `IsDescending = false` unconditionally (SqLiteIndexManager.cs:72), while CreateAsync/CreateUniqueIndexSql do emit DESC from IndexField.IsDescending. Code that lists the actual index and compares it against the desired IndexDefinition — the natural idempotency check in a migration — sees a permanent mismatch on those two providers and drops/recreates on every run, while being stable on MSSQL and PostgreSQL.

#### SH-M245 — An unterminated builder chain performs no DDL and the migration reports success

`../Birko.Data.Patterns/Schema/IIndexBuilder.cs:19`

Build() is a default interface method with an empty body on both IIndexBuilder and ICollectionBuilder (ICollectionBuilder.cs:21). A provider that defers (the doc names SQL, which emits one CREATE TABLE/CREATE INDEX) only acts inside its Build() override, so `CreateIndex("Invoice", "IX_Number").WithField("Number")` without the terminal call emits nothing, returns normally, and the migration is recorded as applied — a schema change permanently missed with no error. A non-defaulted Build() would have made the omission a compile error.

### area: security-and-authorization

#### SH-M246 — Pbkdf2 Verify trusts the stored iteration segment unbounded — throws for 0/negative, hangs for int.MaxValue

`../Birko.Security/Hashing/Pbkdf2PasswordHasher.cs:50`

int.TryParse at line 50 accepts 0 and negatives, and the value is handed straight to Rfc2898DeriveBytes.Pbkdf2 at line 68. Verified: iterations 0 and -5 both throw ArgumentOutOfRangeException("iterations must be a non-negative and non-zero value"), which escapes Verify — contradicting the CR-M233 contract in the comment at line 53 that Verify be total over arbitrary stored strings. A stored 2147483647 instead burns CPU for hours per login attempt. The FormatException path is caught; this one is not.

#### SH-M247 — BCrypt Verify throws ArgumentException for a shaped hash whose cost is unparseable or out of range

`../Birko.Security.BCrypt/Hashing/BCryptPasswordHasher.cs:277`

IsValidBCryptHash only checks that indices 7..59 are in the BCrypt-Base64 alphabet — digits and letters both are — so "$2a$99$<53 valid chars>" and "$2a$ab$…" pass it. HashPassword then throws ArgumentException("Invalid BCrypt cost factor") out of Verify (line 69) instead of returning false. NeedsRehash on the identical input returns true without throwing, and the PBKDF2 sibling catches its analogous FormatException, so the two hashers and the two BCrypt entry points disagree on a corrupt stored value.

#### SH-M248 — JwtTokenProvider.ValidateToken rejects any token carrying two claims of the same type

`../Birko.Security.Jwt/JwtTokenProvider.cs:102`

`principal.Claims.ToDictionary(c => c.Type, c => c.Value)` throws ArgumentException on a duplicate key, caught by the general handler at line 114 and reported as "Unexpected error: …". Repeated claims are normal JWT (an `aud` array, multiple `role`/`permission` claims — a shape ClaimsCurrentUser explicitly documents supporting at line 54), and JwtSecurityTokenHandler's inbound mapping also collapses "sub" and ClaimTypes.NameIdentifier onto one type. Such a token is reported invalid rather than validated.

#### SH-M249 — IP-bound tokens are bypassable: GetClientIpAddress trusts X-Forwarded-For with no trusted-proxy check

`../Birko.Security/Authentication/AuthenticationService.cs:154`  ·  _restates a first-pass finding_

GetClientIpAddress returns the first entry of a caller-supplied X-Forwarded-For, then X-Real-IP, then CF-Connecting-IP, before falling back to the real socket IP — with no list of trusted proxies and no verification that the app is behind one. The value it returns is exactly what ValidateToken compares against binding.AllowedIps (line 105), so a client holding the token can send `X-Forwarded-For: 10.0.0.1` and satisfy an allow-list it is not on, defeating the only purpose TokenBinding has.

#### SH-M250 — Binding match short-circuits the plain-token fallback; an empty AllowedIps rejects every request

`../Birko.Security/Authentication/AuthenticationService.cs:96`  ·  _restates a first-pass finding_

The loop returns from inside the `binding.Token == token` branch, so a token listed in both TokenBindings (with a non-matching IP) and Tokens is rejected outright — the `_expandedTokens` fallback at line 120 is unreachable. A binding whose AllowedIps expanded to empty rejects every IP while still making IsAuthenticationEnabled() true, indistinguishable at the call site from a wrong token.

#### SH-M251 — Vault request paths interpolate the caller's key unescaped — '../' traversal and query injection, incl. delete

`../Birko.Security.Vault/VaultSecretProvider.cs:62`

`new Uri(_baseUri, relativePath)` with relativePath built by raw interpolation (BuildDataPath line 252, DeleteSecretAsync line 136, ListSecretsAsync line 151). Uri resolution normalises `..`, so key = "../../sys/policy/x" retargets the request at a different Vault endpoint, and a key containing '?' or '#' rewrites the query (ListSecretsAsync appends "?list=true" after the interpolated path). DeleteSecretAsync is a destructive path reachable this way. No Uri.EscapeDataString anywhere.

#### SH-M252 — Azure Key Vault request paths interpolate the caller's key unescaped, including on DeleteSecretAsync

`../Birko.Security.AzureKeyVault/AzureKeyVaultSecretProvider.cs:75`

`$"{BaseUri}secrets/{key}?api-version={ApiVersion}"` at lines 75, 96 and 109 embeds the key with no escaping. A key containing '/' silently becomes the *version* segment of the REST route (secrets/{name}/{version}), '..' collapses to a different route, and '?' or '&' injects query parameters ahead of api-version. Line 109 is a delete. Same species as the Vault provider, so neither backend escapes.

#### SH-M253 — OidcIdTokenVerifier looks the provider up by the raw name, not the canonical one, so " Google " is refused

`../Birko.Security.Jwt/OpenIdConnect/OidcIdTokenVerifier.cs:95`

`_providers.TryGetValue(provider, …)` runs at line 95 while Canonical(provider) (trim + lower-case) is only computed at line 118, after the lookup. The re-keyed dictionary is OrdinalIgnoreCase but not trim-insensitive, so a provider name with surrounding whitespace — from a config file or a form post — returns ProviderNotConfigured even though it is configured. Canonical IS used for the key-source cache key and the returned Identity, so the code is inconsistent with itself.

#### SH-M254 — Recursive secret→config traversal assumes relative child names; Azure Key Vault returns absolute ones

`../Birko.Security.Vault.Configuration/SecretConfigurationProvider.cs:89`

`childPath = path + "/" + childName` treats ListSecretsAsync's output as names relative to `path`. Vault returns KV folder children (relative), but AzureKeyVaultSecretProvider.ListSecretsAsync (line 120) enumerates the whole vault and filters by StartsWith, returning FULL secret names — so with path "app" the recursion queries "app/app-db", which resolves to a nonexistent version route, yields null, and is swallowed by the fail-open catch at line 66. Each level also re-enumerates the entire vault.

#### SH-M255 — UseResolvedPermissions is order-dependent and silently reverts to claim-borne permissions

`../Birko.Security.AspNetCore/Extensions/PermissionResolutionExtensions.cs:27`

It removes only the FIRST ICurrentUser descriptor and then appends ResolvedPermissionsCurrentUser. Since DI resolves the last registration, calling UseResolvedPermissions() before AddBirkoSecurity leaves ClaimsCurrentUser registered afterwards and winning — the app silently keeps reading permissions out of the token instead of resolving them server-side, which is the wider grant of the two. No ordering guard, no throw, and nothing observable at startup.

#### SH-M256 — JWT bearer accepts ?token= on every request, putting the credential in URLs, logs and history

`../Birko.Security.AspNetCore/Authentication/JwtBearerExtensions.cs:74`

OnMessageReceived copies Request.Query["token"] into context.Token whenever present, for any path and any method — the comment names SSE/WebSocket but nothing restricts it to those. Query strings are written to reverse-proxy and Kestrel access logs, browser history and Referer headers, so a bearer token good for ExpirationMinutes leaks wherever the URL is recorded. A path predicate would keep the SSE affordance without applying it to every endpoint.

#### SH-M257 — JwtTokenProvider.ValidateToken skips issuer/audience validation when they are unconfigured

`../Birko.Security.Jwt/JwtTokenProvider.cs:93`  ·  _restates a first-pass finding_

ValidateIssuer = !IsNullOrEmpty(opts.Issuer) and the same for audience, so a TokenOptions carrying only a Secret accepts any correctly signed token whatever iss/aud it names — including one minted by a sibling service that shares the secret. AddBirkoJwtBearer (JwtBearerExtensions.cs:58) always validates both, so the same token is judged differently depending on which entry point sees it.

#### SH-M258 — VaultSecretProvider.ParseKv1Response throws KeyNotFoundException on a malformed 200 body

`../Birko.Security.Vault/VaultSecretProvider.cs:292`  ·  _restates a first-pass finding_

`root.GetProperty("data")` is unguarded, so a KV v1 200 response lacking `data` raises KeyNotFoundException out of GetSecretWithMetadataAsync. The KV v2 sibling was explicitly hardened for exactly this case (CR-M240, lines 258-261) to return an empty value, so the two engine versions behave differently on the same malformed input.

#### SH-M259 — HttpOidcSigningKeySource serves stale signing keys indefinitely while JWKS is unreachable

`../Birko.Security.Jwt/OpenIdConnect/HttpOidcSigningKeySource.cs:84`  ·  _restates a first-pass finding_

On any non-cancellation exception the cached entry is returned unchanged and FetchedAt is never advanced or bounded, so a provider that revokes a compromised key keeps having tokens signed by it accepted for as long as the endpoint is down. The tradeoff is documented at line 82 but there is no maximum stale age and no staleness signal to the verifier, which cannot distinguish fresh keys from week-old ones.

#### SH-M260 — AzureKeyVault ListSecretsAsync enumerates the whole vault and never follows nextLink

`../Birko.Security.AzureKeyVault/AzureKeyVaultSecretProvider.cs:123`  ·  _restates a first-pass finding_

The request is always `secrets?api-version=7.4` with `path` applied only as a client-side StartsWith filter at line 145, and the `nextLink` Key Vault returns for large vaults is never followed. On a vault with more secrets than one page, secrets beyond page 1 are silently missing from both the list and SecretConfigurationProvider's traversal, and each configuration source costs a full-vault enumeration.

### area: serialization

#### SH-M261 — Caller-supplied XmlReaderSettings silently discard the DTD/XXE hardening

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:31`  ·  _restates a first-pass finding_

`DtdProcessing = Prohibit` and `XmlResolver = null` exist only inside the `readerSettings ?? new XmlReaderSettings{…}` fallback. Any caller who supplies reader settings — including the common `new SystemXmlSerializer(new XmlWriterSettings{Indent=true}, new XmlReaderSettings())` when they only wanted indented output — gets DTD-capable defaults with the default resolver on all four read paths (lines 68, 77, 109, 118, 147, 155, 189, 199), re-enabling external-entity resolution and billion-laughs expansion on untrusted XML. Nothing re-applies it and nothing warns.

#### SH-M262 — MessagePack default pairs a contractless resolver with the trusted-data profile

`../Birko.Serialization.MessagePack/MessagePackBinarySerializer.cs:23`  ·  _restates a first-pass finding_

`_options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance)`; there is no `.WithSecurity(MessagePackSecurity.UntrustedData)` in the file. Standard is the trusted profile (no hash-collision mitigation, no depth limit) and contractless binds arbitrary incoming maps to members by name. Every read member (50, 57, 76, 82, 105, 111, 134, 140) uses it, and this is the intended queue/job payload codec, i.e. it is fed externally produced bytes. Supplying `options` to add security also discards the resolver choice.

#### SH-M263 — Newtonsoft stream writes emit a UTF-8 BOM its byte path neither writes nor strips

`../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs:87`  ·  _restates a first-pass finding_

Stream writes (87, 98, 131, 144) use `new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen:true)`; static `Encoding.UTF8` emits `EF BB BF` at position 0. `SerializeToBytes` (60) uses `Encoding.UTF8.GetBytes` (no preamble) and `DeserializeFromBytes` (72, 79) uses `Encoding.UTF8.GetString`, which does not strip a BOM. Bytes captured from `Serialize(stream, v)` then passed to `DeserializeFromBytes<T>` yield a leading U+FEFF and a parse failure, while `Deserialize<T>(stream)` accepts them (`detectEncodingFromByteOrderMarks: true`).

#### SH-M264 — YamlDotNet has the identical BOM asymmetry between its stream and byte paths

`../Birko.Serialization.Yaml/YamlDotNetSerializer.cs:97`  ·  _restates a first-pass finding_

`Serialize(Stream,…)` (97) and `Serialize<T>(Stream,…)` (106) write through `new StreamWriter(stream, Encoding.UTF8, bufferSize:1024, leaveOpen:true)` → BOM at position 0, while `SerializeToBytes` (70, 76) uses `Encoding.UTF8.GetBytes` and `DeserializeFromBytes` (82, 89) uses `Encoding.UTF8.GetString` with no BOM strip. A document round-tripped stream→bytes carries U+FEFF into the parser. Same species as the Newtonsoft defect, fixable the same way (`new UTF8Encoding(false)`, as SystemXmlSerializer already does).

#### SH-M265 — Generic write overloads honour typeof(T) in three impls and erase it in two

`../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs:41`

`Serialize<TBase>(derived)` resolves five ways. STJ (SystemJsonSerializer.cs:43) and MessagePack (:69) serialize the declared type, silently dropping members declared only on the runtime type; XML (:55 `new XmlSerializer(typeof(T))`) throws for an unregistered derived instance; Newtonsoft (:41, whose only matching overload takes `object?`) and YAML (:51 `Serialize(value!)`) emit the derived shape. Overload resolution always prefers `Serialize<T>` over `Serialize(object)`, so this is the path consumers hit: persisting a base-typed reference loses fields under two impls, throws under a third.

#### SH-M266 — An empty payload deserializes five different ways, three of them silently

`../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs:54`

`Deserialize<T>("")` / `DeserializeFromBytes<T>(Array.Empty<byte>())` guard only for null (51-55, 76-81), so an empty payload reaches the library. `JsonConvert.DeserializeObject<T>` returns null for empty/whitespace input, so the caller gets `default(T)` and cannot tell an empty payload from a JSON `null` from a truncated read. STJ (SystemJsonSerializer.cs:56, 81) throws JsonException, MessagePack (:57, :82) throws, and Protobuf (ProtobufBinarySerializer.cs:83) returns a fully default-constructed non-null instance. One member, five outcomes.

#### SH-M267 — Byte-path text decoding silently substitutes U+FFFD for invalid UTF-8

`../Birko.Serialization.Yaml/YamlDotNetSerializer.cs:82`

`DeserializeFromBytes` (82, 89) decodes with `Encoding.UTF8.GetString(data)`, whose default decoder fallback is replacement, not throw. A payload from a non-UTF-8 producer, or one truncated mid multi-byte sequence, decodes without error into text containing U+FFFD and deserializes 'successfully' with corrupted string values — no exception anywhere. Identical at NewtonsoftJsonSerializer.cs:72 and :79. STJ instead hands raw bytes to System.Text.Json (SystemJsonSerializer.cs:75, 81), which rejects invalid UTF-8.

#### SH-M268 — No implementation can serialize a null graph although every read returns T?

`../Birko.Serialization/Core/ISerializer.cs:32`

Every write member of all six impls opens with `ArgumentNullException.ThrowIfNull(value)` (SystemJsonSerializer.cs:42, SystemXmlSerializer.cs:54, MessagePackBinarySerializer.cs:40, ProtobufBinarySerializer.cs:32, YamlDotNetSerializer.cs:50; Newtonsoft's byte members inherit it by delegation). So `Serialize<Payload?>(null)` throws instead of emitting the format's null literal, while every read member is declared `T?`/`object?` and can read a null back. A legitimately null queue/job/workflow payload is unrepresentable and no member or option expresses it, forcing callers to invent a sentinel.

#### SH-M269 — Async members throw synchronously in three impls and fault the Task in the others

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:159`

SystemXmlSerializer (all four, 159-201), ProtobufBinarySerializer (all four, 113-146), YamlDotNetSerializer (all four, 128-158) and MessagePack's SerializeAsync/SerializeAsync<T> (116, 123) are plain Task-returning, so ArgumentNullException/OperationCanceledException are raised at the call site; STJ (all four), Newtonsoft (all four) and MessagePack's two DeserializeAsync are `async`, so the same misuse arrives as a faulted Task. `var t = s.SerializeAsync(...); try { await t; } catch {}` catches it for half the impls and crashes outside the try for the rest.

#### SH-M270 — JsonWriterOptions govern exactly one SystemJsonSerializer overload; indentation diverges

`../Birko.Serialization/Json/SystemJsonSerializer.cs:95`  ·  _restates a first-pass finding_

`_writerOptions` is read only by `Serialize<T>(Stream,T)` (95, `new Utf8JsonWriter(stream,_writerOptions)`); every other member formats from `_options`. `new SystemJsonSerializer(new JsonSerializerOptions{WriteIndented=true})` (writerOptions omitted → Indented=false) makes `Serialize(value)`, `SerializeToBytes(value)` and `Serialize(stream,(object)value)` indent while `Serialize<T>(stream,value)` stays compact — and conversely `new SystemJsonSerializer(null, new JsonWriterOptions{Indented=true})` affects only that one member. The two objects are never reconciled.

#### SH-M271 — Newtonsoft's NullValueHandling.Ignore default makes the two Format.Json impls non-interchangeable

`../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs:23`  ·  _restates a first-pass finding_

The default `_settings` (20-25) adds `NullValueHandling.Ignore` on top of camelCase, dropping null-valued properties; SystemJsonSerializer's default (19-23) sets only camelCase + `WriteIndented=false` and emits `"description":null`. Both report `SerializationFormat.Json` and `application/json` (28-30 vs 30-32), so a component persisting with one and reading with the other — the substitution the shared Format enum invites — sees an asymmetric wire format, and a Newtonsoft round-trip cannot distinguish absent from explicit null.

#### SH-M272 — Protobuf untyped writes pass no runtime type while the read path names the Type

`../Birko.Serialization.Protobuf/ProtobufBinarySerializer.cs:26`  ·  _restates a first-pass finding_

All four untyped reads call `Serializer.Deserialize(type, stream)` with the caller's Type (44, 76, 104, 139), but all four untyped writes call `Serializer.Serialize(stream, value)` with `value` declared `object` and no type argument (26, 59, 90, 118), so protobuf-net's generic parameter binds to System.Object rather than `value.GetType()`. Every other impl passes `value.GetType()` here. The shared project declares no PackageReference (CLAUDE.md defers it to the consumer), so the same code can round-trip on one consumer and throw on another major version.

#### SH-M273 — Base64 string overloads contradict the ContentType the same instance advertises

`../Birko.Serialization.MessagePack/MessagePackBinarySerializer.cs:27`

`Serialize`/`Serialize<T>` return `Convert.ToBase64String(bytes)` (35, 42; ProtobufBinarySerializer.cs:27, 35), yet `ContentType` is the constant `application/x-msgpack` / `application/x-protobuf` (27; Protobuf 18) with no per-member variant. The README documents `serializer.ContentType` as the HTTP header value, so `new StringContent(s.Serialize(v), Encoding.UTF8, s.ContentType)` ships base64 ASCII labelled as raw binary msgpack; a receiver decoding by content type fails, and the mismatch is invisible to the sender.

#### SH-M274 — Newtonsoft DeserializeAsync skips the up-front token check its SerializeAsync added

`../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs:158`  ·  _restates a first-pass finding_

`SerializeAsync`/`SerializeAsync<T>` call `ThrowIfCancellationRequested()` with an explicit CR-L361 comment (130, 143); both DeserializeAsync overloads omit it and wrap the read in `Task.Run(() => serializer.Deserialize(...), cancellationToken)` (158, 167). With a pre-cancelled token the readers are still built and the caller sees TaskCanceledException where every other impl raises OperationCanceledException; a token cancelled after the delegate starts cannot interrupt the read. It is also the only member that moves work to a pool thread.

### area: settings-configuration-chain

#### SH-M275 — RedisSettings.LoadFrom(Settings) silently discards ALL fields for a non-RedisSettings source

`../Birko.Redis/RedisSettings.cs:146`  ·  _restates a first-pass finding_

The override is `if (data is RedisSettings redisData) LoadFrom(redisData);` with no `else base.LoadFrom(data)`. Every other override in the family (SqlSettings:56, MSSql:45, MySql:44, PostgreSql:56, SqLite:54, Mongo:116, Raven:58, Cosmos:67) has that else-branch. Loading from a plain Settings/RemoteSettings copies nothing, so a target built by the parameterless ctor stays at localhost:6379 and every cache/job-queue read and write silently goes to a local Redis instead of the configured host.

#### SH-M276 — TimescaleDBSettings never overrides LoadFrom(Settings), so TimeColumn/ChunkTimeInterval are dropped

`../Birko.Data.TimescaleDB/Stores/Settings.cs:70`  ·  _restates a first-pass finding_

Only `LoadFrom(TimescaleDBSettings)` is declared — a different signature from the virtual Settings.LoadFrom(Settings), so it is an overload, not an override. A copy through any base-typed reference (including the LoadFrom(ISettings) bridge) dispatches to SqlSettings.LoadFrom(Settings) and leaves TimeColumn/ChunkTimeInterval untouched. TimescaleDBConnector.cs:96-98 feeds exactly these two into CreateHypertable, so the hypertable is partitioned on the wrong column/interval with no error.

#### SH-M277 — InfluxDB Settings never overrides LoadFrom(Settings), so Token and Organization are dropped

`../Birko.Data.InfluxDB/Stores/Settings.cs:99`  ·  _restates a first-pass finding_

`LoadFrom(Settings data)` here takes its OWN type (Birko.Data.InfluxDB.Stores.Settings), so the inherited virtual Birko.Configuration.Settings.LoadFrom is never overridden. A base-typed copy reaches the base body and transfers only Location and Name; Token and Organization keep the target's previous values (null! on a fresh instance), surfacing as a 401 from InfluxDB far from the configuration site.

#### SH-M278 — ElasticSearch Settings declares no ILoadable and no override, so IndexSettings is never copied

`../Birko.Data.ElasticSearch/Stores/Settings.cs:18`  ·  _restates a first-pass finding_

The class adds `IEnumerable<IndexSettings> IndexSettings { get; set; } = null!` but declares no LoadFrom override, no ILoadable<Settings> and no GetId override — the only settings class in the family with none of the three. Every LoadFrom lands on the base body (Location/Name only), so the per-type index-name and MaxResultWindow mapping is lost on copy and ElasticSearchStoreHelper.cs:32 falls back to `type.Name` as the index.

#### SH-M279 — TimescaleDBSettings.GetConnectionString builds an Npgsql string by raw interpolation

`../Birko.Data.TimescaleDB/Stores/Settings.cs:54`  ·  _restates a first-pass finding_

PostgreSqlSettings.cs:26-30 was explicitly changed (comment cites CR-L189) to compose via NpgsqlConnectionStringBuilder so values containing ';' or '=' are quoted rather than breaking key=value parsing. TimescaleDB uses the same driver but still interpolates. Password `pa;ss` yields `Password=pa;ss;`, making `ss` a bare keyword; a password containing `SSL Mode=Disable;` silently downgrades transport security. The fix reached one of the two Npgsql providers.

#### SH-M280 — MSSql and MySQL connection strings interpolate Password/UserName/Location without escaping

`../Birko.Data.SQL.MSSql/Stores/MSSqlSettings.cs:32`  ·  _restates a first-pass finding_

Both compose by plain interpolation (MSSqlSettings.cs:32, MySqlSettings.cs:27). A password containing ';' terminates the value early and the remainder parses as further keywords; a value containing '=' corrupts the pair. Worse than a loud failure: an injected `TrustServerCertificate=True` or `Encrypt=False` fragment silently weakens transport security on a connection the settings object claims is secure. SqlClient and MySqlConnector both ship a builder that quotes correctly; neither is used.

#### SH-M281 — MongoDB URI does not URL-encode credentials and drops a username that has no password

`../Birko.Data.MongoDB/Stores/Settings.cs:52`  ·  _restates a first-pass finding_

Credentials are appended as `$"{UserName}:{Password}@"` with no percent-encoding, so a password containing '@', ':', '/' or '?' produces an ambiguous or invalid mongodb:// URI (`p@ss` yields two '@'). Separately the guard requires BOTH UserName and Password non-empty, so a configuration with a username but an empty password omits the credential segment entirely and connects anonymously — the operator sees an authorization error on first query, not a configuration error.

#### SH-M282 — RavenDB CreateDocumentStore ignores the UserName/Password/UseSecure its constructor accepts

`../Birko.Data.RavenDB/Stores/Settings.cs:29`  ·  _restates a first-pass finding_

CreateDocumentStore sets only Urls, Database and Conventions.RequestTimeout — no Certificate, no credential wiring — yet the ctor (line 23) takes username and password and forwards them into inherited slots nothing reads. A secured RavenDB cluster is unreachable while the configuration looks complete. Compounded by GetId() (line 44) returning only "{Location}:{Name}", so two Raven configs differing only in credentials are indistinguishable to the StoreLocator cache.

#### SH-M283 — Settings.LoadFrom overwrites the target's Location/Name with nulls from a sparser source

`../Birko.Configuration/Settings.cs:84`

The CR-H037/H038 fix made every override delegate to base unconditionally before its type guard, and the base body assigns Location/Name with no emptiness check. Both default to `null!`, so `configuredSqlSettings.LoadFrom(new Birko.Configuration.Settings())` — or from any source whose Location/Name were never assigned, e.g. a RedisSettings whose ctor passes string.Empty for Name — wipes the target's server and database. Result is not an exception but `Server=tcp:,0;Initial Catalog=;`.

#### SH-M284 — SqLiteSettings embeds Password in the connection string unescaped

`../Birko.Data.SQL.SqLite/Stores/SqLiteSettings.cs:39`

`cs += $";Password={Password}"` with no quoting. A password containing ';' injects further keywords: `pw;Mode=Memory` yields `Data Source=...;Password=pw;Mode=Memory;Default Timeout=30`, which opens an in-memory database instead of the file, so every write is silently discarded at process exit. Same escaping defect as MSSql/MySQL/TimescaleDB, on a third provider the earlier fix also missed.

#### SH-M285 — ElasticSearch Settings has no GetId override, so different index mappings collide in the store cache

`../Birko.Data.ElasticSearch/Stores/Settings.cs:10`

It inherits GetId() = "{Location}:{Name}", which excludes IndexSettings. StoreLocator.cs:25-44 keys its cache on GetId()+store type and on a cache hit does NOT call SetSettings, so a second request with the same cluster/name but a different per-type index mapping receives the store built from the first settings. ElasticSearchStoreHelper.cs:32 resolves the index name from that stale mapping, so reads and writes land in the wrong index — crossing tenants wherever indices are the tenant boundary.

#### SH-M286 — CosmosDB GetId omits PartitionKeyPath, so a cached store uses the wrong partition key path

`../Birko.Data.CosmosDB/Stores/Settings.cs:51`

GetId() returns "{Location}:{Name}:{UserName}" — PartitionKeyPath, the one field that changes document addressing, is absent. Two settings differing only in it produce the same id, and StoreLocator.cs:35-44 returns the first-built store without re-applying settings. CosmosDBStore.cs:370 / AsyncCosmosDBStore.cs:462 then create and address the container with the first config's path, so point reads keyed by guid resolve against a partition key the caller never configured and return nothing.

#### SH-M287 — MongoDB GetId omits AuthDatabase and ReplicaSet, so different clusters share an identity

`../Birko.Data.MongoDB/Stores/Settings.cs:97`

GetId() is "{Location}:{Port}:{Name}:{UserName}" while GetConnectionString (lines 69-82) additionally encodes AuthDatabase, ReplicaSet and tls. Two settings differing only in ReplicaSet (a different cluster topology), AuthDatabase (a different credential store) or UseSecure (plaintext vs TLS) return the same id, and StoreLocator returns the first store built — so a caller asking for the replica-set/TLS configuration silently gets a direct unencrypted connection to the other one.

#### SH-M288 — RedisSettings GetId omits RawConnectionString and KeyPrefix, the fields that redefine the target

`../Birko.Redis/RedisSettings.cs:122`

GetId() = base.GetId() + Database. RawConnectionString is ignored, yet GetConnectionString (line 74) returns it verbatim and ignores Location/Port — so a plain instance and one whose RawConnectionString names an entirely different cluster both yield "localhost:::6379:0". KeyPrefix, documented at lines 22-23 as the namespace-isolation mechanism, is likewise absent. Any GetId-keyed reuse hands back a client for the wrong server or the wrong key namespace.

#### SH-M289 — MSSqlConnector rebuilds MSSqlSettings from a RemoteSettings, discarding configured timeouts and flags

`../Birko.Data.SQL.MSSql/Database/Connector/MSSqlConnector.cs:116`

When settings is a RemoteSettings/SqlSettings but not an MSSqlSettings, the connector constructs `new MSSqlSettings(Location, Name, UserName, Password, Port, UseSecure)` and calls GetConnectionString() on it. The derived instance re-defaults ConnectionTimeout to 15, CommandTimeout to 30, MultipleActiveResultSets to false and TrustServerCertificate to false, so a SqlSettings carrying ConnectionTimeout = 60 silently connects with 15. File is outside this area's manifest but consumes its contract.

### area: specifications-and-paging

#### SH-M290 — IsNull/IsNotNull on a non-nullable value-type property throws ArgumentException out of ToExpression()

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:108`  ·  _restates a first-pass finding_

BuildNullCheck calls Expression.Constant(null, member.Type) with no nullability guard, outside any try. Verified by running it: Expression.Constant(null, typeof(int)) throws ArgumentException "Argument types do not match" (typeof(int?) is fine). A rule `Qty IsNull` on `int Qty` therefore throws from ToExpression() while every sibling path degrades gracefully and IsSatisfiedBy handles it (ComparisonHelper.IsNull => actual is null). RuleExpressionConverter.BuildIsNull returns Constant(false) for this case.

#### SH-M291 — Ordering comparisons on string/bool/non-comparable members throw InvalidOperationException from ToExpression()

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:124`

BuildComparison calls comparison(member, constant) outside the conversion try-block. Verified: Expression.GreaterThan on two strings throws InvalidOperationException "The binary operator GreaterThan is not defined for the types 'System.String'", likewise GreaterThanOrEqual on bool and Equal on a struct with no op_Equality. So `Name GreaterThan "a"`, or any Between on a string property (BuildBetween line 132-134), throws out of ToExpression() — while ComparisonHelper.CompareValues falls back to string.Compare and evaluates it fine in memory.

#### SH-M292 — Guid and enum-name string values fail Convert.ChangeType, silently degrading the leaf to match-nothing

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:182`

TryConvertConstant falls through to Convert.ChangeType for any value not already the member type. Verified: ChangeType("<valid guid>", typeof(Guid)) and ChangeType("Open", typeof(SomeEnum)) both throw InvalidCastException, which the catch at line 188 swallows into Constant(false). Since rule values normally arrive as strings from JSON/a UI rule builder, `Guid Equal "<guid>"` on the AbstractModel key matches zero rows with no error — while ComparisonHelper.AreEqual compares ToString() and matches. RuleExpressionConverter.ConvertValue parses Guid/DateTime/enum explicitly.

#### SH-M293 — Convert.ChangeType is called with no IFormatProvider, so rule values are parsed in the server's current culture

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:182`

`Convert.ChangeType(value, underlying)` uses CurrentCulture. Verified under this machine's sk-SK: ChangeType("1.5", typeof(decimal)) throws FormatException and is swallowed into Constant(false) (a price rule matches nothing), and ChangeType("02/03/2026", typeof(DateTime)) yields 2 March, not 3 February — so the same stored rule filters different rows on differently-localised hosts. RuleExpressionConverter.ConvertValue passes CultureInfo.InvariantCulture for exactly this reason.

#### SH-M294 — Convert.ChangeType silently rounds a fractional value to an integer member, matching rows in-memory evaluation rejects

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:182`

Verified: Convert.ChangeType(10.7, typeof(int)) returns 11, so a rule `Qty Equal 10.7` on `int Qty` emits `x.Qty == 11` and the store filter selects rows with Qty 11 — a value the rule never named. ComparisonHelper.AreEqual(11, 10.7) returns false (both promoted to double, diff 0.3 exceeds the relative tolerance), so in-memory evaluation rejects the same entity. On Update(filter,...)/Delete(filter) this mutates rows that do not satisfy the rule.

#### SH-M295 — String Equal/NotEqual are case-sensitive at the store but case-insensitive in memory

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:86`

The Equal/NotEqual arms go through BuildComparison → Expression.Equal, i.e. ordinal string equality. Verified compiled: `x.Name == "open"` against Name="Open" returns false. ComparisonHelper.AreEqual (ComparisonHelper.cs:68) ends with string.Equals(..., OrdinalIgnoreCase) and returns true for the same pair, so IsSatisfiedBy and ToExpression select different rows for the commonest rule shape of all. RuleExpressionConverter.BuildComparison special-cases string equality to string.Equals(OrdinalIgnoreCase) precisely to avoid this.

#### SH-M296 — A null rule value turns Contains/StartsWith/EndsWith into match-all (and NotContains into match-none)

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:145`  ·  _restates a first-pass finding_

BuildStringMethod builds the needle as `value?.ToString() ?? string.Empty`, so a null Value yields "" and `x.Name.Contains("", OrdinalIgnoreCase)` is true for every non-null string — a whole-table filter on Read/Update/Delete. ComparisonHelper.ContainsString/StartsWithString return false when expected is null, so the same rule is match-all at the store and match-none in memory; the NotContains arm inverts both, giving match-none at the store and match-all in memory.

#### SH-M297 — An unresolvable or nested field with IsNull/IsNotNull matches every entity in memory but no row at the store

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:47`

BuildLeafExpression returns Constant(false) when typeof(T).GetProperty fails (line 77-78) — including any dotted field such as "Address.City", which this translator does not support. IsSatisfiedBy takes the other path: RuleEvaluator.EvaluateLeaf lines 48-53 ignore the TryGetValue result for IsNull/IsNotNull, so Compare(null, IsNull, null) returns true and EVERY entity satisfies a null-check on a field that does not exist. A typo'd or nested field therefore reports 0 rows from the store and 100% match in memory.

#### SH-M298 — Composing a RuleSpecification routes in-memory evaluation back through the divergent LINQ path, defeating CR-H074

`../Birko.Data.Patterns/Specification/AndSpecification.cs:25`

AndSpecification/OrSpecification/NotSpecification do not override IsSatisfiedBy, so the base (Specification.cs:23) compiles ToExpression() — the hand-built rule tree — instead of dispatching to RuleSpecification's evaluator override. `ruleSpec.IsSatisfiedBy(e)` and `(ruleSpec & other).IsSatisfiedBy(e)` therefore disagree wherever the two halves diverge (In/Like match-all, string casing, null needle), and a composite's IsSatisfiedBy can even throw (IsNull on int, GreaterThan on string) where the operand alone succeeds — the exact dispatch problem the override's comment claims to have fixed.

#### SH-M299 — RuleSpecification hand-rolls a second rule→LINQ translator instead of using Birko.Rules.RuleExpressionConverter

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:31`

Birko.Rules/Expressions/RuleExpressionConverter.cs is a complete public converter for the same IRule/RuleSet model, and it is correct on every point this file gets wrong: In/NotIn/Like, group IsNegated, IsNull on a non-nullable value type, nested "A.B" fields with null guards, invariant-culture and Guid/DateTime/enum conversion, case-insensitive string equality. Two translators for one model means the store filter a caller gets depends on which entry point they picked — and a disabled RuleSet is match-nothing here versus no-filter (match-everything) there.

#### SH-M300 — Offset multiply is unchecked int arithmetic, so a large page number produces a negative offset

`../Birko.Data.Patterns/Paging/PagedRepositoryWrapper.cs:40`  ·  _restates a first-pass finding_

`(page - 1) * pageSize` is unchecked and page is clamped only at the low end, so page=int.MaxValue with pageSize=20 wraps to a negative offset handed straight to IBulkReadRepository.Read(filter, orderBy, limit, offset) and on to the backend's OFFSET clause — provider-specific failure or wrong window instead of an empty page. AsyncPagedRepositoryWrapper.cs:43 is identical.

#### SH-M301 — Paging with orderBy = null produces non-deterministic page windows (rows repeated or skipped)

`../Birko.Data.Patterns/Paging/PagedRepositoryWrapper.cs:41`

orderBy defaults to null in both wrappers and interfaces and is forwarded unchanged; DataBaseBulkStore.ReadCore passes `orderBy?.ToDictionary()`, so SQL gets LIMIT/OFFSET with no ORDER BY, whose row order is unspecified. Paging a grid with the default arguments can therefore show the same entity on pages 1 and 2 and never show another, with no error and a TotalCount that looks consistent. Neither the interface docs nor the wrappers require or supply a deterministic tiebreak sort.

### area: store-crud-contract

#### SH-M302 — IsStoreOfType returns false for any 2+ layer wrapper chain, so backend repository ctors reject composed stores

`../Birko.Data.Stores/StoreExtensions.cs:79`

`if (store is IStoreWrapper<T> w) return w.GetInnerStoreAs<TStore>() != null;` returns directly, never reaching the chain walk at line 85. Every wrapper implements GetInnerStoreAs as a SINGLE-level `_innerStore as TInner` (AuditStoreWrapper.cs:62, SoftDeleteStoreWrapper.cs:82, DefaultStoreWrapper.cs:129, + async twins). On the chain StoreWrapperBuilder builds, the inner store is another wrapper, so the cast is null and the answer is false. ElasticSearchModelRepository.cs:25 then throws ArgumentException; ~20 backend repositories gate identically.

#### SH-M303 — StoreLocator returns the cached instance by indexing the Dictionary outside the lock

`../Birko.Data.Stores/StoreLocator.cs:44`  ·  _restates a first-pass finding_

`return (TStore)_stores[id][type];` sits after the `lock (_lockObject)` block. Both `_stores` and the inner per-id map are plain non-thread-safe Dictionary. A thread reaching that return while another is inside the lock doing `_stores.Add` / `_stores[id].Add` can observe a resize in progress, yielding InvalidOperationException or a spurious KeyNotFoundException. Moving the return inside the lock fixes it.

#### SH-M304 — StoreLocator cannot serve async stores — the constraint is IBaseStore, which IAsyncBaseStore does not extend

`../Birko.Data.Stores/StoreLocator.cs:22`

Both overloads constrain `where TStore : IBaseStore` (lines 14, 22). IAsyncBaseStore (IAsyncStore.cs:13) is standalone and does not derive from IBaseStore, and AbstractAsyncStore<T>/AbstractAsyncBulkStore<T> pull in only IAsyncBaseStore. So `GetStore<AsyncInMemoryStore<Invoice>>()` does not compile and the entire async half of the store family is excluded from the process-wide cache, with no async locator anywhere.

#### SH-M305 — StoreLocator silently discards the settings argument on a cache hit

`../Birko.Data.Stores/StoreLocator.cs:35`

SetSettings runs only inside `if (!_stores[id].ContainsKey(type))`. The bucket key is settings.GetId(), and several providers omit fields from it — RavenDB Settings.GetId() returns only "{Location}:{Name}", dropping UserName/Password/UseSecure. Two settings instances differing only in credentials therefore collapse onto whichever was constructed first, with no error: the second caller gets a store configured with someone else's credentials and cannot tell.

#### SH-M306 — Save returns a Guid for an entity that was never persisted

`../Birko.Data.Stores/AbstractStore.cs:145`  ·  _restates a first-pass finding_

Save routes to Update whenever data.Guid is non-empty, then returns data.Guid!.Value. Update returns void and — in AbstractInMemoryStore.UpdateCore, which no-ops when the key is absent — cannot report that nothing was written. Save(entityWithFabricatedGuid) reports success and hands back a plausible identifier for data that does not exist. Same in AbstractAsyncStore.SaveAsync line 161.

#### SH-M307 — Destroy does not reset the initialization latch, so torn-down storage is never re-provisioned

`../Birko.Data.Stores/AbstractStore.cs:48`  ·  _restates a first-pass finding_

Destroy()/DestroyAsync() are abstract and the private `_initialized` field is never cleared. For a store whose InitCore provisions backend structures and whose Destroy removes them — RavenDBStore drops the database, CosmosDBStore deletes the container, MongoDBStore drops the collection, JsonStore deletes the file — the next CRUD call sees _initialized == true, skips InitCore, and operates against storage that no longer exists.

#### SH-M308 — In-memory UpdateCore's ContainsKey-then-indexer is a non-atomic check-then-act that resurrects deleted entities

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:90`

`if (data?.Guid != null && _items.ContainsKey(data.Guid.Value)) { ...; _items[data.Guid.Value] = data; }` — ConcurrentDictionary makes each call atomic but not the pair. If another thread removes the key between the ContainsKey and the indexer assignment, the update writes the entity back and a deleted row reappears. TryUpdate/AddOrUpdate exists for this. Same shape in the bulk override at line 171 (Where evaluates ContainsKey lazily while the loop body writes) and in AbstractAsyncInMemoryStore.cs:94 and :182.

#### SH-M309 — In-memory UpdateCore silently drops the write when the key is absent, with no channel to report it

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:88`

The whole body is guarded by `data?.Guid != null && _items.ContainsKey(...)`; updating an entity whose Guid is null, or absent from _items, returns successfully with nothing written and the delegate never invoked. Update returns void so there is no result to inspect, and StoreException exists for exactly this failure but is never thrown. The bulk override (line 171) additionally applies partially — some items written, some skipped, no report of which. This is the mechanism behind Save's bogus success return.

#### SH-M310 — In-memory CreateCore silently upserts over an existing key where a real backend's primary key would reject

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:63`

`_items[data.Guid.Value] = data;` is an indexer assignment with no duplicate-key detection. Create(entityWithExistingGuid) silently replaces the stored entity and reports success, whereas a SQL store with a Guid primary key raises a unique-constraint violation and Mongo/Cosmos reject the duplicate id. Since InMemory is the canonical test double, a create path that can repeat an identifier passes in tests and destroys a row in production. Same at AbstractAsyncInMemoryStore.cs:67 and both bulk create paths (160 / 170).

#### SH-M311 — In-memory store persists the caller's reference, not a snapshot, so post-write mutation silently changes stored state

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:63`

`_items[data.Guid.Value] = data` stores the live object, and ReadCore/Read(Guid)/the bulk ReadCore hand the same reference back. Every serializing backend (SQL, JSON, XML, Mongo, ES) takes a value copy at write time. So `store.Create(e); e.Name = "x";` leaves the InMemory store holding "x" with no second write, and a read-modify-without-Update sequence appears to persist. AbstractModel exposes CopyTo for exactly this and it is unused, so the test double gives false-green for the whole class of missing-Update bugs.

#### SH-M312 — In-memory Delete(filter) overrides the public member and bypasses DeleteCore entirely

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:194`

AbstractBulkStore.Delete(filter) (AbstractBulkStore.cs:97-101) reads the matches then routes them through Delete(items) -> DeleteCore(IEnumerable<T>). This override goes straight to `_items.TryRemove`, so a subclass overriding DeleteCore — the documented extension point — never sees filter-based deletes and any logging/auditing it adds is skipped on the destructive path only. Also violates the convention that concrete stores override protected *Core, not public CRUD (MongoDBStore.cs:99 records CR-M117 removing such overrides). Same at AbstractAsyncInMemoryStore.cs:207.

#### SH-M313 — In-memory Read(Guid) makes an entity stored under Guid.Empty unreachable by identifier, unlike other backends

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:77`

The base AbstractStore.Read(Guid) (lines 79-82) builds ModelByGuid<T>(guid).Filter() = `x => x.Guid == guid`, which matches an entity whose Guid is explicitly Guid.Empty. This override short-circuits on `guid != Guid.Empty` and returns null. Since CreateCore's `??=` (line 61) happily stores an entity under the Guid.Empty key, InMemory can hold a row it can never return by identifier while SQL/Mongo/ES, which use the inherited Read(Guid), return it. Same at AbstractAsyncInMemoryStore.cs:81.

#### SH-M314 — CreateCore's `??=` lets Guid.Empty survive, colliding with the null-data failure return

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:61`  ·  _restates a first-pass finding_

`data.Guid ??= Guid.NewGuid()` only replaces a NULL Guid. An entity arriving with Guid.Empty is stored under the Guid.Empty key and CreateCore returns Guid.Empty — the exact value returned when data was null (line 59), so success is indistinguishable from failure. Worse, the Read(Guid) override at line 80 rejects Guid.Empty, so the entity is unreachable by identifier. Same at AbstractAsyncInMemoryStore.cs:65 and in both bulk create paths.

#### SH-M315 — StoreDataDelegate<T> returns T but every invocation discards the return value

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:62`  ·  _restates a first-pass finding_

The delegate is declared `public delegate T StoreDataDelegate<T>(T data)` (IStore.cs:13), yet all call sites are `storeDelegate?.Invoke(data);` followed by persisting the original `data` reference (AbstractInMemoryStore.cs:62/92/159/173, AbstractAsyncInMemoryStore.cs:66/96/169/184). A delegate written in the natural functional style — returning a modified copy rather than mutating in place — is silently ignored and the unmodified entity is stored. Either honour the return or declare it as Action<T>.

#### SH-M316 — InitCore re-entrancy fails two different ways in the sync and async bases for identical subclass code

`../Birko.Data.Stores/AbstractStore.cs:27`

`lock (_initLock)` is a re-entrant Monitor and `_initialized = true` is set only after InitCore() returns (line 31), so an InitCore seeding defaults via this.Create(...) re-enters EnsureInitialized on the same thread, still sees false, and recurses to an uncatchable StackOverflowException. The async base uses SemaphoreSlim (AbstractAsyncStore.cs:18/39), which has no owner affinity, so identical code deadlocks forever instead. One mistake, two unrelated symptoms, so it ports between hierarchies undetected.

### area: store-decorator-composition

#### SH-M317 — PropertyUpdate assignments bind to the marker INTERFACE member, so the SQL SET clause ignores column remaps

`../Birko.Data.Patterns/Decorators/AuditBulkStoreWrapper.cs:60`

In `updates.Set(x => x.UpdatedBy, ...)` T is constrained `AbstractModel, IAuditable`; AbstractModel has no UpdatedBy so the member binds to IAuditable.UpdatedBy, making Member.ReflectedType typeof(IAuditable). GetFieldFromLambda (DataBase_Field.cs:158-165) only remaps AbstractModel/AbstractLogModel, so LoadField runs on the interface property, which has no [NamedField], and CreateAbstractField (AbstractField.cs:68) falls back to property.Name. DataBaseBulkStore.cs:125 uses that as the SET column, so a renamed column yields an UPDATE against a column that does not exist.

#### SH-M318 — Async Audit/Timestamp/SoftDelete SaveAsync discard the created Guid and can return Guid.Empty

`../Birko.Data.Patterns/Decorators/AsyncAuditStoreWrapper.cs:46`  ·  _restates a first-pass finding_

`await CreateAsync(data, processDelegate, ct)` at line 50 discards its result and line 56 returns `data.Guid ?? Guid.Empty`. An inner store that generates the Guid and returns it without assigning it back gives the caller Guid.Empty for a row that was created. Same at AsyncTimestampStoreWrapper.cs:54/60 and AsyncSoftDeleteStoreWrapper.cs:68/74. The sync twins (AuditStoreWrapper.cs:48, TimestampStoreWrapper.cs:52, SoftDeleteStoreWrapper.cs:68) all `return Create(...)`, as do Sluggable/Default/Versioned, so Save's result depends on the flavour used.

#### SH-M319 — SluggableBulkStoreWrapper.Update omits the in-batch slug set, allowing duplicate slugs in one batch

`../Birko.Data.Patterns/Decorators/SluggableBulkStoreWrapper.cs:41`  ·  _restates a first-pass finding_

`ResolveSlug(item, item.Guid)` is called with no batchSlugs argument, so uniqueness is checked only against the inner store. Two rows in one batch both re-resolving to "widget" both keep "widget". Its own Create (line 31) passes a batch set and AsyncSluggableBulkStoreWrapper.UpdateAsync (line 53) does too, with a CR-H075 comment naming missing per-batch tracking as the defect being fixed. The sync Update was left behind.

#### SH-M320 — Default wrappers forward PropertyUpdate<T> untouched, so the single-default invariant can be violated

`../Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs:104`  ·  _restates a first-pass finding_

`UpdateAsync(filter, PropertyUpdate<T>)` is a straight pass-through (same at DefaultStoreWrapper.cs:100). `updates.Set(x => x.IsDefault, true)` with a filter matching N rows yields N rows with IsDefault=true, exactly what the decorator exists to prevent, while the Action<T> overload directly above (line 90) does enforce it.

#### SH-M321 — Timestamp PropertyUpdate overload sets UpdatedAt but never PrevUpdatedAt

`../Birko.Data.Patterns/Decorators/TimestampBulkStoreWrapper.cs:64`  ·  _restates a first-pass finding_

`Update(filter, PropertyUpdate<T>)` appends only `Set(x => x.UpdatedAt, now)`, breaking the previous-timestamp chain the class maintains everywhere else: PrevUpdatedAt keeps the value from two writes ago. The Action<T> overload above (line 57) sets both. Same at AsyncTimestampBulkStoreWrapper.cs:66. Consumers using PrevUpdatedAt for change detection or sync watermarks read a stale value.

#### SH-M322 — Audit and Timestamp bulk wrappers mutate the caller's PropertyUpdate<T> instance in place

`../Birko.Data.Patterns/Decorators/AsyncAuditBulkStoreWrapper.cs:62`  ·  _restates a first-pass finding_

`updates.Set(...)` appends to the caller-owned Assignments list (PropertyUpdate.cs:27 mutates and returns this), not a copy. Same at AuditBulkStoreWrapper.cs:60, TimestampBulkStoreWrapper.cs:64, AsyncTimestampBulkStoreWrapper.cs:66. Reusing one PropertyUpdate for a second call carries the first call's assignments forward, and a chain with both decorators appends one per decorator per call; the SQL path then throws ArgumentException on the duplicate dictionary key (DataBaseBulkStore.cs:125).

#### SH-M323 — Audit/Timestamp/SoftDelete bulk wrappers mutate entities inside a lazy Select, re-stamping on double enumeration

`../Birko.Data.Patterns/Decorators/AsyncTimestampBulkStoreWrapper.cs:45`  ·  _restates a first-pass finding_

The mutation lambda lives in `data.Select(...)`, so it runs whenever the inner store enumerates. An inner store that enumerates twice (count-then-write, retry) sets PrevUpdatedAt to the UpdatedAt the first pass just wrote, leaving both equal. AsyncSluggableBulkStoreWrapper.cs:31 materializes first for exactly this species (CR-M124); Audit (32, 43), Timestamp (33, 45) and SoftDelete (35, 59) plus their sync twins never got the same fix.

#### SH-M324 — UnsetOtherDefaults with excludeGuid == null skips existing default rows whose Guid is null

`../Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs:141`  ·  _restates a first-pass finding_

`allDefaults.Where(e => e.Guid != excludeGuid)` degenerates to `e.Guid != null` when excludeGuid is null, so a stored row with IsDefault=true and a null Guid is filtered out of the clearing pass and keeps the flag, leaving two defaults. Reached from batch create (line 53 passes literal null), CreateAsync(T) line 43 (Guid is null on create) and SaveAsync line 114. The alsoExcludeGuids HashSet at 85/144 has the same hole for a batch with a null-Guid item. Same at DefaultStoreWrapper.cs:136/51/41/110/83.

#### SH-M325 — Soft-delete entity-based delete paths re-stamp DeletedAt on already-deleted rows

`../Birko.Data.Patterns/Decorators/SoftDeleteBulkStoreWrapper.cs:57`  ·  _restates a first-pass finding_

`Delete(IEnumerable<T>)` and `Delete(T)` (SoftDeleteStoreWrapper.cs:60) assign `DeletedAt = _clock.UtcNow` unconditionally, overwriting the original deletion instant. The filter-based Delete(filter) on the same class (line 67) does guard via CombineWithNotDeleted. Whether the true deletion time survives depends on which overload the caller reaches, so retention/audit logic keyed on DeletedAt loses it. Same at AsyncSoftDeleteBulkStoreWrapper.cs:59 and AsyncSoftDeleteStoreWrapper.cs:60.

#### SH-M326 — Slug uniqueness probe reads only ONE row, so an update lets an existing duplicate slug survive

`../Birko.Data.Patterns/Decorators/SluggableStoreWrapper.cs:73`

`var existing = _innerStore.Read(BuildSlugFilter(slug)); return existing is not null && existing.Guid != excludeId;` asks whether THE returned row has a different Guid, not whether ANY other row holds the slug. With rows A and B both holding "widget" (reachable via the sync bulk-update defect, or a pre-existing dataset), updating A probes "widget", the store returns A, the Guid comparison is false, the candidate is reported free, A keeps "widget" and the duplicate is silently preserved and never repaired. Identical at AsyncSluggableStoreWrapper.cs:79.

#### SH-M327 — Slug resolution is an unguarded read-then-write, so concurrent creates assign the same slug

`../Birko.Data.Patterns/Decorators/AsyncSluggableStoreWrapper.cs:78`

ResolveSlugAsync probes with `_innerStore.ReadAsync(entity => entity.Slug == slug, ct)` and the caller then writes; nothing holds a lock, opens a transaction or relies on a unique index. Two requests creating entities whose slug source is "widget" both observe the slug free and both persist Slug="widget". The in-batch HashSet closes only the single-call window. Same at SluggableStoreWrapper.cs:72. Neither the decorator nor ISluggable documents that a storage-level unique index is required.

#### SH-M328 — UnsetOtherDefaults is an unguarded read-then-write, so concurrent promotions leave two default rows

`../Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs:140`

`ReadAsync(e => e.IsDefault)` then `UpdateAsync(toUpdate)` then the caller's own create/update: three round trips with no lock, transaction or conditional predicate. Two callers each writing an entity with IsDefault=true interleave so both read the same default set, both clear it, and both persist their own default. The invariant the decorator exists to enforce is violated with no error. Same at DefaultStoreWrapper.cs:136-152, and the builder emits no compensating storage constraint.

#### SH-M329 — DefaultStoreWrapper.Update(filter, action) re-enters the inner store from inside its own update callback

`../Birko.Data.Patterns/Decorators/DefaultStoreWrapper.cs:90`

The callback passed to `_innerStore.Update(filter, item => {...})` calls UnsetOtherDefaults(item.Guid), issuing an inner-store bulk Read plus bulk Update while the inner store is mid-iteration of its own filter-update. DataBaseBulkStore.Update(filter, Action<T>) (line 103) materializes with ToList() so SQL survives, but file-backed backends that rewrite the whole document per write (JSON/XML) and connection- or transaction-stateful stores see a nested write inside an in-flight operation. The async sibling reads first and writes after (AsyncDefaultStoreWrapper.cs:92-101).

#### SH-M330 — Default filter-update diverges sync vs async: one native filter-update vs N individual writes

`../Birko.Data.Patterns/Decorators/AsyncDefaultStoreWrapper.cs:92`

`UpdateAsync(filter, action)` reads all matches into a list then issues one `_innerStore.UpdateAsync(item)` per row, so the inner store's native filter-update path is never used and the call is N+1 round trips with no atomicity. `DefaultStoreWrapper.Update(filter, action)` (line 90) instead forwards to `_innerStore.Update(filter, callback)`. The same logical call is one statement on the sync chain and N on the async chain, and a mid-loop failure on the async path leaves an arbitrary prefix updated.

#### SH-M331 — Soft-delete entity Update forwards unguarded, so resurrecting a row re-introduces duplicate slugs/defaults

`../Birko.Data.Patterns/Decorators/SoftDeleteStoreWrapper.cs:50`

`Update(T)` (and AsyncSoftDeleteStoreWrapper.cs:50, SoftDeleteBulkStoreWrapper.cs:38) pass straight through with no DeletedAt == null check, while the filter-based overloads do combine it. Setting DeletedAt back to null restores the row; because the Sluggable probe (AsyncSluggableStoreWrapper.cs:78) and the Default probe (AsyncDefaultStoreWrapper.cs:140) read through the SoftDelete wrapper and never see deleted rows, the restored row still holds its old Slug and IsDefault=true, yielding two rows with one slug and/or two defaults, with no error on either write.

#### SH-M332 — A null filter turns soft-delete Delete(filter) into "soft-delete every active row"

`../Birko.Data.Patterns/Decorators/SoftDeleteBulkStoreWrapper.cs:67`

CombineWithNotDeleted returns the right-hand lambda unchanged when filter is null (ExpressionParameterReplacer.cs:33), so `Delete(null)` becomes `_innerStore.Update(x => x.DeletedAt == null, item => item.DeletedAt = now)`, a destructive operation over the whole table. Neither Delete(filter) nor Update(filter, updates) (line 48) null-checks its filter, and the decorator actively converts the null into a match-all-active predicate instead of rejecting it. Same at AsyncSoftDeleteBulkStoreWrapper.cs:69 and 50.

#### SH-M333 — Bulk soft delete by filter is a per-row read-modify-write, so it applies partially on failure

`../Birko.Data.Patterns/Decorators/AsyncSoftDeleteBulkStoreWrapper.cs:69`

`DeleteAsync(filter)` routes through `_innerStore.UpdateAsync(filter, Action<T>)`, which backends implement as read-then-update-each-row (DataBaseBulkStore.cs:102-109), rather than the PropertyUpdate<T> overload that exists to become one native UPDATE ... SET DeletedAt WHERE. A failure or cancellation halfway leaves some matched rows flagged deleted and others not, with no indication of progress and no affected-count returned. Same at SoftDeleteBulkStoreWrapper.cs:67.

#### SH-M334 — StoreWrapperBuilder silently ignores IVersioned, so composed chains never enforce optimistic concurrency

`../Birko.Data.Composition/StoreWrapperBuilder.cs:100`

Build probes IEventSourced, ITimestamped, IAuditable, ITenant, ISoftDeletable, ISluggable and IDefault but never IVersioned, and neither VersionedStoreWrapper nor AsyncVersionedStoreWrapper is referenced in the file. An entity declaring IVersioned gets a chain that treats Version as an ordinary column with no conflict detection, so lost updates occur while the marker suggests they cannot. There is also no sync Build overload, so the whole sync decorator family has no supported composition path.

#### SH-M335 — Versioned Update increments Version before the inner write, turning a transient failure into a permanent conflict

`../Birko.Data.Patterns/Concurrency/VersionedStoreWrapper.cs:50`

`data.Version++` runs at line 50 and `_inner.Update(data, storeDelegate)` at line 51. If the inner write throws (timeout, deadlock, dropped connection) the caller's entity is at Version+1 while storage is still at Version, so the obvious retry fails the `existing.Version != data.Version` check at line 45 and throws ConcurrentUpdateException forever. A transient I/O error is reported as a concurrency conflict the caller cannot resolve without re-reading. Same at AsyncVersionedStoreWrapper.cs:54-55.

#### SH-M336 — Composed chains silently drop capabilities outside IStore/IBulkStore, including aggregation and transactions

`../Birko.Data.Composition/StoreWrapperBuilder.cs:26`

Build takes and returns IAsyncBulkStore<T>, and every decorator implements only that surface. A raw store additionally implementing IAsyncAggregatableStore<T>, ITransactionalStore or ISettingsStore loses those members once wrapped: `wrapped as IAggregatableStore<T>` returns null and the caller must walk GetInnerStore() to reach them, at which point the decorators' soft-delete exclusion and tenant scoping are bypassed for that operation. Nothing forwards or re-exposes those interfaces.

### area: tenant-isolation

#### SH-M337 — Filter-based bulk Delete/Update forward Filter()! so a null filter with no tenant hits every tenant's rows

`../Birko.Data.Tenant/Stores/TenantBulkStoreWrapper.cs:78`

`_innerStore.Delete(TenantFilter(filter).Filter()!)` suppresses a genuinely nullable return: ModelByTenant.Filter() returns BaseFilter unchanged when the effective tenant is null (Permissive with no tenant, or inside an all-tenants scope). A caller passing a nullable expression through the non-nullable parameter (`store.Delete(f!)`) reaches DataBaseBulkStore.Delete(filter) with null, which calls `Connector.Delete(typeof(T), null)` — an unqualified DELETE. Same `!` at lines 68 and 73 and at AsyncTenantBulkStoreWrapper.cs:71, 76, 81.

#### SH-M338 — All-tenants scope widens reads but item writes are still tenant-compared, contradicting the comment at the seam

`../Birko.Data.Tenant/Stores/TenantStoreWrapper.cs:152`

TenantFilter's comment claims 'the write-authorization guards already special-case all-tenants scope', but BelongsToCurrentTenant (line 180) consults IsAllTenantsScope only inside `if (!HasTenant)`. With tenant t set, entering WithAllTenants widens reads to every tenant yet Update/Delete of any row so read throws TenantMismatchException. This is the reads-fail-open / writes-fail-closed split that ModelByTenant's own remarks record as the Guid.Empty bug (Symbio TASK-295), reintroduced for the admin scope.

#### SH-M339 — Tenant repository registration always wraps with the non-bulk wrapper, breaking or unscoping bulk stores

`../Birko.Data.Tenant/Repositories/RepositoryServiceCollectionExtensions.cs:42`

All four factories build TenantStoreWrapper<,> / AsyncTenantStoreWrapper<,> even when TStore implements IBulkStore/IAsyncBulkStore, diverging from AsTenantAware which picks the bulk wrapper. A TRepository whose constructor takes IAsyncBulkStore<TModel> (any AbstractAsyncBulkRepository) cannot be built from the non-bulk wrapper, so Activator.CreateInstance throws MissingMethodException at resolve time — also making the `?? throw new InvalidOperationException` arm unreachable. Where it does construct, the repository loses tenant-composed filter-based Update/Delete.

#### SH-M340 — ApplyTenantFiltering mutates the caller's SyncFilterOptions, accumulating one tenant predicate per run

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:155`

The method assigns filterOptions.CanSaveToLocal/CanSaveToRemote on the instance the caller passed in, capturing the previous delegate. Reusing one SyncFilterOptions across two syncs under tenants t then u yields `BelongsToTenant(u) && BelongsToTenant(t)` — no item satisfies both, so every save is silently skipped and the run reports success with zero writes. Same escaping-mutation defect CR-M168 fixed for Direction, at a second site.

#### SH-M341 — ApplyTenantContext mutates the caller-supplied TenantSyncOptions instance

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:102`  ·  _restates a first-pass finding_

For a TenantSyncOptions with a null TenantGuid it sets `tenantOptions.TenantGuid = CurrentTenantGuid` on that same instance and returns it, so the mutation escapes the call. Reusing one options object across two tenant scopes pins every later sync to the first tenant. CR-M168 fixed exactly this leak for Direction via an effectiveDirection local but left TenantGuid mutating in place.

#### SH-M342 — OnBatchCompleted reports every error so far, not the current batch's

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:528`  ·  _restates a first-pass finding_

`result.Errors.Skip(result.Errors.Count - progress.Errors)` takes the last progress.Errors entries, but progress.Errors is cumulative across batches and never reset per batch. With one error in batch 1 and one in batch 2, batch 2's callback receives both, so a consumer using the callback for per-batch alerting or retry double-reports earlier failures.

#### SH-M343 — Bidirectional SyncAction.Create is a silent no-op that still counts as processed and records knowledge

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:429`

The Create arm acts only when effectiveDirection is Download or Upload, but DetermineSyncAction returns Create for one-sided items in the Bidirectional branch (lines 667, 682) and effectiveDirection is then Bidirectional. Nothing is written, no counter moves (not even SkippedItems), totalProcessed++ runs and CreateKnowledgeItem records the item as synced. Bidirectional is the default Direction, so the default configuration drops every one-sided create while reporting Success == true, and the recorded knowledge later turns the missing side into a spurious deleted-remotely conflict.

#### SH-M344 — Preview and Sync disagree: AnalyzeItem ignores Direction and can never report a conflict

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:202`

AnalyzeItem re-implements the decision independently of DetermineSyncAction: it never reads options.Direction (so a Download-only run previews local-only items as ToCreate although the sync Skips them), and returns only Create/Update/Delete/Skip, so preview.Conflicts is always 0 and an item the sync will treat as a conflict is previewed as a plain Update. A caller using PreviewAsync as a dry run sees counts the following SyncAsync will not honour.

#### SH-M345 — A tenant-blocked Update is dropped with no counter and no error, unlike the Create branch

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:459`

In the Update arm `if (CanSaveToLocal?.Invoke(remoteItem) != false)` has no else, so when the tenant predicate rejects a cross-tenant item nothing is incremented — not UpdatedItems, not SkippedItems, no SyncError. The Create arm (lines 436-439, 448-451) increments SkippedItems for the identical condition. The item is still counted in totalProcessed and written to the knowledge store as synced, so the skip is invisible in SyncResult and never retried.

#### SH-M346 — Knowledge is recorded for items whose write was skipped, stamping foreign entity guids into the tenant's scope

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:504`

CreateKnowledgeItem runs for every item reaching the end of the try, whether or not the save predicate blocked the write. Because the fetch side is unscoped that includes other tenants' items: a TenantSyncKnowledgeItem carrying another tenant's EntityGuid is persisted with TenantGuid = the current tenant. The next run reads it as prior knowledge and infers IsLocalDeleted/IsRemoteDeleted from the item's absence, driving deletes and conflicts for rows this tenant never owned.

#### SH-M347 — Last-sync-time is written even when the run was cancelled or every item failed

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:534`

SetLastSyncTimeAsync(scope, tenant, DateTime.UtcNow) runs unconditionally after the batch loop, including when the loop broke on options.CancellationToken.IsCancellationRequested (line 408) and when result.Errors is non-empty. The next run then reports IsInitialSync == false, skipping the initial-download path that would have re-fetched the unprocessed items. A cancelled first sync permanently marks itself complete.

#### SH-M348 — TenantSyncProvider ignores OnSaveFilterBlock and OnError, so ThrowException on a tenant-blocked save is silently skipped

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:431`

Every save gate calls `filterOptions.CanSaveToLocal?.Invoke(item) != false` directly. The non-tenant siblings route the same decision through SyncProviderBase.CanSaveToLocal/HandleSaveFilterBlock (SyncProviderBase.cs:301), honouring SaveFilterBlockAction.ThrowException / LogAsError / MarkConflict and firing options.OnError / OnConflict. Here those settings are read by nothing, so a cross-tenant item — the exact case the tenant predicate blocks — can never be surfaced as an error or made to fail the sync.

#### SH-M349 — TenantSyncProvider ignores options.MaxItems and OnBatchStarting, which both non-tenant providers honour

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:402`

The batch loop iterates all of allGuids with no cap. SyncProvider.cs:61 and AsyncSyncProvider.cs:62 both truncate allGuids to options.MaxItems (CR-L207). A caller who limits a tenant sync to N items — the documented way to bound a first run — syncs the entire (cross-tenant) set instead. options.OnBatchStarting is likewise never invoked here while the siblings call it at AsyncSyncProvider.cs:156.

#### SH-M350 — TenantSyncQueue's per-tenant key does not give per-tenant concurrency — one semaphore serializes all tenants

`../Birko.Data.Sync.Tenant/TenantSyncQueue.cs:43`

GetQueueKey scopes only the bookkeeping Dictionary<string, Queue<QueuedSync>>. The actual gate is the single SyncQueue._semaphore created with maxConcurrentSyncs (default 1) and awaited in EnqueueWithKeyAsync regardless of key, so tenant t's sync blocks tenant u's for its whole duration. The class summary ('uses current tenant for queue scoping') and the spec scenario 'the two tenants do not serialize against one another' describe behaviour the code does not implement.

#### SH-M351 — ToDictionary(GetGuid) throws on duplicate or unset guids, aborting the sync as an opaque "Sync failed"

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:392`

GetGuid returns Guid.Empty whenever the reflected `Guid` property is null or not a Guid, so any two unsaved/unkeyed entities on one side collide and ToDictionary raises ArgumentException. In ExecuteSyncAsync the outer catch swallows it into one SyncError { Message = "Sync failed" } naming no item; in ExecutePreviewAsync it propagates raw. The two entry points fail differently for the same input and neither points at the offending rows.

#### SH-M352 — ISyncProvider entry points silently drop a mismatched filterOptions via `as`

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:904`

Both explicit interface implementations do `filterOptions as SyncFilterOptions<T>`, yielding null for any other type. A caller passing SyncFilterOptions<TOther> (easy through the non-generic ISyncProvider surface) loses LocalFetchPredicate/RemoteFetchPredicate and CanSaveTo* with no exception, and the run proceeds as a full unfiltered sync of both stores instead of reporting the mistake.

#### SH-M353 — An unattributed event dispatches with IsAllTenantsScope true, so Strict handlers operate across all tenants

`../Birko.EventBus.Tenant/TenantEventScopeAccessor.cs:59`

When EventContext is null or its TenantGuid is null, RunWithScopeAsync runs the handler inside WithAllTenantsAsync. Any publish path that missed the enricher — a hand-rolled EventContext, or the DI mismatch above — therefore escalates the handler to cross-tenant scope rather than failing closed. The fail direction is open: NullEventScopeAccessor (no scope at all) would leave Strict operations throwing instead of silently succeeding across tenants.

#### SH-M354 — RequireTenantHeaderMatchesClaim defaults true but is enforced only if the app also calls UseBirkoTenantHeaderGuard

`../Birko.Security.AspNetCore/Extensions/TenantHeaderGuardExtensions.cs:34`

The option is read exclusively by TenantHeaderClaimGuardMiddleware, which nothing registers automatically — AddBirkoSecurity wires auth, ICurrentUser, ITenantResolver and ITenantContext but not this middleware, and no startup check verifies the guard is in the pipeline. An app that adopts AddBirkoSecurity and never adds the Use call reports RequireTenantHeaderMatchesClaim == true while performing no comparison, contradicting the 'secure by default; an opt-in guard protects nobody' rationale.

### area: views-and-aggregation

#### SH-M355 — AggregateHelper mislabels group keys when a GroupByFields name does not resolve to a property

`../Birko.Data.Stores/AggregateHelper.cs:96`  ·  _restates a first-pass finding_

keyProperties is built with `.Where(p => p != null)`, so an unresolvable name shortens the array, but the row-labelling loop indexes `query.GroupByFields[i]` by position in the SHORTENED array. With GroupByFields == ["Bogus", "Status"] the Status value is written under the key "Bogus"; every downstream GetValue reads the wrong column and no error is raised.

#### SH-M356 — BuildCountAggregateSql emits SQL Cosmos cannot run and deserializes into the wrong shape

`../Birko.Data.CosmosDB.Views/CosmosViewStore.cs:262`  ·  _restates a first-pass finding_

It builds `SELECT VALUE COUNT(1) FROM (SELECT c.id FROM c ... GROUP BY c.X)` while its own comment states Cosmos does not support sub-queries in FROM; the inner query also projects non-grouped `c.id` alongside GROUP BY. Even if it executed, `SELECT VALUE COUNT(1)` returns a bare number, not an object with a `Count` member, so the private `CountResult` binding yields 0 — CountAsync would report zero rows for a populated view.

#### SH-M357 — CosmosViewStore.QueryFirstAsync/CountAsync branch on HasAggregates only, contradicting QueryAsync

`../Birko.Data.CosmosDB.Views/CosmosViewStore.cs:61`  ·  _restates a first-pass finding_

CR-L110 made QueryAsync take the SQL path for `HasAggregates || HasGroupBy`, but QueryFirstAsync (61) and CountAsync (75) still test HasAggregates alone. For a group-by-only (distinct) view, QueryAsync returns grouped rows while QueryFirstAsync returns a raw ungrouped document and CountAsync returns the raw document count — the same store answers three inconsistent questions about one definition.

#### SH-M358 — ElasticSearchViewStore.CountAsync ignores grouping and aggregation entirely

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:73`  ·  _restates a first-pass finding_

It issues a plain CountRequest against the resolved index with only the filter. For an aggregate/grouped view it returns the number of source DOCUMENTS, not the number of result rows QueryAsync produces on the same definition: 1,000,000 vs 12 status buckets. Any caller paging on Count/Query pages over a phantom result set.

#### SH-M359 — ElasticSearchViewStore aggregate path silently discards orderBy and offset

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:57`  ·  _restates a first-pass finding_

QueryAsync forwards only (filter, limit, ct) to ExecuteAggregateQueryAsync. A caller supplying OrderBy or offset for an aggregate view gets unordered, unpaged buckets with no error, and `limit` silently changes meaning from page size to terms-bucket size (line 188), so limit:10 caps the number of GROUPS rather than rows.

#### SH-M360 — ElasticSearchViewStore non-aggregate path never maps SourceProperty to ViewProperty

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:343`  ·  _restates a first-pass finding_

_sourceFields is built from FieldSelector.SourceProperty and used only as a `_source` include filter; the hits are deserialized straight into TView by NEST name mapping. There is no projection/rename step, so `Select(o => o.StatusCode, v => v.Status)` leaves TView.Status at its default. The same names are used for FieldSort (line 136), so ordering by a renamed property targets a field the index does not have.

#### SH-M361 — LinqAggregateAsync's raw-rows shortcut ignores OrderBy, Limit, Offset and TimeBucketInterval

`../Birko.Data.Stores/AggregateHelper.cs:50`

When `Aggregates.Count == 0 && GroupByFields.Count == 0` the method returns every filtered row and never reaches ApplyOrderingAndPaging (line 126) or the bucketing code. `new AggregateQuery<Order> { Limit = 10, Offset = 100, OrderBy = ..., TimeBucketInterval = "1 hour", TimeColumn = "Created" }` therefore returns the entire table, unordered and unbucketed, reporting success — a caller paging a large store gets everything.

#### SH-M362 — Composite group key is a "|"-joined ToString(), so distinct key tuples collide into one group

`../Birko.Data.Stores/AggregateHelper.cs:86`

`string.Join("|", key.Select(k => k?.ToString() ?? ""))` is the group key. Values ("a|b", "c") and ("a", "b|c") produce the identical key "a|b|c" and are summed into one row; null and "" also collide, as do any two values with equal ToString() (e.g. two enums of different types). Rows are silently merged and the emitted key columns come from `group.First()`, so one of the real key tuples disappears from the result.

#### SH-M363 — ComputeSum/ComputeAvg return null for short, byte, sbyte, ushort, uint and ulong properties

`../Birko.Data.Stores/AggregateMath.cs:36`

Both methods only branch on decimal/double/float/int/long and fall through to `return null`. A Sum or Avg over a `short`/`byte`/`uint`/`ulong` column silently yields a null cell with no error, while a native backend (SQL SUM, ES SumAggregation) returns a number for the same query — so the LINQ fallback and the server-side path disagree. ViewDefinitionBuilder.ValidateAggregates (line 210) explicitly accepts all of those types as valid Sum/Avg targets.

#### SH-M364 — TimeIntervalParser parses the numeric part with the current culture, so "1.5 hours" fails under a comma-decimal locale

`../Birko.Data.Stores/TimeIntervalParser.cs:29`

`double.TryParse(parts[0], out var value)` (and `TimeSpan.TryParse` on line 23) use CurrentCulture. On sk-SK/de-DE, "1.5 hours" fails to parse and Parse returns TimeSpan.Zero, which AggregateHelper reads as "bucketing disabled" (no bucket_time key at all) and StoreAggregationHelper.ParseToTime silently rewrites to "1h". The same query therefore buckets differently depending on the process locale, with no error.

#### SH-M365 — ES grouped aggregate parsing writes group keys under the SOURCE property name, so renamed keys are dropped

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:240`

Both the composite branch (240) and the terms branch (264) call `SetPropertyValue(item, viewType, groupBy.PropertyName, ...)` with the GroupByClause's SOURCE property name. With `GroupBy<Order>(o => o.StatusCode)` and `Select(o => o.StatusCode, v => v.Status)`, GetProperty("StatusCode") on TView returns null and SetPropertyValue returns silently, so every returned row carries metric values with a default (null/0) group key — the buckets become indistinguishable.

#### SH-M366 — ES metric extraction forces every value to double?, so Min/Max on a date or string field is silently dropped

`../Birko.Data.ElasticSearch/Aggregation/StoreAggregationHelper.cs:120`

ExtractMetricValues returns Dictionary<string,double?>, so a MinAggregation over a date field surfaces as epoch-millis double. ElasticSearchViewStore.SetPropertyValue then calls ConvertValue for a DateTime target, Convert.ChangeType(double, DateTime) throws, the catch returns false and the property is left at default with no error. `Min<Order,DateTime>(o => o.Created, v => v.Earliest)` — which ViewDefinitionBuilder explicitly permits — therefore always yields default(DateTime) on ES while SQL/Mongo/Raven return the real value.

#### SH-M367 — ES multi-field terms grouping discards all group keys

`../Birko.Data.ElasticSearch/Aggregation/StoreAggregationHelper.cs:209`

In ParseAggregateResponse (209) and ParseGroupedBuckets (276) the terms branch assigns a key only `if (query.GroupByFields.Count == 1)`. If a response carries a terms `group_by` while the query groups by two or more fields (e.g. a hand-built request, or a cached/older mapping), each row is emitted with metric values and NO key columns at all, so rows are indistinguishable and silently unattributable.

#### SH-M368 — Group-by aggregations default to size 10000 with no bucket-truncation detection

`../Birko.Data.ElasticSearch/Aggregation/StoreAggregationHelper.cs:62`

BuildGroupByAggregation caps both the terms and composite aggregation at `size = 10000` and neither caller inspects `DocCountErrorUpperBound`, `SumOtherDocCount` or the composite `AfterKey`. A view grouping by a high-cardinality field (customer, SKU) silently returns only the 10000 largest buckets — the caller sees a complete-looking result set that omits groups, and ElasticSearchViewStore passes `limit ?? 10000` here so a page size of 10 caps it at ten groups.

#### SH-M369 — ElasticSearchViewManager.EnsureAsync for an Auto-mode view creates the PRIMARY SOURCE index, not the view's

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewManager.cs:57`

ResolveIndexName delegates to ElasticSearchViewIndexResolver, which returns `definition.Name` only for Persistent mode; for an `Auto` definition named "order_summary" it returns "order". EnsureAsync then probes and, if missing, CREATEs the source-data index "order" with dynamic mapping — a lifecycle call for a view mutating the entity index. DropAsync("order_summary") afterwards targets a different index, so ensure/drop are not inverses for Auto views.

#### SH-M370 — MongoViewStore Persistent mode queries the named view unconditionally; a missing view returns empty, not an error

`../Birko.Data.MongoDB.Views/MongoViewStore.cs:134`

`useView` is true for Persistent without any existence check, and the CR-M122 comment on the very next lines records that aggregating against a non-existent MongoDB view/collection returns an EMPTY cursor rather than throwing. So a Persistent definition whose view was never created (or was dropped) makes QueryAsync return no rows and CountAsync return 0 — indistinguishable from a genuinely empty view. Auto mode was fixed; Persistent still fails silently.

#### SH-M371 — MongoViewManager.DropAsync drops any collection with the given name, view or not

`../Birko.Data.MongoDB.Views/MongoViewManager.cs:70`

It calls `_database.DropCollectionAsync(viewName)` with no check that the target is a view (ExistsAsync just above deliberately relies on views appearing in the collection-name listing, so the two are indistinguishable here). `DropAsync("Order")` — the primary-source collection name, which SqlViewTranslator-style name derivation and the ES resolver both produce for unnamed/Auto views — destroys the real collection and every document in it.

#### SH-M372 — MongoViewTranslator translates a Cross join identically to Inner, so no cartesian product is produced

`../Birko.Data.MongoDB.Views/MongoViewTranslator.cs:47`

`preserveNullAndEmptyArrays` is added only for JoinType.LeftOuter, and the $lookup always joins on localField/foreignField. A definition declaring JoinType.Cross therefore produces an equality join, not a cross product — SQL honours Cross via CROSS JOIN (SqlViewTranslator.TranslateJoinType), so the same ViewDefinition returns different row sets on the two backends with no error or warning.

#### SH-M373 — RavenViewStore OnTheFly mode ignores the ViewDefinition and queries a TView collection that need not exist

`../Birko.Data.RavenDB.Views/RavenViewStore.cs:114`

For ViewQueryMode.OnTheFly, BuildQuery returns `session.Query<TView>()` — a dynamic query over the collection Raven infers from TView. Fields, Joins, Aggregates and GroupBy are never consulted and RavenViewTranslator is never invoked, so an OnTheFly view over Order projecting into OrderSummary queries an "OrderSummaries" collection and returns an empty list (or unrelated documents) while reporting success. OnTheFly is the builder's default mode.

#### SH-M374 — RavenViewStore Auto mode queries the static index with no existence check, so it cannot fall back

`../Birko.Data.RavenDB.Views/RavenViewStore.cs:119`

Auto is documented as "try persistent, fall back to on-the-fly", but the branch only checks whether `_indexName` is non-empty and then returns `session.Query<TView>(_indexName)`. If the index does not exist, Raven fails the query (IndexDoesNotExistException) instead of degrading to the dynamic query — MongoViewStore performs an explicit existence probe for the identical mode (CR-M122), so Auto means two different things across backends.

#### SH-M375 — RavenDB Avg reduce uses integer division, truncating the average for integer source columns

`../Birko.Data.RavenDB.Views/RavenViewTranslator.cs:219`

The reduce emits `{View} = g.Sum(x => x.{View}_Sum) / g.Sum(x => x.{View}_Count)`. When the source property is an int/long, both operands are integers and Raven's compiled index performs integer division: an Avg over values 10 and 15 yields 12, not 12.5. AggregateHelper.ComputeAvg returns a double for the same int column, so the Raven view and the LINQ fallback disagree on every non-integral average, silently.

#### SH-M376 — RavenDB join translation ignores RightProperty and JoinType and assumes the left key is a document id

`../Birko.Data.RavenDB.Views/RavenViewTranslator.cs:49`

The map emits `let joined = LoadDocument<Customer>(entity.CustomerGuid)`. A LeftJoin on `(o => o.CustomerGuid, c => c.Guid)` is thus resolved by treating the raw Guid as a Raven document id: LoadDocument returns null, and the select's `joined.Name` then errors or yields null inside the index. JoinType is discarded too, so an Inner join keeps unmatched parents. The definition builds and the index is put successfully — the wrongness only shows up in the index's output.

#### SH-M377 — A Raven map-only index stores no fields, so a Persistent non-aggregate view loses every Select rename

`../Birko.Data.RavenDB.Views/RavenViewTranslator.cs:28`

For a non-aggregate definition the translator returns reduce == null and RavenViewManager puts an IndexDefinition with only `Maps` — no Fields/FieldStorage configuration. RavenViewStore then queries it with `session.Query<TView>(indexName)` and no ProjectInto (RavenViewStore.cs:124/137), so Raven returns the matched SOURCE documents rather than the map's projection; a `Select(o => o.Number, v => v.OrderNumber)` rename never reaches TView.

#### SH-M378 — SqlViewTranslator keys aggregate fields by the SQL function name, so a second SUM on a table is silently dropped

`../Birko.Data.SQL.Views/SqlViewTranslator.cs:150`

FunctionField.Name is the function ("SUM"/"COUNT"/…) and the field is added as `view.AddField(table.Name, table.Type, functionField, functionField.Name)`. View.AddField ignores an add whose key already exists (`if (!table.Fields.ContainsKey(fieldName))`), so `Sum(o.Total → v.Total)` plus `Sum(o.Tax → v.Tax)` keeps only the first: the generated SELECT has one SUM column and TView.Tax stays at its default, with no exception. Non-aggregate fields collide the same way on the source column name (line 99).

#### SH-M379 — A SQL view with no joins cannot be queried or created — the connector requires at least one join

`../Birko.Data.SQL.Views/SqlViewStore.cs:63`

Translate only calls AddJoin inside the `definition.Joins` loop (SqlViewTranslator.cs:156), so a single-source definition (`From<Order>().Select(...)`, the minimal documented shape) produces a View with Join == null. AbstractConnector.CreateSelectCommand then throws ArgumentNullException("view.Join") on every QueryAsync/QueryFirstAsync, and ViewSelectSqlBuilder throws InvalidOperationException("View must have at least one join definition") from EnsureAsync.

#### SH-M380 — SqlViewStore orders by TView property names, but the SQL exposes source column names and "SUM"/"COUNT" aliases

`../Birko.Data.SQL.Views/SqlViewStore.cs:137`

TranslateOrderBy copies `field.PropertyName` (the view property) into the order dictionary, and the connector interpolates the key verbatim (AbstractConnectorBase.cs:558; CreatePersistentViewSelectCommand does the same). The on-the-fly SELECT emits `Table.Column` for plain fields and aliases aggregates as the Fields-dictionary key ("SUM"), so ordering by a renamed field or by any aggregate property is invalid SQL — or, if a same-named column exists on another joined table, sorts the wrong column.

#### SH-M381 — SqlViewStore drops the offset when no limit is supplied, returning page 1 instead of the requested page

`../Birko.Data.SQL.Views/SqlViewStore.cs:63`

Both the on-the-fly and persistent command builders apply paging only inside `if (limit != null)` (AbstractConnectorBase.cs:560, AbstractConnectorBase_View.cs:95), so `QueryAsync(filter, orderBy, limit: null, offset: 20)` silently returns rows from the start of the result set. MongoViewStore emits `$skip` for the same call and CosmosViewStore emits `OFFSET 20 LIMIT 2147483647`, so the same arguments page correctly there and wrongly on SQL.

#### SH-M382 — SqlViewStore.CountAsync counts joined source rows, not view rows, for aggregate/grouped views

`../Birko.Data.SQL.Views/SqlViewStore.cs:98`

It calls SelectCount(view, conditions), whose on-the-fly path is `SelectCount(view.Tables…, view.Join, conditions)` — a COUNT over the joined tables with no GROUP BY (AbstractConnector_SelectViewCount.cs:41). For a view grouping 1,000,000 orders into 12 buckets, CountAsync returns 1,000,000 while QueryAsync returns 12 rows. MongoViewStore.CountAsync counts post-$group documents, so the same contract means different things per backend.

#### SH-M383 — SqlViewStore filters resolve TView property names through LoadTable/LoadView, neither of which knows portable views

`../Birko.Data.SQL.Views/SqlViewStore.cs:51`

DataBase.ParseConditionExpression resolves a column via ResolveColumnName(TView, prop) → LoadTable(TView), which returns null for a TView with no [Table] attribute, then falls back to the ResolveFieldSelectName hook → LoadView(TView), which throws TableAttributeException("No view attributes in type") for a type with no [View] attribute. If the hook was never registered the name stays empty and AbstractConnectorBase throws "Condition name cannot be null or empty". Either way every filtered query fails, and which exception you get depends on unrelated global state.

#### SH-M384 — SQL and MongoDB EnsureAsync never update an existing view, contradicting the "Creates or updates" contract

`../Birko.Data.SQL.Views/SqlViewManager.cs:42`

IViewManager.EnsureAsync is documented as "Creates or updates the persistent view", but SqlViewManager returns as soon as ViewExists/ViewExistsAsync is true and MongoViewManager returns as soon as ExistsAsync is true (MongoViewManager.cs:44). After a ViewDefinition changes (a new column, a changed join), Ensure reports success while every subsequent query keeps reading the stale artifact. RavenViewManager, by contrast, always replaces the index.

#### SH-M385 — CosmosViewManager.EnsureAsync reports success for a Persistent definition without creating anything

`../Birko.Data.CosmosDB.Views/CosmosViewManager.cs:35`

It returns Task.CompletedTask for every mode, including Persistent — no validation of the definition, no name check, no error. A caller that follows the documented lifecycle (EnsureAsync then query) gets a successful ensure and then CosmosViewStore computes everything on the fly against the source container; ExistsAsync(definition.Name) afterwards returns false, so the manager contradicts itself. The other four managers throw InvalidOperationException when a non-OnTheFly definition has no name.

#### SH-M386 — CosmosViewManager.DropAsync deletes a real container with no check that it backs a view

`../Birko.Data.CosmosDB.Views/CosmosViewManager.cs:54`

`_database.GetContainer(viewName).DeleteContainerAsync()` destroys whatever container carries that name, and since Cosmos has no views at all (EnsureAsync is a no-op) no container this class created can ever exist. Passing the primary-source name — e.g. the name derived for an unnamed view — deletes the live data container and all its documents, and the NotFound catch means a wrong-name call is indistinguishable from a successful teardown.

#### SH-M387 — Cosmos filter values are inlined into SQL text with a backslash escape that does not protect the closing quote

`../Birko.Data.CosmosDB.Views/CosmosViewStore.cs:457`

`string s => $"'{s.Replace("'", "\\'")}'"` concatenates the value straight into the query; no QueryDefinition parameter is ever created. A value ending in a backslash ("C:\\") renders as 'C:\' — the escape consumes the terminating quote and the rest of the generated SQL (GROUP BY / ORDER BY / OFFSET) is swallowed into the literal, so an attacker-influenced search term can alter or widen the predicate. Non-listed types fall through to `value.ToString()` unquoted (line 470), emitting invalid SQL for char/TimeSpan.

#### SH-M388 — Cosmos non-aggregate views ignore Fields entirely, so no projection or rename is applied

`../Birko.Data.CosmosDB.Views/CosmosViewStore.cs:92`

QueryLinqAsync/QueryFirstLinqAsync/CountLinqAsync use `_container.GetItemLinqQueryable<TView>()` and never consult `_definition.Fields`; the raw documents are deserialized into TView by the Cosmos serializer's own name matching. `Select(o => o.StatusCode, v => v.Status)` therefore leaves Status at its default, and the filter/order expressions the LINQ path sends use TView names (MapViewPropertyToSource is only wired into the aggregate SQL path), so they target non-existent document fields and match nothing.

#### SH-M389 — Cosmos and ElasticSearch accept join clauses and then ignore them silently

`../Birko.Data.CosmosDB.Views/CosmosViewStore.cs:48`

CosmosViewStore never reads `_definition.Joins` on either path, and ElasticSearchViewStore only documents that it "ignores join clauses" (ElasticSearchViewStore.cs:20). A definition built with `LeftJoin<Order, Customer, Guid>(...)` is accepted by the builder, materialized by these stores as an unjoined single-source query, and returns rows whose joined-source view properties are all default — no NotSupportedException, no warning, and SQL/Mongo/Raven return joined data for the identical definition.

#### SH-M390 — ViewDefinitionBuilder.Build never rejects a GroupBy field that is not also Selected

`../Birko.Data.Views/ViewDefinitionBuilder.cs:175`

Validation checks selected-implies-grouped but not the converse, so `From<Order>().GroupBy(o => o.Status).Sum(...)` with no Select builds fine and then means five different things: SqlViewTranslator throws NotSupportedException, RavenViewTranslator invents a reduce key the map never emitted, Mongo groups by a field it does not project, Cosmos groups by `c.Status`, and ES groups without ever setting a key on TView. The one portable definition has no portable meaning.

### area: workflow-state-machine

#### SH-M391 — State-changed callback exception faults a transition that already succeeded

`../Birko.Workflow/Execution/WorkflowEngine.cs:105`  ·  _restates a first-pass finding_

_onStateChanged is invoked inside the try block, after AddHistoryRecord. If a subscriber throws, the catch at line 109 rolls CurrentState back to fromState and sets Status=Faulted even though the transition completed and its history record is already appended — producing exactly the history/CurrentState inconsistency the CR-M267 rollback exists to prevent, plus a permanently Faulted instance for a successful transition. The notification belongs outside the try, or guarded.

#### SH-M392 — Cancellation during an action is swallowed into WorkflowActionException and permanently faults the instance

`../Birko.Workflow/Execution/WorkflowEngine.cs:109`  ·  _restates a first-pass finding_

The filter is `when (ex is not WorkflowException)`, and the token is passed to every exit/transition/entry action. An OperationCanceledException from a cooperatively-cancelling action is therefore caught, sets Status=Faulted, and is rethrown wrapped — a routine cancellation permanently kills the instance (every later FireAsync throws WorkflowFaultedException) and callers can only tell cancellation from a real fault via InnerException. Contradicts the leading ThrowIfCancellationRequested at line 24.

#### SH-M393 — A WorkflowException from a user action escapes the rollback, leaving state advanced with no history

`../Birko.Workflow/Execution/WorkflowEngine.cs:109`  ·  _restates a first-pass finding_

The filter excludes WorkflowException, presumably to let the engine's own Completed/Faulted throws pass — but those are thrown at lines 33/37, before the try. The only exceptions it can actually exclude are WorkflowExceptions raised by user code (e.g. an OnEntry action firing a trigger on a completed child instance). Such an exception propagates with CurrentState already assigned to ToState (line 86), Status still Active and no history record appended — the CR-M267 inconsistency, still reachable.

#### SH-M394 — Guarded alternative transitions on one trigger are unreachable

`../Birko.Workflow/Execution/WorkflowEngine.cs:40`  ·  _restates a first-pass finding_

FireAsync selects with FirstOrDefault(FromState && Trigger) and, if that candidate's guards deny, returns Denied without considering any later transition with the same trigger. The standard idiom `submit -> Fast when amount<100` declared before an unguarded `submit -> Slow` never reaches Slow: firing with amount=500 returns Denied. Build() emits no warning for a duplicate (trigger, fromState) pair and GetPermittedTriggers (line 143, g.First()) mirrors the limitation rather than fixing it.

#### SH-M395 — Build accepts duplicate state names; the second declaration's actions never run

`../Birko.Workflow/Definition/WorkflowBuilder.cs:64`  ·  _restates a first-pass finding_

stateNames (line 65) is a HashSet used only for validation; `states` keeps every StateBuilder result. Declaring State("Draft") twice builds successfully with two StateDefinition entries, and the engine's FirstOrDefault(s => s.Name == ...) at WorkflowEngine lines 63-64 always picks the first — so OnEntry/OnExit actions declared on the second are silently dead, and IsFinal() on the second never completes the instance (`toStateDef?.IsFinal` reads the first).

#### SH-M396 — Cosmos scopes state/status queries by the constructor's workflow name, not the saved one

`../Birko.Workflow.CosmosDB/CosmosDBWorkflowInstanceStore.cs:91`  ·  _restates a first-pass finding_

FindByStateAsync/FindByStatusAsync add `m.WorkflowName == _workflowName` (the ctor argument) while SaveAsync line 58 overwrites the document's WorkflowName with whatever the caller passed. Saving through the same store under a different name silently makes the instance invisible to that store's state/status queries — and only on this backend. FindByWorkflowNameAsync (line 118) applies no scope at all, so the class is internally inconsistent as well as divergent from its six siblings.

#### SH-M397 — FireAsync has no per-instance mutual exclusion and History is a plain List

`../Birko.Workflow/Execution/WorkflowEngine.cs:86`

FireAsync is a read-then-write claim on the instance (read CurrentState at line 41, evaluate guards, await actions, assign CurrentState at 86, append history at 103) with no lock or CAS. Two concurrent fires on the same WorkflowInstance both pass the status checks, both resolve against the same CurrentState, interleave their awaits and both append — producing a history describing no valid path, and concurrently mutating WorkflowInstance._history (List<T>, line 13). Nothing documents a single-writer requirement.

#### SH-M398 — Find* return a lazy Select on six backends but a materialized list on Cosmos

`../Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs:85`

SQL/JSON/XML/ES/Mongo/Raven all end with `return models.Select(m => m.ToInstance<TData>());` — deferred. ToInstance throws InvalidOperationException for a null Guid or empty payload, so one corrupt or foreign row surfaces its exception inside the consumer's foreach after part of the sequence was yielded, not from the awaited call; re-enumerating re-deserializes every row. CosmosDBWorkflowInstanceStore lines 97/111/124 append .ToList(), so the same contract throws in a different place.

#### SH-M399 — SaveAsync can report success while writing nothing (Guid.Empty / silent no-op update)

`../Birko.Workflow.SQL/SqlWorkflowInstanceStore.cs:58`

The create branch returns whatever CreateAsync returns; AsyncDataBaseStore.CreateCoreAsync:129 and AsyncElasticSearchStore.CreateCoreAsync:105 both `return Guid.Empty` when Connector is null. The update branch returns instance.InstanceId unconditionally and UpdateAsync returns void, so a store built via the store-taking constructor with an unconfigured store completes SaveAsync 'successfully' having persisted nothing. Callers get Guid.Empty or a fabricated success, on every backend.

## Low severity

### area: background-jobs

#### SH-L001 — BackgroundJobProcessor never disposes its linked CTS and Stop() outside RunAsync is a silent no-op

`../Birko.BackgroundJobs/Processing/BackgroundJobProcessor.cs:33`

`_cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` is assigned per RunAsync call and never disposed, so each run leaks a registration on the caller's token (a long-lived host token accumulates them). A second RunAsync overwrites the field, so Stop() only affects the newest loop and the previous one runs forever; Stop() called before RunAsync hits `_cts?.Cancel()` on null and reports nothing.

#### SH-L002 — JobExecutor's typed path never checks that the resolved type implements IJob<TInput>

`../Birko.BackgroundJobs/Processing/JobExecutor.cs:65`

The parameterless branch guards with `job is IJob` (84) and fails cleanly otherwise, but the typed branch only does `jobType.GetMethod("ExecuteAsync", new[]{ inputType, typeof(JobContext), typeof(CancellationToken) })` and invokes whatever it finds. Any type with a coincidentally matching signature is constructed via the factory and invoked, and JobType/InputType come from persisted storage rather than from code, so a tampered or hand-written job row selects the type to instantiate with no interface check.

#### SH-L003 — JobExecutor unconditionally reports the inner exception, hiding the thrown exception's own message

`../Birko.BackgroundJobs/Processing/JobExecutor.cs:100`

`var innerEx = ex.InnerException ?? ex;` is applied to every exception, not just TargetInvocationException. A job that throws its own wrapper (`throw new ImportFailedException("file 3 of 40 rejected", ioEx)`) has its message discarded and only the inner IOException text is persisted as LastError and surfaced in JobResult.Error, losing the context the wrapper existed to carry.

#### SH-L004 — RecurringJobScheduler ignores sub-second intervals and accepts zero/negative ones without validation

`../Birko.BackgroundJobs/Processing/RecurringJobScheduler.cs:82`

The loop sleeps a hard-coded `TimeSpan.FromSeconds(1)` and enqueues each due definition at most once per iteration, so Register(name, TimeSpan.FromMilliseconds(100)) fires ~1x/s, not 10x/s, with no error or warning. Register (31-43) validates neither name nor interval: TimeSpan.Zero or a negative interval sets NextRunAt at or before now, so the job is enqueued on every tick forever.

#### SH-L005 — RecurringJobScheduler advances NextRunAt from the observed time, so every schedule drifts

`../Birko.BackgroundJobs/Processing/RecurringJobScheduler.cs:75`

`def.NextRunAt = now.Add(def.Interval)` uses the loop's observed `now` rather than the previous NextRunAt, so each firing adds the up-to-1s tick latency (plus the awaited EnqueueAsync duration) to the period. A '5 minutes' job slips a few seconds per run, so a job registered to align with a wall-clock time walks away from it indefinitely. The mutation also writes a shared definition object with no synchronization.

#### SH-L006 — InMemoryJobQueue.EnqueueAsync silently overwrites an existing job with the same Id

`../Birko.BackgroundJobs/Processing/InMemoryJobQueue.cs:35`

`_jobs[descriptor.Id] = descriptor` is an upsert, so enqueuing a descriptor whose Id already exists (a caller retrying an enqueue after a timeout, or re-submitting one fetched from GetAsync) destroys the stored job's status/attempt history and returns success. Every store-backed backend inserts instead: AbstractAsyncJsonStore.CreateCoreAsync does `_items.Add(...)` which throws on a duplicate key, and a SQL insert violates the primary key.

#### SH-L007 — InMemoryJobQueue returns live descriptor references, so callers can mutate queue state

`../Birko.BackgroundJobs/Processing/InMemoryJobQueue.cs:120`

GetAsync (119-120), GetByStatusAsync (125-129) and DequeueAsync (62) hand back the same JobDescriptor instances stored in `_jobs`. Every other backend materialises a fresh descriptor via ToDescriptor(). A consumer that adjusts a returned descriptor (Status, MaxRetries, Metadata) as if it were a snapshot silently rewrites the queue's state, so behaviour verified against the reference in-memory queue does not carry over to any persistent backend.

#### SH-L008 — The dequeue SemaphoreSlim is never disposed because the queue types are not disposable

`../Birko.BackgroundJobs/Processing/InMemoryJobQueue.cs:19`

InMemoryJobQueue (19), JsonJobQueue (27) and XmlJobQueue (28) each own a `new SemaphoreSlim(1, 1)` but none implements IDisposable, so the semaphore's internal wait handle is never released. Any consumer that creates queues per scope or per tenant leaks one per instance; RedisJobQueue is disposable, so the family is inconsistent here too.

#### SH-L009 — JobQueueOptions.RetryPolicy is never read by anything

`../Birko.BackgroundJobs/Core/JobQueueOptions.cs:29`

Documented as 'Default retry policy for jobs that don't specify their own', but a grep across the whole framework shows BackgroundJobProcessor reads only PollingInterval, MaxConcurrency, DefaultQueueName and JobTimeout. Each queue takes its own RetryPolicy? constructor argument, so configuring JobQueueOptions.RetryPolicy - the obvious place, given the type name - has no effect at all on backoff or on the retry budget.

#### SH-L010 — JobQueueOptions.RetentionPeriod is never read and PurgeAsync is never called

`../Birko.BackgroundJobs/Core/JobQueueOptions.cs:45`

RetentionPeriod is documented as 'How long to keep completed/dead jobs before they are eligible for purging', but no file in the family reads it and nothing calls IJobQueue.PurgeAsync. An operator who sets RetentionPeriod reasonably expects retention to be enforced; instead terminal jobs accumulate without bound in every backend until a consumer writes its own purge loop.

#### SH-L011 — Contracts RetryPolicy shares one unsynchronized System.Random for jitter

`../Birko.Contracts/Retry/RetryPolicy.cs:11`

`private static readonly System.Random _jitterRandom = new();` is used from GetDelay (73) with no lock. System.Random is documented as not thread-safe; concurrent NextDouble() calls corrupt its internal state and can make it return 0 indefinitely, collapsing the jitter factor exactly when many callers retry at once - the thundering herd AddJitter exists to prevent. Random.Shared or a [ThreadStatic] instance is the fix.

#### SH-L012 — BackgroundJobs.RetryPolicy does not clamp attemptNumber and its BaseDelay doc contradicts the formula

`../Birko.BackgroundJobs/Core/RetryPolicy.cs:46`

`BaseDelay.Ticks * Math.Pow(2, attemptNumber - 1)` with attemptNumber = 0 gives half of BaseDelay and with -3 gives BaseDelay/16, while the otherwise-parallel Birko.RetryPolicy clamps to 1 for exactly this reason (CR-L095). Any FailAsync reached with AttemptCount == 0 (Cosmos double-dispatch, or a descriptor failed without being dequeued) schedules a shorter-than-configured retry. The BaseDelay doc at line 16 also states 'Multiplied by the attempt number', which is not what the body computes.

#### SH-L013 — JobDescriptor.Delay is documented as controlling eligibility but no backend persists or honours it

`../Birko.BackgroundJobs/Core/JobDescriptor.cs:49`

Delay is documented as 'Delay before the job becomes eligible for processing' and ScheduledAt as 'EnqueuedAt + Delay', but no backing model has a Delay column/field and no queue derives ScheduledAt or Status from it. `new JobDescriptor { Delay = TimeSpan.FromHours(1) }` passed straight to EnqueueAsync runs immediately on every backend and the delay is silently dropped on the first round trip. Only JobDispatcher.ScheduleAsync resolves it, for its own callers.

#### SH-L014 — JobDispatcher typed enqueue accepts a null input and produces a job that can only ever fail

`../Birko.BackgroundJobs/Processing/JobDispatcher.cs:48`

EnqueueAsync<TJob, TInput>(input) and ScheduleAsync<TJob, TInput>(input, delay) call `_serializer.Serialize(input)` with no null check. A null input serializes to the string "null", so the descriptor is stored with a non-null SerializedInput and InputType; at execution JobExecutor deserializes it to null and returns 'Failed to deserialize job input' (JobExecutor.cs:61) on every attempt until the job goes Dead. The error surfaces minutes later in a worker rather than at the call site.

#### SH-L015 — Redis DeserializeDescriptor reads required fields with the indexer and throws on a partial hash

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:452`

Id, JobType, Status, Priority, MaxRetries, AttemptCount and EnqueuedAt are read as `dict["..."]` while every optional field uses TryGetValue. A hash written by an older or partial enqueue (see the non-atomic enqueue finding), or truncated by an unrelated EXPIRE, makes DeserializeDescriptor throw KeyNotFoundException - and it is on the GetAsync, DequeueAsync, FailAsync and GetByStatusAsync paths, so one bad hash breaks GetByStatusAsync for the entire status set.

#### SH-L016 — A Redis queue member whose job hash is gone aborts the whole dequeue poll

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:48`

The Lua script takes the single lowest-scored member, ZREMs it, and `if redis.call('EXISTS', jobKey) == 0 then return nil end`. An orphan member (hash purged or expired while the member survived) therefore makes DequeueAsync return null even though other due jobs exist, so the processor sleeps a full PollingInterval per orphan instead of moving to the next candidate. The same early return also skips the status-set cleanup for that id.

#### SH-L017 — Redis GetByStatusAsync issues one HGETALL per member of the entire status set

`../Birko.BackgroundJobs.Redis/RedisJobQueue.cs:287`

SetMembersAsync pulls every id in `{prefix}:status:{n}` and the loop does a separate awaited HashGetAllAsync per id before ordering and applying `limit`. GetByStatusAsync(Completed, 10) against a status set that has accumulated 100k completed jobs (which it will, since nothing calls PurgeAsync) performs 100k sequential round trips to return 10 descriptors. Every other backend pushes the limit into the query.

#### SH-L018 — Store-backed PurgeAsync materialises every expired row before deleting

`../Birko.BackgroundJobs.SQL/SqlJobQueue.cs:213`

PurgeAsync reads all matching rows with no limit and no batching, then passes the whole list to DeleteAsync (SQL:213, Mongo:198, ES:199, Raven:203, JSON:196, XML:193, Cosmos:158). On a queue that has run untended - and nothing calls PurgeAsync automatically - the first purge loads potentially millions of descriptors into memory. The bulk stores expose Delete(filter), which these same backends already use for native deletes elsewhere.

#### SH-L019 — Cosmos PurgeAsync enumerates the read result twice

`../Birko.BackgroundJobs.CosmosDB/CosmosDBJobQueue.cs:164`

`var count = results.Count();` followed by `await _store.DeleteAsync(results, ct)` enumerates the returned IEnumerable a second time. If the store returns a lazily evaluated sequence the Cosmos query is executed twice (double RU charge) and the reported count can disagree with the set actually deleted. The SQL/Mongo/ES/Raven/JSON/XML siblings all call ToList() once and use list.Count.

#### SH-L020 — SqlJobLockProvider ignores the timeout on PostgreSQL

`../Birko.BackgroundJobs.SQL/SqlJobLockProvider.cs:71`

The PostgreSQL branch issues `pg_try_advisory_lock`, which returns immediately, so TryAcquireAsync("jobs", TimeSpan.FromMinutes(1)) returns false the instant another session holds the lock; `timeout` only shapes cmd.CommandTimeout. The MSSql and MySQL branches genuinely wait for it. Callers written against the documented acquire semantics get a spuriously contended result on PostgreSQL under normal contention.

#### SH-L021 — SqlJobLockProvider hashes lock names to 63 bits, so distinct names can share one Postgres lock

`../Birko.BackgroundJobs.SQL/SqlJobLockProvider.cs:183`

GetLockKey folds the name with djb2 and masks the sign bit, and PostgreSQL locks on that number. Two lock names that collide serialise against each other invisibly (one worker blocks on a lock it never asked for), and combined with ReleaseAsync building the key from the caller-supplied name, releasing "queue-b" can unlock a colliding "queue-a". MSSql and MySQL pass the name through verbatim, so the collision behaviour differs by dialect.

#### SH-L022 — SqlJobLockProvider picks its dialect from a connector type-name substring

`../Birko.BackgroundJobs.SQL/SqlJobLockProvider.cs:160`

Dialect matches typeof(DB).Name against 'PostgreSQL'/'Postgres', 'MSSql'/'SqlServer' and 'MySQL', defaulting to Other. Any wrapped, renamed or consumer-supplied connector (a decorating TenantPostgresDbConnector, or MariaDbConnector) silently resolves to Other, at which point TryAcquireAsync returns false without opening a connection - indistinguishable from genuine contention, so callers cannot tell 'not supported here' from 'someone else holds it'.

#### SH-L023 — JsonJobQueue and XmlJobQueue take an IDateTimeProvider but never stamp EnqueuedAt from it

`../Birko.BackgroundJobs.JSON/JsonJobQueue.cs:55`

Both queues require a non-null clock and use it for eligibility, LastAttemptAt, CompletedAt, retry ScheduledAt and the purge cutoff, but EnqueueAsync copies the descriptor's EnqueuedAt straight through and JobDescriptor defaults it to the real DateTime.UtcNow. InMemoryJobQueue restamps it (line 34, CR-L017) precisely so the ThenBy(EnqueuedAt) ordering is deterministic under a test clock; on JSON/XML a TestDateTimeProvider cannot control dequeue order, so equal-priority FIFO behaviour stays wall-clock dependent.

#### SH-L024 — XmlJobDescriptorModel's design comment names types that do not exist

`../Birko.BackgroundJobs.XML/Models/XmlJobDescriptorModel.cs:108`

The CR-L033 comment justifies dropping Delay by asserting 'the enqueue pipeline (JobProcessor/JobScheduler) resolves Delay into ScheduledAt + Status=Scheduled before EnqueueAsync'. There is no JobProcessor or JobScheduler type; the resolver is JobDispatcher.ScheduleAsync, and RecurringJobScheduler never sets Delay at all. A reader looking for that guarantee finds nothing, and nothing rejects the 'not a supported path' raw Delay-only descriptor the comment describes.

### area: bulk-filter-operations

#### SH-L025 — Aggregate/AggregateAsync are non-virtual with no *Core hook, contradicting the class's template-method doc

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:166`

The class summary states 'Uses Template Method pattern - concrete stores override *Core methods', and every other operation has a virtual *Core. Aggregate (166) and AsyncDataBaseBulkStore.AggregateAsync (223) are non-virtual with the connector call inlined, so a provider needing different GROUP BY handling (e.g. TimescaleDB time_bucket) cannot override anything - only re-declare with `new`, which dispatch through IAggregatableStore<T> will not see.

#### SH-L026 — SQL bulk stores make the public collection methods non-virtual while the abstract bases make them virtual

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:33`

Read(filter,...) 33, Create(IEnumerable) 66, Update(IEnumerable) 84 and Delete(IEnumerable) 134 are non-virtual, while Read() 48 and ReadFirst 54 in the same class are virtual, and AbstractBulkStore declares all of them virtual (19/31/43/56/85). Async mirrors it (35/93/117/182 vs 75/81). A decorator or test double deriving from the SQL store cannot intercept the collection entry points on one hierarchy and can on the other, for one IBulkStore<T> contract.

#### SH-L027 — Sync store's Connector is annotated non-nullable yet null-checked here; the async store declares it nullable

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:116`

DataBaseStore declares `public DB Connector { get; protected set; } = null!` (DataBaseStore.cs:29) - a non-nullable annotation backed by a null suppression - while AsyncDataBaseStore declares `DB?` (line 31). The bulk store null-checks it at 116/155/169 and uses `Connector?.` at 44, so the code contradicts its own annotation, and a consumer writing `store.Connector.Insert(...)` gets no nullable diagnostic for exactly the unconfigured state these guards exist for.

#### SH-L028 — table.Name is dereferenced from LoadTable, which returns a null-suppressed value

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:118`

DataBase.LoadTable returns `table!` and ComputeTable returns null when the type carries no [Table]/[TableAttribute] (DataBase_Table.cs:60-79). Update(filter, PropertyUpdate) uses it unguarded at line 130 (`table.Name`), as does AsyncDataBaseBulkStore.cs:163/176/178, so an unattributed model fails with a NullReferenceException from inside the store instead of the TableAttributeException LoadTables raises for the same mistake.

#### SH-L029 — No null guards on the updates/filter arguments of the filter-based write overloads

`../Birko.Data.SQL/Stores/DataBaseBulkStore.cs:116`

`updates.Assignments.Count` dereferences updates with no check, so Update(filter, null!) throws NullReferenceException after EnsureInitialized has already provisioned the table (same at AsyncDataBaseBulkStore.cs:161). The portable path is worse: AbstractBulkStore.cs:70 captures `updates` in a lambda, so the NRE surfaces per-entity inside the read-modify-save loop, after the read. Neither throws ArgumentNullException at the entry point.

#### SH-L030 — ApplyTo throws InvalidCastException for a non-member selector

`../Birko.Data.Stores/PropertyUpdate.cs:42`  ·  _restates a first-pass finding_

`(MemberExpression)property.Body` is an unchecked cast, so `Set(x => x.Compute(), v)` - accepted at compile time because Set takes any Expression<Func<T,TProperty>> - fails at update time with InvalidCastException from deep inside the store rather than at the Set() call site with a meaningful ArgumentException. Set (line 25) validates nothing, not even a null property expression. Re-confirmed as reported.

#### SH-L031 — AsyncDataBaseBulkStore omits ConfigureAwait(false) on every await after the init gate

`../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs:43`  ·  _restates a first-pass finding_

EnsureInitializedAsync is awaited with ConfigureAwait(false) but the following `await ReadCoreAsync(...)` is not (same at 77, 99, 112, 123, 136, 146, 150, 176, 178, 187, 199, 211, 213, 232), while AbstractAsyncBulkStore uses ConfigureAwait(false) throughout. Under a synchronization context every continuation hops back to the captured context - the classic sync-over-async deadlock and per-item throughput hazard. Re-confirmed as reported.

#### SH-L032 — Dead null check: Connector.Select is an iterator method and can never return null

`../Birko.Data.SQL/Stores/AsyncDataBaseBulkStore.cs:69`

`if (results == null) return Enumerable.Empty<T>();` guards the result of `Task.Run(() => Connector!.Select(...))`. Every Select overload in AbstractConnector_Select.cs is a `yield return` iterator, so it returns a non-null enumerable even for a null table set (it yield-breaks). The branch is unreachable and misrepresents 'no rows' as a possible null, as does the sync ReadCore's `?? Enumerable.Empty<T>()` (DataBaseBulkStore.cs:44).

#### SH-L033 — Filter-based update and portable delete materialise the whole match set with ToList and no concurrency guard

`../Birko.Data.Stores/AbstractBulkStore.cs:76`

`Read(filter, null, null, null).ToList()` (76, 99; AbstractAsyncBulkStore 117, 146; DataBaseBulkStore 104; AsyncDataBaseBulkStore 146) loads every matching entity into memory with no limit, batching or streaming, so a filter matching a large table is bounded only by RAM. It is also a read-then-write with no version check: a row modified by another writer between the read and `Update(item)` is overwritten wholesale.

#### SH-L034 — Bulk Read overloads omit the orderBy parameter from their XML docs

`../Birko.Data.Stores/IBulkStore.cs:23`

The doc block for `Read(filter, orderBy, limit, offset)` documents filter, limit and offset but not orderBy (IBulkStore.cs:23-29); IAsyncBulkStore.cs:25-33 has the same gap. With <GenerateDocumentationFile> in a consumer aggregator this is CS1573 on the primary bulk-read contract, and it leaves the one parameter whose semantics are genuinely ambiguous (property name vs column name) undocumented.

#### SH-L035 — PropertyUpdate's Assignments and ApplyTo are internal, so an out-of-assembly store cannot translate or apply it

`../Birko.Data.Stores/PropertyUpdate.cs:16`

The class doc says 'Platforms can translate these to native operations (SQL SET, MongoDB $set, etc.)', but Assignments (16) and ApplyTo (36) are `internal`, reaching only stores compiled into the same assembly as Birko.Data.Stores. The documented consumer pattern allows splitting aggregators, and a store in a second aggregator sees a PropertyUpdate<T> it can neither read nor apply, making Update(filter, updates) unimplementable there.

#### SH-L036 — ApplyTo throws on a getter-only property while the SQL path writes the column successfully

`../Birko.Data.Stores/PropertyUpdate.cs:45`

`prop?.SetValue(entity, value)` throws ArgumentException ('Property set method not found') for a property with no accessible setter, whereas the SQL native path resolves it through LoadField and emits a valid `SET Col = @SETCol`. The same PropertyUpdate succeeds on SQL and throws on every portable backend - the mirror image of the non-PropertyInfo case, which throws on SQL and no-ops portably.

#### SH-L037 — ReadFirst routes through a different *Core than the bulk read and cannot express an ordering

`../Birko.Data.Stores/AbstractBulkStore.cs:52`

ReadFirst calls base.Read(filter), i.e. the single-result ReadCore(filter) - a separate abstract member from ReadCore(filter, orderBy, limit, offset). A concrete store must implement both and nothing keeps them consistent (soft-delete handling, tenant scoping, join set). The doc promises 'the first entity matching', but with no ORDER BY the row is whatever the backend yields first and the API cannot pass an order. Same at AbstractAsyncBulkStore.cs:81, DataBaseBulkStore.cs:58, AsyncDataBaseBulkStore.cs:85.

### area: caching

#### SH-L038 — GetL1Options(null) returns L1DefaultExpiration uncapped by L1MaxExpiration

`../Birko.Caching.Hybrid/HybridCache.cs:257`  ·  _restates a first-pass finding_

The `requested == null` branch returns CacheEntryOptions.Absolute(_options.L1DefaultExpiration) before the maxExpiry cap is applied. If a consumer sets L1DefaultExpiration > L1MaxExpiration, the GetAsync L2-backfill path (which always passes null) creates L1 entries longer-lived than the documented hard cap — the staleness bound L1MaxExpiration exists to enforce.

#### SH-L039 — CachePriority.NeverRemove is honoured by the sweep but ignored by the read path

`../Birko.Caching/Memory/MemoryCache.cs:45`  ·  _restates a first-pass finding_

EvictExpired() skips entries with Priority == NeverRemove (line 178), but GetAsync (45-49) and ExistsAsync (85-89) call _entries.TryRemove for any expired entry without consulting Priority. A NeverRemove entry survives the timer yet is deleted by the first read after its window elapses — the two eviction paths disagree and NeverRemove buys nothing observable.

#### SH-L040 — CachePriority.Low/Normal/High are dead configuration and the doc promises memory-pressure eviction

`../Birko.Caching/Core/CacheEntryOptions.cs:29`

Priority is read in exactly one place (MemoryCache.EvictExpired, and only the NeverRemove comparison). Low/Normal/High are never read by any backend, and no backend implements any size limit or memory-pressure eviction at all, yet the XML doc states 'Eviction priority when cache is under memory pressure'. RedisCache and HybridCache's L2 path never see Priority; HybridCache.GetL1Options only copies it forward.

#### SH-L041 — Non-positive TTLs are unvalidated and diverge between backends

`../Birko.Caching/Core/CacheEntryOptions.cs:33`

Absolute(TimeSpan.Zero) / a negative window is accepted by the factories and by every backend. MemoryCache stores an entry whose IsExpired() is true from the next tick (SetAsync reports success but the value is unreachable — a silent no-op write), whereas RedisCache passes it as the StringSetAsync expiry and Redis rejects a non-positive PX/EX with a server error. Same call, success on one backend and an exception on the other.

#### SH-L042 — Expired-entry removal uses TryRemove(key) instead of the key/value overload, discarding fresh writes

`../Birko.Caching/Memory/MemoryCache.cs:47`

GetAsync (47), ExistsAsync (87) and EvictExpired (179) all call _entries.TryRemove(key, out _) after deciding the entry they OBSERVED is expired. If another thread completes SetAsync for that key in between, the reader/sweeper deletes the brand-new entry. The per-key lock map uses the correct TryRemove(KeyValuePair) pair overload (line 148) — the entry map does not. Observable as a GetOrSetAsync whose factory result vanishes immediately.

#### SH-L043 — No cache operation guards _disposed, and a disposed MemoryCache grows without bound

`../Birko.Caching/Memory/MemoryCache.cs:188`

Dispose() sets _disposed and clears the maps, but GetAsync/SetAsync/GetOrSetAsync never check it. A disposed MemoryCache keeps accepting writes while its cleanup timer is gone, so expired entries are only removed when someone happens to read that exact key — unbounded growth with no error. RedisCache/HybridCache are the same (and their _disposed flags are plain bools, not volatile like MemoryCache's, so concurrent Dispose can double-dispose the owned RedisConnectionManager).

#### SH-L044 — Dispose disposes semaphores still held by in-flight GetOrSetAsync callers

`../Birko.Caching/Memory/MemoryCache.cs:194`

Dispose() disposes every KeyLock semaphore and clears _locks regardless of RefCount/Refs. A caller currently inside its critical section then hits ObjectDisposedException on keyLock.Semaphore.Release() in its finally (line 118), which masks the factory's result and skips ReleaseKeyLock. HybridCache.Dispose (lines 290-295) has the identical hazard against its RefCount>0 locks.

#### SH-L045 — CacheSerializer suppresses a genuinely-null value into a ThrowIfNull parameter

`../Birko.Caching/Serialization/CacheSerializer.cs:24`

Serialize<T>(T value) forwards `value!` to ISerializer.SerializeToBytes, whose first statement is ArgumentNullException.ThrowIfNull. The `!` asserts non-null for exactly the input the ICache contract permits (MemoryCache stores nulls as hits), converting a supported cache operation into an ArgumentNullException at the Redis backend. SerializeToString (line 34) does the same; Deserialize returns T? which RedisCache.cs:64 re-suppresses with `!` into CacheResult<T>.Hit.

#### SH-L046 — CacheSerializer documents pluggable ISerializer support that cannot be plugged in

`../Birko.Caching/Serialization/CacheSerializer.cs:15`

The class doc says it 'Delegates to ISerializer for pluggable serialization format support', but DefaultSerializer is a private static readonly SystemJsonSerializer with no setter, constructor, factory or configuration hook, and every member is static. No consumer can substitute a format — the abstraction is dead and the doc claim is false.

#### SH-L047 — GetOrSetAsync returns a cached null through a non-nullable Task<T>

`../Birko.Caching/Memory/MemoryCache.cs:99`

For a cached null, GetAsync returns HasValue==true with Value==null and GetOrSetAsync returns `result.Value!` (lines 99 and 110). The declared return is Task<T>, so a caller writing `Foo f = await cache.GetOrSetAsync<Foo>(...)` receives null with no nullable-flow warning and NREs later; the factory is also never consulted. HybridCache repeats this at lines 158 and 167.

#### SH-L048 — RedisCache's SET NX lock has no fencing token, so a slow holder deletes another caller's lock

`../Birko.Caching.Redis/RedisCache.cs:147`

The lock is taken with a fixed 30-second TTL and released with `KeyDeleteAsync(lockKey)` whenever lockAcquired is true, without checking that the value "1" is still the one this caller wrote. A factory that runs longer than 30 s lets the lock expire, a second caller acquires it, and then the first caller's finally deletes the second caller's lock — collapsing stampede protection precisely under the slow-factory conditions it exists for.

#### SH-L049 — The always-observe-L1 fix exists only in SetAsync; Remove/RemoveByPrefix/Clear leave l1Task unobserved

`../Birko.Caching.Hybrid/HybridCache.cs:130`

SetAsync deliberately awaits l1Task in a finally (CR-M031). RemoveAsync (125-135), RemoveByPrefixAsync (219-232) and ClearAsync (234-247) instead start l1Task, then await Task.WhenAll inside a try whose only catch is `when (FallbackToL1OnL2Failure)`. With that flag false, an L2 failure propagates without l1Task ever being awaited — the same unobserved-faulted-task hazard, and the caller cannot tell whether L1 was cleared. A synchronous throw from _l2.RemoveAsync before l2Task is assigned has the same effect.

#### SH-L050 — HybridCache's L2 fallback filter also covers the L1 backfill write in GetOrSetAsync

`../Birko.Caching.Hybrid/HybridCache.cs:170`

The try at lines 161-173 wraps both `_l2.GetAsync` and the `_l1.SetAsync` backfill (line 166). An L1 write failure is therefore swallowed by `catch when (FallbackToL1OnL2Failure)` and execution falls through to the stampede lock and the factory, so the factory runs and overwrites a value that L2 had already returned successfully — an L1 fault is misattributed to L2 and duplicate work is silently performed.

#### SH-L051 — HybridCache.cs compiles only under ImplicitUsings (Dictionary with no System.Collections.Generic import)

`../Birko.Caching.Hybrid/HybridCache.cs:2`

Line 18 declares `Dictionary<string, KeyLock>` but the file imports System, System.Collections.Concurrent (which it never uses), System.Threading and System.Threading.Tasks — not System.Collections.Generic. Every consumer today happens to set ImplicitUsings=enable, so it builds; a consumer aggregator without it fails to compile this shared project. Every other file in the area declares its usings explicitly.

#### SH-L052 — Expiry is computed from wall-clock DateTime.UtcNow, not the framework's IDateTimeProvider

`../Birko.Caching/Memory/MemoryCacheEntry.cs:22`

IsExpired() and the CreatedAt/LastAccessedAt stamps read DateTime.UtcNow directly, so a backwards clock adjustment (NTP step) silently extends every entry's lifetime past its TTL, and expiry cannot be driven deterministically even though Birko.Time.Abstractions.IDateTimeProvider is the framework's stated abstraction for exactly this (used by the Birko.Data.Patterns timestamp/audit decorators). LastAccessedAt is also a non-volatile DateTime written by readers (MemoryCache.cs:51) and read by the sweep with no synchronisation, so on a 32-bit runtime the sweep can observe a torn value.

#### SH-L053 — BuildKey keys on table name only, so distinct entity types sharing a table name share entries

`../Birko.Data.SQL.Caching/Caching/SqlCacheKeyBuilder.cs:31`

The key format has no component identifying T. Two entity types mapped to the same table, or two unmapped types in different namespaces both falling back to typeof(T).Name (CachedAsyncDataBaseBulkStore.cs:195, e.g. Sales.Order and Logistics.Order), produce identical keys over one shared ICache — so a read for one type is served the other type's payload (Miss/churn on MemoryCache, JsonException or a wrongly-shaped object on Redis).

#### SH-L054 — Enabled=false suppresses invalidation, so a shared cache is left stale by that store's writes

`../Birko.Data.SQL.Caching/Caching/SqlCacheOptions.cs:21`

Enabled is a settable property and InvalidateCacheAsync returns before computing a prefix when it is false (CachedAsyncDataBaseBulkStore.cs:172). A second store over the same ICache and table with Enabled=true therefore keeps serving cached rows that the disabled store has already changed or deleted, until DefaultExpiration. Disabling the cache is documented as 'delegates directly to the base store', which reads as safe.

### area: data-sync

#### SH-L055 — Cancellation is reported as a per-item sync failure, and differently in Sync vs Preview

`../Birko.Data.Sync/AsyncSyncProvider.cs:347`

Store calls receive `options.CancellationToken`, so cancelling mid-item throws OperationCanceledException inside the per-item try and is recorded as a SyncError "Failed to sync item {guid}" with Operation = the action name, indistinguishable from a real store failure. The outer catch (line 228) likewise turns OperationCanceledException into a "Sync failed" error, while PreviewAsync deliberately rethrows it (line 90) — two entry points with opposite cancellation contracts.

#### SH-L056 — SaveFilterBlockAction.ThrowException is documented as "fail sync" but only fails one item

`../Birko.Data.Sync/Models/SyncFilterOptions.cs:61`

The enum member's doc says "Throw exception, fail sync". HandleSaveFilterBlock (SyncProviderBase.cs:304) throws InvalidOperationException from inside CanSaveToLocal/CanSaveToRemote, which is called inside ProcessBatch's per-item try, so the exception is caught at SyncProvider.cs:346, recorded as one item error, and the run continues writing every remaining item. Nothing aborts.

#### SH-L057 — OnProgress always reports zero counters and a zero total during change detection

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:402`

ReportProgress constructs a fresh `new SyncProgress { Phase, TotalItems, ProcessedItems }`, never passing the provider's accumulating `progress` object, so CreatedItems/UpdatedItems/DeletedItems/SkippedItems/Conflicts/Errors are always 0 in the callback. The DetectingChanges and Failed calls pass totalItems = 0 (SyncProvider.cs:118, :238), so PercentComplete is 0 for the whole detection phase — a caller cannot render meaningful progress.

#### SH-L058 — progress.TotalItems double-counts entities present on both sides

`../Birko.Data.Sync/SyncProvider.cs:139`

`progress.TotalItems = localDict.Count + remoteDict.Count` counts a Guid present on both sides twice, so for a fully overlapping sync of 10 entities the internal total is 20 and `SyncProgress.PercentComplete` caps at 50%. The value handed to OnProgress during ApplyingChanges is a different number (`allGuids.Count`, line 178), so the two totals disagree within one run. Same at AsyncSyncProvider.cs:140.

#### SH-L059 — ISyncKnowledgeItem.Metadata is never populated by any backend and never read

`../Birko.Data.Sync/Models/ISyncKnowledgeItem.cs:54`

All seven CreateKnowledgeItem implementations (Sql, AsyncSql, Json, Xml, Mongo, Elastic) omit Metadata, and no code path in the capability reads it. It is persisted as null in every knowledge row of every backend, plus a mapped column/field in the SQL, ES, Mongo, JSON and XML schemas — plumbed and documented, wired to nothing.

#### SH-L060 — SyncOptions.SkipPreview is a dead option

`../Birko.Data.Sync/Models/SyncOptions.cs:79`

No code in SyncProvider, AsyncSyncProvider or SyncProviderBase reads SkipPreview; its own XML doc admits it is "not consulted by the provider". A caller setting it observes no behavioural difference, which the doc-comment reframes as intent rather than removing the surface.

#### SH-L061 — filterOptions and cancellationToken parameters are accepted and ignored

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:44`

DetermineSyncAction takes `SyncFilterOptions<T>? filterOptions` and never reads it; AnalyzeItem takes it only to forward it there. `SyncProvider.GetAllItems` (SyncProvider.cs:417) takes a CancellationToken and calls `store.Read(filter, null, null, null)` without it, so an already-cancelled token still performs both full store reads.

#### SH-L062 — AnalyzeItem computes three locals it never uses

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:227`

`localExists`, `remoteExists` and `hasKnowledge` are assigned from the TryGetValue calls and never referenced; only the out-values are used. Dead code that suggests preview-specific logic which does not exist.

#### SH-L063 — Dead branch in DetermineSyncAction's initial-sync arm

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:54`

`if (!remoteExists && !localExists) return (SyncAction.Skip, ...);` is immediately followed by the identical unconditional `return (SyncAction.Skip, ...)` on line 56, so the test can never change the outcome.

#### SH-L064 — GetUpdatedAt re-reflects on every call while the Guid property is cached

`../Birko.Data.Sync/Internal/SyncProviderBase.cs:376`

`typeof(T).GetProperty("UpdatedAt")` runs on every invocation — up to four times per item (GetNewest twice, GetVersionHash twice) — whereas the Guid PropertyInfo is resolved once in the constructor (line 20). Same class, two opposite caching decisions on the same kind of lookup.

#### SH-L065 — SyncQueue's two-argument GetQueueKey overload is never invoked

`../Birko.Data.Sync/SyncQueue.cs:134`

`EnqueueAsync` (line 42) and `GetQueueLength` (line 159) both call the one-argument `GetQueueKey(scope)`. Nothing in the class calls `GetQueueKey(string, Guid?)`, so a subclass that overrides the tenant-aware overload to isolate tenants gets no isolation — its key is never used for enqueueing or for the queue-length lookup.

#### SH-L066 — SyncQueue never disposes its SemaphoreSlim and Clear() discards in-flight bookkeeping

`../Birko.Data.Sync/SyncQueue.cs:31`

The SemaphoreSlim allocated in the constructor is never disposed and SyncQueue does not implement IDisposable. `Clear()` (line 183) empties `_queues` including records for operations currently awaiting or running, after which their DequeueNext returns null and the "Shouldn't reach here" fallback (line 80) runs the operation with no tracking. DequeueNext also pops the oldest record for the key, not the caller's own, so timings are attributed to the wrong QueuedSync.

#### SH-L067 — SQL GetLastSyncTime executes its SELECT twice

`../Birko.Data.Sync.Sql/Stores/SqlSyncKnowledgeStore.cs:26`

`items?.Any() == true ? items.Max(x => (DateTime?)x.LastSyncedAt) : null` enumerates the lazy connector sequence twice, so two full SELECTs over the scope run for one call. Every sync and preview begins with this call.

#### SH-L068 — Last-sync stamping is one write per row in four backends and one bulk write in two

`../Birko.Data.Sync.ElasticSearch/Stores/AsyncElasticSyncKnowledgeStore.cs:33`

AsyncElasticSyncKnowledgeStore, AsyncMongoSyncKnowledgeStore (33), SqlSyncKnowledgeStore (50) and AsyncSqlSyncKnowledgeStore (54) issue an Update per row inside the loop, while AsyncJsonSyncKnowledgeStore (59) and AsyncXmlSyncKnowledgeStore (37) issue a single bulk `UpdateAsync(items, ...)` with a `Count > 0` guard. All six stores expose the same bulk overload, so the divergence is unmotivated: a 10k-row scope costs 10k round trips on ES/Mongo/SQL and one on JSON/XML, and the four per-row loops have no empty-collection guard.

#### SH-L069 — CosmosSyncKnowledgeItem.FromInterface mutates the object it was asked to map

`../Birko.Data.Sync.CosmosDB/Models/CosmosSyncKnowledgeItem.cs:90`

When the input is already a CosmosSyncKnowledgeItem the method assigns `cosmosItem.TenantId = tenantId` and `cosmosItem.Guid ??= Guid.NewGuid()` on the caller's instance and returns it. A caller passing its own item to UpdateKnowledgeAsync has that item's tenant silently re-stamped and a Guid injected as a side effect of what the doc calls a build/map operation.

#### SH-L070 — Cosmos knowledge batches surface only the first of several failures

`../Birko.Data.Sync.CosmosDB/Stores/AsyncCosmosSyncKnowledgeStore.cs:106`

`await Task.WhenAll(tasks)` (also 144, 180) rethrows only the first faulted task's exception, so when several upserts/deletes/replaces fail the remaining CosmosExceptions are never observed — the caller sees one failure and cannot tell how many rows were written. CosmosSyncKnowledgeStore.cs:89/127/163 additionally block on the batch with `.GetAwaiter().GetResult()`, which deadlocks under a SynchronizationContext, and neither store bounds the fan-out.

#### SH-L071 — ElasticSyncKnowledgeItem.IndexName and Id/GenerateId are dead surface

`../Birko.Data.Sync.ElasticSearch/Models/ElasticSyncKnowledgeItem.cs:86`

`public const string IndexName = "sync-knowledge"` is referenced nowhere — the store never passes it and the base store resolves the index elsewhere, so documents do not live in "sync-knowledge" (the same trap the file's own CR-L215 note documents for Mongo's CollectionName). The Id/docKey value produced by GenerateId (92) is written on every document but never queried or read by any code, so it neither identifies nor de-duplicates anything.

#### SH-L072 — SqlSyncKnowledgeItem.Id is an increment field that is never written or read

`../Birko.Data.Sync.Sql/Models/SqlSyncKnowledgeItem.cs:27`

Every connector excludes `IsAutoincrement` fields from INSERT and UPDATE (`allFields.Where(f => !f.IsPrimary && !f.IsAutoincrement)`), CreateKnowledgeItem never assigns Id, and no code reads it. On MSSql/PostgreSQL/MySQL it is an unused identity column; on SQLite, FieldDefinition deliberately emits it as a plain column with no AUTOINCREMENT, so nothing ever populates it and it stays NULL for every row.

#### SH-L073 — Through() silently rewrites a HasOne into a many-to-many

`../Birko.Data.Aggregates/Core/RelationshipBuilder.cs:44`

`Through<TJunction>` unconditionally sets `_descriptor.Type = RelationshipType.ManyToMany`, including on a descriptor created by HasOne. The navigation is thereafter flattened into NestedCollections instead of NestedSingles, so `GetSingle<TChild>(nav)` returns null forever and ExpandSingle is never reached — the declared cardinality is overridden with no error or warning.

#### SH-L074 — Mapper dereferences the data provider's result without a null check

`../Birko.Data.Aggregates/Mapping/AggregateMapper.cs:45`

`related.FirstOrDefault()` and `result.NestedCollections[nav] = related` run on whatever IRelatedDataProvider.GetRelated returned. The interface declares a non-nullable `IEnumerable<AbstractModel>`, so a provider returning null (the natural "no children" implementation) produces a NullReferenceException inside Flatten rather than a validated error — while the same method null-checks root and dataProvider two lines earlier. Same at lines 40, 49, 68, 73, 77 and in Expand at 124-133.

#### SH-L075 — Sync flatten/expand helpers are lazy while their async twins are eager

`../Birko.Data.Aggregates/Extensions/SyncPipelineExtensions.cs:87`

`ExpandManyFromSync` returns `aggregates.SelectMany(...)` and `AggregateMapper.FlattenMany` returns `roots.Select(...)`, so no provider call happens until the caller enumerates and every re-enumeration re-reads the whole aggregate from the store; FlattenManyAsync and ExpandManyFromSyncAsync materialise into a List and check cancellation per aggregate. The same API pair differs in when I/O happens, when exceptions surface, and whether cancellation is honoured.

#### SH-L076 — OneToOne flattening silently discards extra matches

`../Birko.Data.Aggregates/Mapping/AggregateMapper.cs:45`

For a OneToOne relationship the provider's result is reduced with `related.FirstOrDefault()`. If the FK is not actually unique the surplus children are dropped with no error, and because the provider's ordering is unspecified, which child survives can change between runs — whereupon Expand emits a spurious Delete+Insert swap (ExpandSingle, line 185).

#### SH-L077 — FlattenResult accessors conflate "not flattened" with "empty" and "null"

`../Birko.Data.Aggregates/Mapping/FlattenResult.cs:45`

`GetCollection<TChild>` returns null when the navigation key is absent but an empty sequence when it is present and empty, so a caller must handle two null-ish states; `GetSingle<TChild>` returns null for "never flattened", "flattened as null" and "flattened as the wrong type" (`single as TChild`) — the last being exactly the silent-null failure CR-H040 fixed for collections by switching to OfType.

### area: entity-localization

#### SH-L078 — Destroy() leaves every translation row orphaned; Init() never touches the translation store

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:107`

Init/Destroy (106-107) forward to the inner store only. Destroy() therefore wipes the entity store while the translation store keeps every row for entities that no longer exist - a leak with no reachable cleanup path, and one that resurfaces as bogus translations when GUIDs are re-seeded (fixture data with fixed GUIDs). Same at LocalizedBulkStoreWrapper.cs:230, AsyncLocalizedStoreWrapper.cs:109, AsyncLocalizedBulkStoreWrapper.cs:238.

#### SH-L079 — A GetLocalizableFields name that is not a writable string property is silently ignored everywhere

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:216`

ApplyTranslations requires prop != null && PropertyType == typeof(string) && CanWrite and otherwise does nothing (216-219); SaveTranslations skips on prop == null || type mismatch (248). ILocalizable's doc says these must be string properties but nothing validates it - a typo or renamed property disables localization for that field with no exception or log, while the same name still participates in filter/order-by matching (fieldSet.Contains works on the string alone), so a filter on it is treated as localized and resolves against rows that can never exist.

#### SH-L080 — Translation lookup and cascade delete ignore EntityType, contradicting the documented four-part key

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:293`

EntityTranslationModel documents each row as one (EntityGuid, EntityType, FieldName, Culture) combination, but DeleteTranslations uses ByEntity(guid) (293) and ApplyTranslations uses ByEntityAndCulture (196), both leaving EntityType unconstrained, so any row sharing the GUID is applied or deleted regardless of type. Reachable in-process, not only via GUID collision: because writes stamp the runtime type name while resolution uses typeof(T).Name, one entity can own rows under two EntityType values, and ApplyTranslations applies whichever is enumerated last.

#### SH-L081 — LocalizedOrderByHelper reimplements OrderByHelper with divergent semantics

`../Birko.Data.Localization/Expressions/LocalizedOrderByHelper.cs:57`

Birko.Data.Stores.OrderByHelper.ApplyTo(IEnumerable) already does dynamic in-memory ordering; this copy diverges in three ways - accumulator guarded by loop index rather than null (the NRE above), a missing property skipped instead of throwing from Expression.Property, and SafeObjectComparer instead of the default comparer. Consequence: the same OrderBy over the same data sorts differently on the default culture (backend/OrderByHelper, backend NULL placement - NULLs last on PostgreSQL) versus a non-default one (this helper, NULLs always first, ordinal fallback for mismatched types).

#### SH-L082 — GetLocalizableFieldsFromInstance constructs a throwaway entity on every filter/order/update inspection

`../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs:304`

Every RewriteFilter, ReferencesLocalizedField and TouchesLocalizableField call does `_innerStore.CreateInstance().GetLocalizableFields()` - up to three allocations plus a CreateInstance round-trip per read, and CreateInstance is an inner-store extension point that may do more than `new`. It also splits the source of truth: filter/order/update decisions use the fresh instance's list while ApplyTranslations/SaveTranslations use the list from the entity being processed (209, 240), so an instance-dependent ILocalizable is filtered by one field set and translated by another.

#### SH-L083 — `_ => null!` in the operator switch is a suppression that is genuinely null

`../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs:167`

The switch is typed `Func<string, bool>` (non-nullable) and its default arm returns `null!`, which the next line null-checks (`if (predicate != null)`). The project bans CS8600-CS8625 and allows `!` only when provably safe; here the value provably IS null for every operator other than Equal/NotEqual. The method-call switch below (191) does it correctly with `Func<string, bool>?` and `_ => null`, so the fix already exists in the same method.

#### SH-L084 — ValuePredicate is declared over non-nullable string although its bodies guard against null

`../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs:45`

`Func<string, bool> ValuePredicate` is invoked as condition.ValuePredicate(t.Value) (LocalizedStoreWrapper.cs:165 and the three sibling wrappers). The Contains/StartsWith/EndsWith predicates are written `v => v != null && ...` (188-190), i.e. the author expects a null argument, and a backend hydrating a NULL column into the non-nullable EntityTranslationModel.Value will supply one. It should be `Func<string?, bool>`; as written the null-tolerance is invisible to callers and to the nullable analyzer.

#### SH-L085 — README documents PropertyUpdate and Delete(filter) as delegated straight to the inner store; neither is

`../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs:181`

README's "Filter-Based Bulk Operations" section states the wrappers delegate PropertyUpdate and Delete(filter) directly to the inner store. In code, Update(filter, PropertyUpdate) intercepts and converts to an Action<T> whenever the update touches a localizable field on a non-default culture (181-194), and Delete(filter) never uses the native filter-delete - it reads the matching set and calls Delete(IEnumerable) so translations cascade (206-214). A consumer trusting the README expects native SET/DELETE performance and no translation side effects, and gets the opposite.

#### SH-L086 — In-memory paging silently returns an empty page for a negative limit instead of matching the pushdown path

`../Birko.Data.Localization/Expressions/LocalizedOrderByHelper.cs:103`

ApplyInMemoryPaging applies Skip/Take unvalidated, so offset=-5 skips nothing and limit=-1 yields an empty list. The same call with a non-localized orderBy goes to the inner store instead, where a negative limit either errors or is ignored per backend - so identical arguments produce an empty page on one culture and rows (or an exception) on another, with no validation or diagnostic at either site.

#### SH-L087 — BuildGuidFilter inlines the whole GUID set into one Contains with no chunking

`../Birko.Data.Localization/Expressions/LocalizedFilterHelper.cs:38`

The matched GUIDs become a single List<Guid> constant used in a Contains call, which SQL backends render as one IN list. A localized condition matching more than ~2100 entities exceeds MSSQL's parameter limit (and MySQL's packet size), so a broad localized search fails at the driver rather than degrading. The remarks (19-25) acknowledge this as best-effort, but nothing bounds the set - ResolveMatchingGuids can return every row of the translation slice.

### area: entity-tagging

#### SH-L088 — ToDto dereferences t.Guid!.Value with no null guard

`../Birko.Data.Tagging/Services/TagService.cs:210`  ·  _restates a first-pass finding_

`new(t.Guid!.Value, ...)` — Guid is `Guid?` defaulting to null on AbstractModel. If CreateTagInternalAsync returns the instance it was handed without the store assigning Guid (an easy slip, and the `!` suppresses the warning that would have flagged it), the caller gets "Nullable object must have a value" with no hint whether the tagging layer or the hook is at fault. AttachTagByNameAsync:164 repeats the same dereference on a tag from FindTagByNameAsync.

#### SH-L089 — Both read paths silently drop links whose tag does not resolve

`../Birko.Data.Tagging/Services/TagService.cs:98`

`if (tag is not null) tags.Add(...)` at 98 and `.Where(l => tagMap.ContainsKey(l.TagId))` at 193 discard any link whose tag is missing, with no log, no counter and no distinction between the three causes: the tag was deleted, the tag belongs to another tenant and the read hook filtered it, or the link was garbage from the unvalidated attach path. Dangling rows accumulate invisibly, and a tenant-filtering bug in GetTagByIdAsync presents to the user as 'my tags vanished' rather than as an error.

#### SH-L090 — The base stamps TenantGuid on inserts but never the AbstractLogModel timestamps

`../Birko.Data.Tagging/Services/TagService.cs:39`

Tag and EntityTag extend AbstractLogModel (non-nullable CreatedAt/UpdatedAt, PrevUpdatedAt), yet the initializers at 39-45, 108-114 and 140-146 set none of them, and UpdateTagAsync (73-77) saves without advancing UpdatedAt or rolling PrevUpdatedAt. Unless the implementation composes TimestampStoreWrapper, every tag persists with CreatedAt = UpdatedAt = 0001-01-01 forever, making the log fields useless for sorting or audit. The class takes responsibility for one cross-cutting field and silently not the other, and nothing documents that a timestamp decorator is required.

#### SH-L091 — Non-nullable string and collection parameters throw NullReferenceException, not ArgumentNullException

`../Birko.Data.Tagging/Services/TagService.cs:36`

`name.Trim()` (36, 42), `query.Trim()` (64), `tagName.Trim()` (151), `tagIds.ToHashSet()` (128) and `entityIds.Count` (173) are all unguarded. A caller passing null — easy from a deserialized request DTO with the property absent — gets an NRE from inside the tagging layer with no parameter name, instead of the ArgumentNullException the rest of the framework raises (e.g. TenantStoreWrapper's ctor). The project bans CS86xx warnings, so the absence of any guard means the null path was never considered rather than deliberately delegated.

#### SH-L092 — SearchTagsAsync forwards an unvalidated limit to the backend

`../Birko.Data.Tagging/Services/TagService.cs:64`

`SearchTagsByNameAsync(query.Trim(), limit, ct)` passes the caller's int through with no sign or upper-bound check. Zero silently returns nothing (indistinguishable from 'no matches'), and a negative value diverges per backend: LINQ-to-objects Take(-1) yields empty, SQL `TOP (-1)` / `LIMIT -1` is a syntax or range error on MSSQL and MySQL, and an ElasticSearch `size: -1` is rejected. int.MaxValue turns an autocomplete call into a full-table read. The base is the only shared place this could be clamped.

#### SH-L093 — ListTagsAsync is unbounded with no paging hook anywhere in the contract

`../Birko.Data.Tagging/Services/TagService.cs:58`

ListAllTagsAsync(ct) materialises every tag for the tenant and projects all of them (58-59); ITagService offers no skip/take or PagedResult variant, unlike Birko.Data.Patterns' IPagedRepository which exists for exactly this. A tenant with tens of thousands of free-form tags (which the unvalidated blank-name creates at 42 make easy to produce) turns any tag-picker render into a full-collection read.

#### SH-L094 — GetEntityTagsAsync issues one tag query per link where its batch sibling deduplicates

`../Birko.Data.Tagging/Services/TagService.cs:95`

The loop at 95-99 calls GetTagByIdAsync once per link with no Distinct, so an entity with duplicate links queries the same tag repeatedly and returns it repeatedly; GetEntityTagsBatchAsync:179 does `Select(l => l.TagId).Distinct()` for the identical problem. Two sibling methods on one class with different query amplification, and neither exposes a batch tag-load hook, so even the 'batch' path is one round trip per distinct tag.

#### SH-L095 — Attach, detach and delete report success indistinguishably from doing nothing

`../Birko.Data.Tagging/Services/TagService.cs:121`

DetachTagAsync returns void whether it deleted a link or found none (117-122); DeleteTagAsync returns silently when the tag is absent or was filtered out by the implementation's tenant scoping (83); AttachTagAsync returns early on the already-attached path (106). A caller — or an API layer choosing between 204 and 404 — cannot distinguish 'done' from 'target did not exist' from 'the tenant filter hid it', so a cross-tenant delete attempt and a successful delete look identical to the client.

#### SH-L096 — ITaggable is dead: nothing in the library references it and no API is generic over it

`../Birko.Data.Tagging/Models/ITaggable.cs:7`

The static-abstract `TagEntityType` is the one piece of type safety the design offers, yet neither ITagService nor TagServiceBase mentions ITaggable and there is no `where T : ITaggable` overload — every entity-facing method takes entityType as a raw string (TagService.cs:91, 103, 117, 124, 149, 170). The declared discriminator and the value actually passed are never connected, which is what makes the un-normalized entityType a silent failure. CLAUDE.md and README document it as a component while it is unreachable from the service API.

#### SH-L097 — ITagService doc opens with "All operations are tenant-scoped", which the same class contradicts

`../Birko.Data.Tagging/Services/ITagService.cs:10`

Line 10 asserts blanket tenant scoping, then lines 11-14 walk it back to inserts only, and the code confirms the walk-back (no tenant comparison anywhere in TagServiceBase). A reader who stops at the first sentence — the part that shows as IntelliSense's summary line — concludes the layer isolates tenants. The safe claim and the unsafe claim sit in one comment, with the unsafe one first.

#### SH-L098 — CreateTagAsync is a get-or-create but neither its name nor its interface doc says so

`../Birko.Data.Tagging/Services/ITagService.cs:20`

The declaration at 20 has no XML doc at all, while SetEntityTagsAsync and AttachTagByNameAsync below it do. Nothing tells a caller that a matching trimmed name short-circuits the insert (TagService.cs:36-37), that their color/group will be ignored, or that the returned Id may be an existing tag's — so code that calls CreateTagAsync in a loop to seed a colour palette silently ends up with the first colour on every duplicate name.

#### SH-L099 — AttachTagByNameAsync ignores color on the hit path and never forwards a group

`../Birko.Data.Tagging/Services/TagService.cs:151`

When FindTagByNameAsync hits, lines 164-165 attach and return the stored tag with its existing colour, discarding the caller's `color`; on the miss path line 159 calls CreateTagAsync(tagName, color, ct: ct), leaving group null even for a tag whose name clearly belongs to a group. The interface doc (ITagService.cs:40-42) says only 'creates the tag if it doesn't exist yet', so a quick-tag UI passing a colour per call gets it applied or dropped depending on prior state, with no way to tell which happened.

#### SH-L100 — AddTagService uses AddScoped rather than TryAddScoped

`../Birko.Data.Tagging/Extensions/TaggingExtensions.cs:14`

Calling AddTagService twice (a common outcome when two composition-root modules both wire tagging) registers two ITagService descriptors. Single-service resolution silently takes the last, while IEnumerable<ITagService> yields both — so a later registration can override an earlier deliberate one with no diagnostic. Other Birko registration helpers of this shape use TryAdd for exactly this reason.

#### SH-L101 — TaggingExtensions doc states a registration-order requirement that does not exist

`../Birko.Data.Tagging/Extensions/TaggingExtensions.cs:9`

"Call this after registering repositories for Tag and EntityTag" — DI descriptor order is irrelevant to resolution, so the instruction is unenforceable and implies that following it guarantees the repositories are present. A consumer who calls AddTagService last and registers no repositories still compiles and still fails at first resolution inside a request scope. The helper cannot validate the dependency it documents (it does not know TImpl's constructor), so the comment gives false assurance where a real check is impossible.

### area: event-bus-and-messaging

#### SH-L102 — Redis poll faults are unobservable outside the assembly because PollError is internal

`../Birko.MessageQueue.Redis/RedisConsumer.cs:31`

`internal event EventHandler<Exception>? PollError` — application code in another assembly cannot subscribe. Both the per-iteration fault path (line 280) and the loop-terminating path (line 178) report exclusively through it, so a serializer exception on every entry, or a subscription that died at startup, produces no observable signal; the only detection is polling RedisSubscription.IsActive. MqttConsumer exposes the equivalent hook (OnHandlerError) publicly.

#### SH-L103 — Redis TTL check never expires an entry written by a foreign producer

`../Birko.MessageQueue.Redis/RedisConsumer.cs:311`

The expiry test is `DateTimeOffset.UtcNow - message.CreatedAt > ttlMs`. In the field-fallback parse path (ParseStreamEntry line 451) CreatedAt is only set when a `created_at` field is present; otherwise QueueMessage's initializer stamps DateTimeOffset.UtcNow at PARSE time, making elapsed ≈ 0. An entry carrying ttl_ms but no created_at is therefore treated as fresh no matter how old it is.

#### SH-L104 — RedisConsumer.SubscribeAsync is async without any await and returns before the subscription is live

`../Birko.MessageQueue.Redis/RedisConsumer.cs:60`

Declared `async Task<ISubscription>` but contains no await (CS1998), and it fires the poll loop with `_ = Task.Run(...)` at line 96. The awaited call completes before EnsureConsumerGroupAsync has run, so the caller cannot know when the consumer group exists or the loop is polling; a group-creation failure surfaces only later through the internal PollError.

#### SH-L105 — MQTT dispatch shares one mutable QueueMessage instance across every matching filter

`../Birko.MessageQueue.MQTT/MqttConsumer.cs:202`

OnMessageReceivedAsync builds one QueueMessage (line 168) and passes that same reference to every filter whose pattern matches, sequentially. A handler that mutates Headers.Custom, Body or PayloadType changes what the next handler sees, and the reused Id means two handlers cannot be distinguished for ack tracking.

#### SH-L106 — MqttSettings.LoadFrom(Settings) silently copies nothing when the argument is not MqttSettings

`../Birko.MessageQueue.MQTT/MqttSettings.cs:146`

`public override void LoadFrom(Settings data) { if (data is MqttSettings mqttData) LoadFrom(mqttData); }` — no else branch and no call to base.LoadFrom(data). Passing a plain Settings (or any non-MQTT descendant) copies not even Location/Name, and the call reports success, so a configuration load that silently did nothing is indistinguishable from one that worked.

#### SH-L107 — MqttSettings host constructor passes null! into non-nullable RemoteSettings parameters

`../Birko.MessageQueue.MQTT/MqttSettings.cs:98`

`: base(host, null!, username ?? null!, password ?? null!, port, useSsl)` forces three nulls through parameters the base declares non-nullable. The project bans CS8600-CS8625; this suppresses the diagnostic rather than fixing it, and any base-class code dereferencing Name/UserName/Password now NREs at a call site the compiler was told was safe.

#### SH-L108 — QueueMessage.TimeToLive is honoured only by Redis

`../Birko.MessageQueue/Core/QueueMessage.cs:43`

Grep shows TimeToLive is read only in RedisProducer.cs:61-64 (written as ttl_ms) and enforced only in RedisConsumer.ProcessEntryAsync. The in-memory channel and MQTT producer/consumer ignore it entirely, so an expired message is still delivered to handlers on two of the three backends with no indication the TTL was dropped.

#### SH-L109 — QueueMessage.Priority is parsed but never affects ordering on any backend

`../Birko.MessageQueue/Core/QueueMessage.cs:48`

Documented as "higher = more urgent". Grep finds exactly one read in the area: RedisConsumer.cs:489, which copies it back onto the parsed message. The in-memory bounded channel is strict FIFO, Redis streams are append-ordered and MQTT drops the field, so a caller setting Priority = 10 gets ordinary FIFO delivery. Dead configuration.

#### SH-L110 — ConsumerOptions.PrefetchCount is read by no backend in the area

`../Birko.MessageQueue/Core/ConsumerOptions.cs:17`

Grep over Birko.MessageQueue*, Birko.EventBus* finds only the declaration. In-memory dispatches one message at a time from the channel, Redis uses RedisStreamSettings.ReadCount instead, and MQTT ignores ConsumerOptions wholesale. The documented "maximum number of unacknowledged messages" is enforced nowhere.

#### SH-L111 — MessageHeaders.ReplyTo and GroupId are never read by any producer or consumer

`../Birko.MessageQueue/Core/MessageHeaders.cs:18`

ReplyTo is documented for the "request-reply pattern" and GroupId (line 28) for "ordered delivery within a group". Grep finds no read of either outside the declarations; the three producers copy Headers wholesale onto the wire and no consumer inspects them. Both are dead configuration promising delivery semantics the area does not implement.

#### SH-L112 — AutoSubscribe is evaluated at registration time, so mutating the resolved options has no effect

`../Birko.EventBus.MessageQueue/DistributedEventBusServiceCollectionExtensions.cs:36`

`if (options.AutoSubscribe) services.AddSingleton<IHostedService, DistributedEventBusHostedService>();` runs against the local options object during registration. The same instance is registered as a singleton (line 27), so a consumer that later resolves DistributedEventBusOptions and sets AutoSubscribe = true gets no hosted service at all and silently receives nothing, while StartAsync's own AutoSubscribe check (hosted service line 31) implies the flag is honoured at runtime.

#### SH-L113 — x-delivery-count is never produced, so EventContext.DeliveryCount is always 1

`../Birko.EventBus.MessageQueue/DistributedEventBus.cs:171`

The delivery callback reads Headers.Custom["x-delivery-count"] to populate DeliveryCount, but grep over the whole framework finds no writer of that key — none of the in-memory, MQTT or Redis producers or consumers sets it. A handler (or RuleFilterBehavior, which exposes DeliveryCount in its rule context at RuleFilterBehavior.cs:68) can never distinguish a first attempt from a redelivery, even on Redis where redelivery genuinely happens.

#### SH-L114 — DistributedEventBusOptions.RetryPolicy and DeadLetterOptions are read by nothing

`../Birko.EventBus.MessageQueue/DistributedEventBusOptions.cs:35`

Both default to non-null values (RetryPolicy.Default, new DeadLetterOptions{Enabled=true}) and their XML docs admit the bus does not read them. Nor does any in-area transport: grep finds no read of RetryPolicy.GetDelay or DeadLetterOptions.GetDeadLetterDestination anywhere. The defaults advertise 3 retries and an enabled DLQ that, for the in-memory and MQTT backends, do not exist at all.

#### SH-L115 — DistributedEventSubscription.Dispose guard is a non-atomic bool, unlike the in-process one

`../Birko.EventBus.MessageQueue/DistributedEventBus.cs:302`

`if (!_isActive) return; _isActive = false; _unsubscribe();` on a plain non-volatile bool. Two concurrent Dispose calls can both pass and both invoke the unsubscribe action. InProcessEventSubscription was fixed for exactly this under CR-L250 using Interlocked.Exchange (InProcessEventBus.cs:235); the same guard in InMemorySubscription, MqttSubscription and RedisSubscription was left unconverted, so four subscription types diverge on one contract.

#### SH-L116 — _disposed flags are non-volatile bools written by Dispose and read from other threads

`../Birko.EventBus.MessageQueue/DistributedEventBus.cs:29`

`private bool _disposed;` is read by ObjectDisposedException.ThrowIf in PublishAsync/Subscribe/SubscribeToTransportAsync and written by Dispose/DisposeAsync with no volatile or lock. Same pattern in InProcessEventBus.cs:24, OutboxEventBus.cs:21, InMemoryChannel.cs:18, MqttConsumer.cs:23, MqttMessageQueue.cs:27, RedisConsumer.cs:23. Dispose and DisposeAsync can also both pass the `if (_disposed) return` check and each unsubscribe every queue subscription.

#### SH-L117 — Parallel dispatch swallows a handler's OperationCanceledException even in Continue mode

`../Birko.EventBus/Local/InProcessEventBus.cs:180`

`catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` is meant to absorb the follow-on cancellation from Stop-mode's cts.Cancel(). In Continue mode cts.Cancel() is never called, so the filter instead absorbs a genuine OCE thrown by a handler's own internal timeout: OnHandlerError is not invoked, firstStopError is not set, and the failure is completely unobservable. The sequential path (line 141) reports the same exception through OnHandlerError.

#### SH-L118 — Subscribe racing Dispose yields a subscription that reports active and receives nothing

`../Birko.EventBus/Local/InProcessEventBus.cs:86`

Subscribe does `_subscriptions.GetOrAdd(eventType, _ => [])` and closes over the returned List. Dispose (line 211) calls _subscriptions.Clear() with no synchronisation against that. A Subscribe that obtained the list just before the Clear adds its handler to a list no longer reachable from the dictionary, so GetHandlers never finds it — yet the returned IEventSubscription reports IsActive == true. Previously-issued handles also keep reporting IsActive after Dispose.

#### SH-L119 — OutboxProcessor re-resolves and re-closes the generic PublishAsync via reflection for every entry

`../Birko.EventBus.Outbox/Publishing/OutboxProcessor.cs:154`

PublishEventAsync calls typeof(IEventBus).GetMethod(nameof(PublishAsync)).MakeGenericMethod(@event.GetType()) for each of up to BatchSize (default 100) entries per poll (default every 5 s), with no per-type cache. MakeGenericMethod plus Invoke per entry dominates the loop's cost and is trivially cacheable in a ConcurrentDictionary<Type, MethodInfo>.

#### SH-L120 — OutboxEventBus.Subscribe forwards to the inner bus with no disposal check

`../Birko.EventBus.Outbox/Publishing/OutboxEventBus.cs:75`

PublishAsync guards with ObjectDisposedException.ThrowIf(_disposed, this) but Subscribe does not, and Dispose (line 93) disposes the inner bus. Calling Subscribe after Dispose forwards to a disposed InProcessEventBus/DistributedEventBus, whose own guard throws ObjectDisposedException naming the INNER type — a confusing error; for an inner implementation without a guard, a silently dead subscription.

#### SH-L121 — InMemoryOutboxStore marks an unknown entry id as a silent success

`../Birko.EventBus.Outbox/Stores/InMemoryOutboxStore.cs:70`

MarkPublishedAsync and MarkFailedAsync (line 82) both do `if (_entries.TryGetValue(...))` with no else, returning Task.CompletedTask either way, and IOutboxStore declares no return value. The caller cannot distinguish "marked published" from "the entry vanished": an entry removed by a concurrent CleanupAsync, or a wrong id from a store bug, is reported as published and the processor counts it as processed.

#### SH-L122 — InMemoryOutboxStore cleanup and GetAll enumerate entries outside the claim lock

`../Birko.EventBus.Outbox/Stores/InMemoryOutboxStore.cs:98`

GetPendingAsync mutates Status/ClaimedAt on shared OutboxEntry objects under _claimLock, but CleanupAsync (98) and GetAll (117) enumerate _entries.Values and read those same fields with no lock, and MarkPublishedAsync/MarkFailedAsync mutate them unlocked too. A cleanup pass concurrent with a claim can observe a torn Status/CreatedAt pair; the returned entries are also live references the caller can mutate.

#### SH-L123 — Outbox timestamps bypass IDateTimeProvider while the processor's cutoff uses it

`../Birko.EventBus.Outbox/Core/OutboxEntry.cs:59`

OutboxEntry.CreatedAt initialises from DateTime.UtcNow and InMemoryOutboxStore uses DateTime.UtcNow for ClaimedAt/PublishedAt (lines 44, 75), while OutboxProcessor computes its retention cutoff from the injected IDateTimeProvider (OutboxProcessor.cs:128) and EventBase uses the replaceable DefaultClock. With a TestDateTimeProvider the two clocks disagree, so retention and stale-claim reclaim cannot be exercised deterministically and a shifted clock prunes or spares the wrong rows.

#### SH-L124 — EventContext.From drops CorrelationId for any IEvent that does not derive from EventBase

`../Birko.EventBus/Core/EventContext.cs:51`

`CorrelationId = @event is EventBase eb ? eb.CorrelationId : null`. IEventBus and IEventHandler are constrained on IEvent, not EventBase, so a legitimate event implementing IEvent directly — even one declaring its own CorrelationId — is silently assigned a null correlation. CorrelationEventEnricher then generates a fresh Guid (CorrelationEventEnricher.cs:19), so the trace is broken rather than obviously missing.

#### SH-L125 — null! is used for properties the deserialization paths genuinely leave null

`../Birko.EventBus/Core/EventContext.cs:20`

EventContext.Source, EventEnvelope.EventType/Source/Payload (EventEnvelope.cs:20,25,45), OutboxEntry.EventType/Payload/Source (OutboxEntry.cs:24,29,34) and DomainEventPublished.DomainEventType/EventData (DomainEventPublished.cs:26,31) are all `= null!`. DomainEventPublished has an explicit parameterless deserialization ctor (line 70) leaving both null, and a JSON envelope missing a field deserializes to null too — the `!` asserts non-null on values the code path is documented to leave null.

#### SH-L126 — RuleFilterBehavior does not validate ruleSet, deferring the NRE to the first publish

`../Birko.EventBus/Pipeline/RuleFilterBehavior.cs:22`

Both constructors assign `_ruleSet = ruleSet` with no ArgumentNullException guard (contrast DeduplicationBehavior.cs:18). A null rule set surfaces as a NullReferenceException inside HandleAsync at `_ruleSet.IsEnabled` (line 44) — i.e. inside the pipeline during a publish, aborting the publish of an unrelated event rather than failing at wiring time.

#### SH-L127 — InMemoryDeduplicationStore grows without bound when only MarkProcessedAsync is used

`../Birko.EventBus/Deduplication/InMemoryDeduplicationStore.cs:41`

CleanupIfNeeded is invoked from ExistsAsync (37) and TryMarkProcessedAsync (53) but not from MarkProcessedAsync, a first-class IDeduplicationStore member. A caller using the documented Exists/Mark pair without ever hitting TryMark never triggers the TTL sweep, so the ConcurrentDictionary retains every event id for the process lifetime despite the configured TTL.

#### SH-L128 — DefaultTopicConvention's source-prefixed instance overload is unreachable for routing

`../Birko.EventBus/Routing/DefaultTopicConvention.cs:30`

GetTopic(IEvent) returns "{source}.{kebab-name}" — a different topic from GetTopic(Type)'s "events.{kebab-name}". DistributedEventBus routes publishes with GetTopic(@event.GetType()) (DistributedEventBus.cs:81) and subscribes with GetTopic(typeof(TEvent)) (line 143), so the instance overload never decides routing. A consumer implementing ITopicConvention.GetTopic(IEvent) for per-source routing has it silently ignored.

#### SH-L129 — AttributeTopicConvention.GetTopic(IEvent) bypasses its own cache on the attribute-less path

`../Birko.EventBus/Routing/AttributeTopicConvention.cs:33`

The instance overload calls type.GetCustomAttribute<TopicAttribute>() on every invocation before deciding, and for the common attribute-less case delegates to _fallback.GetTopic(@event), which never touches _cache. The ConcurrentDictionary added to avoid per-publish reflection therefore does nothing for the instance path — a reflection lookup per publish, the exact cost CR-L253 removed on the sibling convention.

#### SH-L130 — InMemoryChannel drops in-flight messages and disposes a CTS the dispatch loop still holds

`../Birko.MessageQueue.InMemory/InMemoryChannel.cs:119`

When the last subscriber leaves, RemoveSubscriber calls cts.Cancel() then cts.Dispose() immediately while the dispatch loop may be awaiting handler(message, ct) at line 147. The message the loop already drained from the channel is neither delivered to the remaining subscribers nor written back, so it is lost with no record; and a handler passing that disposed token to an API requiring registration sees ObjectDisposedException rather than clean cancellation.

### area: filter-expression-translation

#### SH-L131 — Any(...) in a SQL predicate is mis-parsed into a nameless sub-condition that throws at render time

`../Birko.Data.SQL/SQL/DataBase.cs:611`

`Any` matches no case in the method-name switch, so its arguments are parsed into the same condition: the collection member overwrites Name and the inner lambda is re-entered through the Lambda branch (318), producing a sub-condition whose Name resolves against the ELEMENT type (or not at all). Rendering then hits BuildSingleCondition's null-name guard and throws InvalidOperationException from deep inside the connector. ElasticSearch translates the same predicate to a NestedQuery, so a collection predicate is supported on one backend and an opaque failure on the other.

#### SH-L132 — ElasticSearch EndsWith injects the raw value into a wildcard pattern without escaping * or ?

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:541`

`new WildcardQuery { Value = "*" + ewVal.Value }` does not escape the wildcard metacharacters, so `x.Code.EndsWith("A*B")` matches any value ending with A, anything, B, and a trailing `?` matches any single character. C# EndsWith treats both literally. Same species as the unescaped SQL LIKE pattern, and both backends widen rather than narrow.

#### SH-L133 — SqlBuilderContext.EscapeValue is dead code while IsField values are emitted unescaped

`../Birko.Data.SQL/SQL/Connectors/SqlBuilderContext.cs:60`

EscapeValue ("Escapes a value to prevent SQL injection (fallback when parameters can't be used)") has no caller in any of the five strategies. Meanwhile the exact fallback it documents does exist: every strategy writes `first?.ToString()` straight into the SQL when condition.IsField is true (EqualConditionStrategy:48, ComparisonConditionStrategy:63, LikeConditionStrategy:51, InConditionStrategy:74). A documented mitigation that nothing invokes.

#### SH-L134 — ModelsByGuid treats a non-nullable Guids property as nullable and turns null into "no filter"

`../Birko.Data.Core/Filters/ModelsByGuid.cs:22`

`Guids` is declared `IEnumerable<Guid>` (non-nullable, line 13) yet Filter() branches on `Guids == null` and the constructor accepts null without guarding — a CS8618/CS8625-class annotation gap the project's rules ban. Behaviourally the two empty-ish inputs invert each other: an empty collection yields a filter that matches nothing, while null yields no filter at all, which DataBase.ParseConditionExpression(null) turns into an absent WHERE clause (matches everything).

#### SH-L135 — The MethodCallExpression arm of the values branch is unreachable

`../Birko.Data.SQL/SQL/DataBase.cs:665`

`if (expr is ConstantExpression || expr is MethodCallExpression)` can never see a MethodCallExpression: the `else if (expr is MethodCallExpression methodExpression)` at line 544 handles every method call and returns on all paths. The dead disjunct suggests method-call operands are value-materialised here when in fact they are routed through the pattern-method switch, which misleads anyone reasoning about how an IN operand gets its values.

#### SH-L136 — ExpressionNormalizer.TryFold executes filter subtrees at translation time and swallows every exception

`../Birko.Data.Core/Expressions/ExpressionNormalizer.cs:129`

Visit compiles and INVOKES every parameter-free subtree, and VisitConditional visits both branches (98-99) before checking whether the test folded to a constant — so the discarded branch is evaluated too. Any side effect (a property getter that logs, increments or opens a resource) runs during query construction, and a bare `catch` hides both genuine translation problems and OutOfMemory/StackOverflow-class signals, leaving a node the downstream parser may then widen or drop.

#### SH-L137 — ExpressionParameterReplacer combinators have no null guards

`../Birko.Data.Core/Expressions/ExpressionParameterReplacer.cs:34`

AndAlso/OrElse guard only `left == null`; a null `right` dereferences at `right.Parameters[0]` with a bare NullReferenceException, and the constructor stores oldParam/newParam unchecked (a null oldParam makes VisitParameter's reference test never fire, silently returning an uncombined lambda). Both members are the documented alternative to Expression.Invoke, so they are the composition entry point for tenant/soft-delete filter chaining.

#### SH-L138 — CreateSelectCommand interpolates caller-supplied ORDER BY / GROUP BY keys straight into the SQL

`../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs:558`

`orderFields.Select(kvp => string.Format("{0} {1}", kvp.Key, …))` and `string.Join(", ", groupFields.Values)` write their keys raw, with no QuoteIdentifier and no field-name resolution. The store overloads resolve names first (AbstractConnector_Select.cs:15), but the IDictionary<string,bool> overloads are public and accept arbitrary strings, so a caller forwarding a client-supplied sort column injects SQL into a statement whose WHERE clause is otherwise fully parameterised.

#### SH-L139 — TryParseHasValue can assign a null condition name and misses HasValue behind a Convert

`../Birko.Data.SQL/SQL/DataBase.cs:880`

It requires `member.Expression is MemberExpression`, so `((IHasDates)x).ClosedAt.HasValue` — where the compiler inserts Convert(param) — returns null and the HasValue falls through to the generic member handling. And when the inner member resolves to no column, `condition.Name = inner.Name` stores null and the failure surfaces later as BuildSingleCondition's InvalidOperationException. Both the bare-boolean branch (651) and TryResolveParameterColumn (1148) do handle the Convert form, so the file is inconsistent.

#### SH-L140 — InConditionStrategy enumerates Values twice and binds null elements as IN parameters

`../Birko.Data.SQL/SQL/Connectors/Strategies/InConditionStrategy.cs:32`

IsEmpty enumerates once and BuildInClause again; Condition.Values is a non-generic IEnumerable that some producers set to a live or lazily-projected sequence (DataBase.InvokeExpression:1323, RuleConditionConverter's `rule.Value as IEnumerable`), so a one-shot or mutated source gives an emptiness verdict that disagrees with the rendered clause. BuildInClause also binds null items verbatim, producing `Col IN (@p0, NULL)` — never true in SQL — for a rules-supplied collection containing nulls, which the expression path filters out.

#### SH-L141 — ConvertLeaf's isOr parameter is always false, so its OR-marking branch is dead

`../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs:116`

The only call site passes `isOr: false` (line 24), so `if (isOr) return new Condition(..., true)` never executes and OR marking is left entirely to SetOr — whose result is then discarded by the AndSubCondition wrapper (the OR-renders-as-AND defect). The dead parameter makes the file read as if leaves can carry their own OR flag, obscuring where the actual bug is.

#### SH-L142 — ParseExpression stores a possibly-null constant in a non-nullable parameter dictionary and can throw on a duplicate key

`../Birko.Data.SQL/SQL/DataBase.cs:240`

`parameters.Add(key, value!)` is used at four sites (240, 266, 291, 307) against IDictionary<string, object>; EvaluateExpression legitimately returns null, so the `!` asserts an invariant the code violates and downstream consumers trusting the annotation can NRE. The key is `"@Const" + parameters.Count`, so any caller that pre-seeds the dictionary with a key of that shape makes Add throw ArgumentException mid-translation instead of generating a fresh name.

#### SH-L143 — ParseFilterQuery's catch-all converts cancellation and infrastructure faults into NotSupportedException

`../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs:93`

`catch (Exception ex)` wraps everything ParseExpression can throw — including OperationCanceledException from a token observed inside an evaluated closure member, and any TargetInvocationException from EvaluateExpression's reflective Invoke (824) — in a NotSupportedException whose message says the filter shape is unsupported. Callers cannot distinguish an untranslatable predicate from a cancelled or faulted evaluation, and cancellation stops propagating as cancellation.

### area: llm-provider-and-agents

#### SH-L144 — AgentOptions.CheckpointInterval is documented and plumbed but never read by the agent loop

`../Birko.AI.Contracts/AgentOptions.cs:70`  ·  _restates a first-pass finding_

The XML doc states it is the interval between self-reflection checkpoints and that 0 disables checkpoint prompts, and it round-trips through Clone/Merge/FromDictionary/ToDictionary — but a grep of Birko.AI finds no reference to it, so no checkpoint is ever injected at any value. Consumers configuring it get a silent no-op.

#### SH-L145 — Unguarded GetArrayLength/GetProperty on `choices` and `tool_calls[].index` in both stream parsers

`../Birko.AI/Providers/LlmProviderBase.cs:663`

`toolCallDelta.GetProperty("index").GetInt32()` is the only unguarded probe in the otherwise TryGetProperty-disciplined tool-capture parser; an OpenAI-compatible server that omits `index` on a single tool call throws KeyNotFoundException out of the enumerator. Likewise `choices.GetArrayLength()` at lines 592 and 638 throws InvalidOperationException for `"choices": null`. All three sit outside the deserialize-only try/catch, so the stream dies mid-response.

#### SH-L146 — Streaming retry discards the error body it read, reporting only the status code

`../Birko.AI/Providers/LlmProviderBase.cs:479`

`var responseBody = await response.Content.ReadAsStringAsync(...)` is assigned and then never used: the non-retryable and exhausted-retry messages are `"{provider} API Error: {status}"` with no detail, while SendWithRetryAsync runs the same body through ExtractErrorFromResponseBody and includes the provider's message. A streaming 400 (bad model name, context overflow) therefore surfaces as a bare status code even though the explanation was fetched and thrown away.

#### SH-L147 — `throw lastException` rethrows a captured exception, destroying its original stack trace

`../Birko.AI/Providers/LlmProviderBase.cs:139`

The stored exception is rethrown with `throw ex` semantics rather than ExceptionDispatchInfo.Capture(...).Throw(), so the caller sees a stack rooted at SendWithRetryAsync instead of the failing SendAsync. The line is also effectively unreachable for MaxRetries >= 0 (both catch blocks throw at the last attempt), so the dead-ish code hides rather than helps.

#### SH-L148 — SendWithRetryAsync returns the sentinel (null, null) when MaxRetries is negative — no request attempted

`../Birko.AI/Providers/LlmProviderBase.cs:142`

A RetryPolicy with MaxRetries = -1 skips the while body entirely and returns (null, null) with no message emitted. Callers that follow the normal pattern (`if (!response.IsSuccessStatusCode)`) NullReference, and the ones that null-check cannot distinguish "nothing was sent" from "the provider returned nothing". There is no validation of MaxRetries anywhere.

#### SH-L149 — SendWithRetryAsync returns an already-disposed HttpResponseMessage on every path

`../Birko.AI/Providers/LlmProviderBase.cs:71`

`response.Dispose(); return (response, responseBody);` hands back a disposed object, so a caller reading Headers (rate-limit headers, request-id) or Content gets ObjectDisposedException — only StatusCode/IsSuccessStatusCode survive. The sibling SendStreamingWithRetryAsync returns a live response, so the two protected helpers in the same base class have opposite ownership contracts with nothing in the signature to distinguish them.

#### SH-L150 — ParseSseStream requires the optional space in `data: ` and drops frames that omit it

`../Birko.AI/Providers/LlmProviderBase.cs:563`

`line.StartsWith("data: ")` (also culture-sensitive — no StringComparison) skips any `data:{...}` line, which the SSE grammar explicitly permits since the space is optional. Against such a provider the enumeration yields zero chunks, no error is raised, and Agent.cs:225 synthesises a response with StopReason "end_turn" and empty text — a silent empty success rather than a failure.

#### SH-L151 — Dead `[DONE]` check on the flushed SSE buffer can never match

`../Birko.AI/Providers/LlmProviderBase.cs:555`

The buffer is filled with `buffer.AppendLine(data)` (line 570), so its ToString() always ends with a newline and `data == "[DONE]"` at line 555 is never true. Only the pre-buffer check at 567 works. Consequently a `[DONE]` frame followed by a blank line — or a residual `[DONE]` buffer at line 574 — is yielded to the chunk parser as data instead of terminating the stream.

#### SH-L152 — Abstract IsConfigured() is declared but never consulted by the base class

`../Birko.AI/Providers/LlmProviderBase.cs:42`

Every provider is forced to implement `protected abstract bool IsConfigured()`, yet a grep of Birko.AI shows no call site: neither SendWithRetryAsync nor SendStreamingWithRetryAsync checks it, and NotConfigured() is only reachable if a derived provider remembers to call both itself. An unconfigured provider therefore issues a real HTTP request with a missing API key and surfaces a 401 instead of the "NotConfigured" stop reason the agent has a dedicated branch for.

#### SH-L153 — Request builder matches block types case-insensitively while the agent dispatches case-sensitively

`../Birko.AI/Providers/LlmProviderBase.cs:233`

BuildOpenAiStyleMessages compares `b.Type?.ToLowerInvariant()` against "text"/"tool_use", but Agent.HandleToolUse (Agent.cs:326) and the end_turn branch (Agent.cs:299) use ordinal `b.Type == "tool_use"` / `== "text"`. A block typed "Tool_Use" is serialized into the request as a tool call yet never executed by the agent, so the model sees its call echoed back with no result.

#### SH-L154 — Assistant/user messages are emitted with a null `role` when Message.Role is unset

`../Birko.AI/Providers/LlmProviderBase.cs:301`

`list.Add(new { role = m.Role, content })` passes `string?` straight through with no default, so a Message constructed without Role (the property is nullable and has no default) serializes as `"role": null` and the provider rejects the whole request with a 400 that names no offending turn. Content is defaulted (`m.Content ?? ""` at line 228) but Role is not.

#### SH-L155 — Cancellation mid-tool-batch discards completed tool results and leaves a dangling tool_use turn

`../Birko.AI/Agents/Agent.cs:328`

ThrowIfCancellationRequested at the top of the per-block loop throws after earlier tools in the same batch already ran (files written, commands executed) but before line 352 appends their results. The conversation returned to the caller — or reused via ContinueAsync — ends with an assistant turn carrying tool_use blocks and no matching tool_result, which providers reject, and the completed work is not recorded anywhere.

#### SH-L156 — OnLlmResponseReceived fires before any streamed token arrives, on failures, and again on fallback

`../Birko.AI/Agents/Agent.cs:208`

Its XML doc (AgentOptions.cs:73) says "invoked after each successful LLM response", but in the streaming loop it is invoked immediately after SendMessageStreamingAsync returns — before GetStreamAsync is even called and before the `streamingResponse.Error` check at line 210, so it fires for a provider that does not support streaming at all. The sync fallback then fires it again for the same logical turn, double-counting for any cost/usage tracker hung off it.

#### SH-L157 — No per-tool try/catch: one throwing tool aborts the entire agent run

`../Birko.AI/Agents/Agent.cs:335`

`await tool.ExecuteAsync(...)` is unguarded, so a custom tool added via AddTool that throws (the built-ins all catch internally, which is convention only — Tool declares no such contract) propagates out of RunAsync, discarding the whole conversation and any results collected in the current batch. The framework's own error channel (a string starting with "Error") is never reached.

#### SH-L158 — AddTool/RemoveTool mutate the live tool list with no null check and no synchronization

`../Birko.AI/Agents/Agent.cs:85`

AddTool dereferences `tool` before any null check (NullReferenceException on tool.MessageCallback rather than ArgumentNullException), and both AddTool and RemoveTool mutate the same List<Tool> that an in-flight RunAsync passes to the provider and enumerates at line 333 — a mutation during a run throws InvalidOperationException out of the LINQ scan or changes the advertised tool set mid-conversation.

#### SH-L159 — RunAsync returns the same List<Message> shape for success, LLM error, not-configured and exhaustion

`../Birko.AI/Agents/Agent.cs:262`

Every terminal path in HandleResponse returns true and RunWithHistorySyncAsync returns `conversation`; there is no status, exception or flag. A caller cannot distinguish a completed task from "provider not configured", "LLM request failed" or "max iterations reached" except by string-matching the injected "Error: …" text or counting messages — and the unknown-stop-reason branch (line 314) injects nothing at all when Verbose is false.

#### SH-L160 — MaxIterations <= 0 makes RunAsync return without ever calling the provider

`../Birko.AI.Contracts/AgentOptions.cs:16`

Nothing validates MaxIterations, and FromDictionary happily parses "0" or "-1". The for loop at Agent.cs:262 then never executes: RunAsync returns the seed conversation, having made no LLM call, and the only signal is a "Maximum iterations (0) reached" message that is suppressed when Verbose is false. To the caller this is indistinguishable from a task that produced no output.

#### SH-L161 — Merge's parameter is non-nullable yet the body null-checks it (banned-warning annotation gap)

`../Birko.AI.Contracts/AgentOptions.cs:97`

`public void Merge(AgentOptions other)` is declared non-nullable but line 99 is `if (other == null) return;`, so the method is runtime-null-tolerant while the signature tells callers null is illegal. Under the project's no-nullable-warnings rule the parameter should be `AgentOptions?`. Callers passing a `AgentOptions?` get a CS8604 they will suppress with `!`.

#### SH-L162 — FromDictionary accepts an empty workingDirectory, breaking every file tool at run time

`../Birko.AI.Contracts/AgentOptions.cs:135`

workingDirectory is the one field copied with no validation (`if (config.TryGetValue("workingDirectory", out var workingDirectory)) options.WorkingDirectory = workingDirectory;`), so `{"workingDirectory":""}` overwrites the "./" default with "". Every tool then calls Path.GetFullPath("") or Path.Combine("", …) and returns "Error reading file: The path is empty." for every operation — while Merge (line 105) guards the identical assignment with IsNullOrEmpty. There is also no null guard on `config` itself.

#### SH-L163 — LlmStreamingResponse has no disposal guard or finalizer, and SuppressFinalize is a no-op

`../Birko.AI.Contracts/Models/LlmStreamingResponse.cs:57`

Dispose() relies on nulling Resource for idempotence (fine) but the type declares no finalizer, so GC.SuppressFinalize is dead, and a response that is never disposed — any provider path that returns before the agent's `await using`, e.g. the Error-set early return at Agent.cs:213 constructed outside a using — leaks the HTTP connection until the HttpResponseMessage itself is finalized. There is also no ObjectDisposedException on later GetStreamAsync use.

#### SH-L164 — AgentFactory resolves only one alias hop and never validates that an alias target exists

`../Birko.AI/Factories/AgentFactory.cs:45`

RegisterAlias accepts any primaryType without checking _factories, and ResolveAgentType does a single lookup. So RegisterAlias("docs","writer") with no "writer" factory registers cleanly and fails only at Create, with a message naming "docs" (line 78) rather than the missing "writer" — pointing the operator at the wrong name. IsRegistered("docs") also returns false with no hint that the alias itself exists.

#### SH-L165 — ResolveAgentType throws ArgumentNullException from a Dictionary lookup on a null agentType

`../Birko.AI/Factories/AgentFactory.cs:54`

`_aliases.TryGetValue(agentType, …)` throws ArgumentNullException("key") when agentType is null, so Create(provider, options, null!) surfaces an exception naming "key" instead of the ArgumentException naming the registered types that the guarded paths produce. Register/RegisterAlias/IsRegistered all guard with IsNullOrWhiteSpace; Create and ResolveAgentType do not.

#### SH-L166 — run_command discards all buffered output on timeout and leaves the reader tasks unobserved

`../Birko.AI/Tools/RunCommandTool.cs:64`

The timeout catch kills the tree and returns "Error: Process timed out" without awaiting stdoutTask/stderrTask, so everything the process printed before the timeout — usually the most diagnostic part (a build log up to the hang) — is thrown away, and the two abandoned ReadToEndAsync tasks can fault after the fact as unobserved task exceptions.

#### SH-L167 — A negative timeout_seconds throws after the process starts, orphaning the child

`../Birko.AI/Tools/RunCommandTool.cs:57`

`cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds))` throws ArgumentOutOfRangeException for a negative value (and 0 cancels instantly). The throw happens after Process.Start, and the outer catch returns "Error running command: …" without killing the process — `using var proc` disposes the wrapper, not the child — so a model-supplied `timeout_seconds: -1` leaves a detached process running for the lifetime of the host.

#### SH-L168 — run_command advertises "shell command" but runs with UseShellExecute = false

`../Birko.AI/Tools/RunCommandTool.cs:14`

The schema description says "Executable or shell command to run" while FileName is set to the raw `command` with UseShellExecute = false, so shell builtins, pipes and redirection (`dir`, `cd x && y`, `a | b`) fail with a Win32Exception surfaced as "Error running command: The system cannot find the file specified." The model is told a capability the tool does not have and burns iterations rediscovering it.

#### SH-L169 — ask_user leaves the timeout Task.Delay running for its full duration when the prompt wins

`../Birko.AI/Tools/AskUserTool.cs:53`

timeoutTask is created directly from Task.Delay with no CancellationTokenSource, so when promptTask wins the race the timer stays armed for the remaining PromptTimeout (300s by default). Every ask_user call leaves a live timer plus its captured continuation until it fires — unbounded for an interactive agent that asks repeatedly.

#### SH-L170 — display_text reports success even when no message callback is attached

`../Birko.AI/Tools/DisplayTextTool.cs:31`

SendMessage is a no-op when MessageCallback is null (Tool.cs:19), yet the tool unconditionally returns "Text displayed successfully". An agent constructed with messageCallback: null therefore tells the model its output reached the user when it went nowhere — the same silent-sink problem applies to ask_user's "prompt_console" emission at AskUserTool.cs:67.

#### SH-L171 — write_file/edit_file/append_to_file skip the leading-'/' normalization read_file and list_files apply

`../Birko.AI/Tools/WriteFileTool.cs:34`

These three do a bare `Path.Combine(workingDirectory, filePath)` with no GetFullPath and no leading-slash strip, whereas ReadFileTool.cs:29 and ListFilesTool.cs:35 strip a single '/' and treat the path as workspace-relative. So `file_path: "/src/x.cs"` is read successfully by read_file but denied by write_file (Combine discards the workspace for a rooted path, and IsPathSafe then rejects it) — identical model intent, opposite outcome, with an access-denied message that misdescribes the cause.

#### SH-L172 — A mid-enumeration UnauthorizedAccessException aborts the whole search, discarding matches found

`../Birko.AI/Tools/SearchCodeTool.cs:47`

Directory.EnumerateFiles with AllDirectories throws when it hits an unreadable subdirectory. The per-file guard only wraps File.ReadAllLines (line 61), so the enumeration throw escapes to the outer catch and the tool returns "Error searching code: Access to the path … is denied" with every match already collected in `results` thrown away. ListFilesTool.cs:47 has the same unprotected enumeration.

#### SH-L173 — search_code returns the empty string for both "no matches" and a scan that examined nothing

`../Birko.AI/Tools/SearchCodeTool.cs:78`

`string.Join(NewLine, results)` on an empty list yields "" with no explanatory text, so the model cannot distinguish "the query genuinely does not occur" from "the glob matched no files" (a `pattern` of "*.cs" in a directory with none) or "every file was skipped as binary". Every sibling tool has an explicit empty case — list_files returns "No files found in: …".

#### SH-L174 — list_files null-checks a non-nullable input parameter, diverging from every other tool

`../Birko.AI/Tools/ListFilesTool.cs:23`

`input != null && input.TryGetValue(...)` guards a parameter declared `Dictionary<string, object> input` on Tool.ExecuteAsync, so the signature and the body disagree (the annotation gap the project's no-CS86xx rule targets). It is also the only tool that tolerates a null dictionary — the other eight dereference it immediately — so the same caller mistake is silently absorbed by one tool and thrown by the rest.

### area: migrations

#### SH-L175 — Fallback CREATE TABLE emits a bare AUTOINCREMENT keyword no provider accepts

`../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:262`

FormatColumn appends " AUTOINCREMENT" detached from PRIMARY KEY (the PK is emitted as a separate table constraint at line 247). SQLite requires exactly `INTEGER PRIMARY KEY AUTOINCREMENT` adjacent, MySQL wants AUTO_INCREMENT, PostgreSQL SERIAL/IDENTITY, MSSql IDENTITY — so an isAutoIncrement field makes the fallback DDL invalid everywhere. SqLiteConnector.FieldDefinition:148 documents this exact trap and handles it; the migration fallback does not.

#### SH-L176 — Fallback DropIndex emits `DROP INDEX ... ON ...`, invalid on SQLite and PostgreSQL

`../Birko.Data.Migrations.SQL/Context/SqlSchemaBuilder.cs:77`

Without a connector, DropIndex emits `DROP INDEX IF EXISTS "IX" ON "T"` — MySQL/MSSql syntax. SQLite and PostgreSQL reject the ON clause, so the statement throws and the migration fails; the connector path (line 73) uses the dialect-correct DropIndexSql. Reachable whenever SqlMigrationContext is built with connector == null (its default).

#### SH-L177 — SqlMigrationSettings.TransactionTimeout is plumbed and documented but never read

`../Birko.Data.Migrations.SQL/Settings/SqlMigrationSettings.cs:35`

TransactionTimeout (default 30) is not referenced anywhere in Birko.Data.Migrations.SQL — SqlMigrationRunner calls connection.BeginTransaction() with no timeout and no command sets CommandTimeout either. A consumer raising it for a long DDL migration gets no effect, silently.

#### SH-L178 — _isInitialized is a non-volatile flag read and written with no synchronization

`../Birko.Data.Migrations/AbstractMigrationRunner.cs:16`

Initialize() and the virtual InitializeAsync() both test-then-set `_isInitialized` outside any lock, and it is not volatile. Two concurrent InitializeAsync() calls both run Store.InitializeAsync (double CREATE TABLE / CreateContainerIfNotExists / bucket create), and EnsureInitialized can observe a stale false. Same pattern in RavenMigrationStore._initialized:25, CosmosMigrationStore._initialized:26, MongoMigrationStore._collection:20, InfluxMigrationStore._migrationsBucket:22.

#### SH-L179 — RegisterMigrations leaves partially-registered, unsorted state when it throws

`../Birko.Data.Migrations/AbstractMigrationRunner.cs:58`

The duplicate-version check throws mid-loop, after earlier elements of the same array were already added to _migrations, and the `_migrations.Sort(...)` at line 67 is skipped by the exception. RegisterMigrations(new[]{ v7, v3dup }) therefore leaves v7 registered and the list out of order, so a caller that catches the InvalidOperationException and retries hits a second duplicate error for v7.

#### SH-L180 — CosmosDB BulkInsert loses every failure but the first

`../Birko.Data.Migrations.CosmosDB/Context/CosmosDBDataMigrator.cs:197`

All CreateItemAsync calls are started eagerly and awaited via `Task.WhenAll(tasks).GetAwaiter().GetResult()`, which rethrows only the first exception of the AggregateException. If 50 of 1000 documents fail (409 conflict, 429 throttle), one error surfaces and the other 49 are discarded, so the caller cannot tell how much of the batch landed. ElasticSearchDataMigrator:132 explicitly reports item-level errors; Cosmos does not.

#### SH-L181 — CosmosMigrationStore.RecordMigration dereferences a possibly-null Resource

`../Birko.Data.Migrations.CosmosDB/CosmosMigrationStore.cs:125`

`var state = response.Resource; state.AppliedMigrations ??= ...` has no null guard, while GetAppliedVersions (line 91) and RemoveMigration (line 163) both use `state?.`. A state item that deserializes to null (schema drift, empty body) throws NullReferenceException instead of a diagnosable error, and the project bans nullable-warning suppressions of this kind.

#### SH-L182 — ElasticSearch schema builder hardcodes 1 shard / 0 replicas, ignoring the settings

`../Birko.Data.Migrations.ElasticSearch/Context/ElasticSearchSchemaBuilder.cs:25`

CreateCollection always creates data indices with NumberOfShards(1)/NumberOfReplicas(0); ElasticSearchMigrationSettings.NumberOfShards/NumberOfReplicas are honoured only for the internal migrations index (ElasticSearchMigrationStore.cs:40) because the schema builder is never handed the settings object. A consumer configuring 5 shards silently gets 1, and shard count cannot be changed after creation.

#### SH-L183 — ElasticSearchMigrationSettings.UseAliases is dead configuration

`../Birko.Data.Migrations.ElasticSearch/Settings/ElasticSearchMigrationSettings.cs:25`

UseAliases (default true) is not read anywhere in Birko.Data.Migrations.ElasticSearch — its own XML doc concedes "reserved / not yet wired". A consumer that leaves it true expecting zero-downtime alias switching gets plain in-place index operations with no warning.

#### SH-L184 — MongoDB store creates the unique version index only when it creates the collection

`../Birko.Data.Migrations.MongoDB/MongoMigrationStore.cs:54`

The CreateIndexModel call sits inside the `if (!collections.Any())` branch. If the __migrations collection already exists without the index (created by an older build, by a restore, or by a manual insert), Initialize() takes the else path and the unique index on `version` is never created — duplicate version documents become possible and GetAppliedVersions silently returns duplicates.

#### SH-L185 — MongoMigrationRunner throws NullReferenceException instead of ArgumentNullException

`../Birko.Data.Migrations.MongoDB/MongoMigrationRunner.cs:34`

The base initializer evaluates `mongoClient.Database` before the constructor body's `?? throw new ArgumentNullException(nameof(mongoClient))` guards can run, so passing null yields an unhelpful NRE from the base call. The guards at lines 36-37 also both blame `mongoClient` when it is its Client/Database members that were null.

#### SH-L186 — MongoDB uses a transaction only on a replica set, never on a sharded cluster

`../Birko.Data.Migrations.MongoDB/MongoMigrationRunner.cs:60`

The guard is `_client.Cluster.Description.Type == ClusterType.ReplicaSet`. MongoDB 4.2+ supports multi-document transactions on ClusterType.Sharded as well, so on a sharded deployment with UseSession = true the runner silently executes with a null session: a mid-batch failure leaves earlier migrations applied even though the deployment could have rolled them back.

#### SH-L187 — TimescaleDB introspection helpers omit the active transaction

`../Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs:162`

IsHypertable and GetChunkInterval (line 175) discard the transaction from GetSqlConnection (`var (connection, _) = ...`) and never assign command.Transaction, unlike every DDL helper in the class and ExecuteScript at line 198. Under the default UseTransaction = true they read outside the migration's transaction, so a hypertable created earlier in the same batch is invisible to IsHypertable — and on a provider enforcing the pending-local-transaction rule the command throws InvalidOperationException.

#### SH-L188 — TimescaleDBMigration bypasses AbstractMigration, losing its metadata defaults

`../Birko.Data.Migrations.TimescaleDB/TimescaleDBMigration.cs:22`

It implements IMigration directly, so Description and CreatedAt are abstract: every TimescaleDB migration must hand-write `Description => Name` and `CreatedAt => DateTime.UtcNow`, and a subclass returning a fixed CreatedAt or an empty Description silently changes what SqlMigrationStore records. It also loses AbstractMigration's NotImplementedException default for Down, contradicting the framework's one-migration-base-class convention.

#### SH-L189 — RavenDB DropIndex ignores the collection name it is given

`../Birko.Data.Migrations.RavenDB/Context/RavenDBSchemaBuilder.cs:56`

`DropIndex(collectionName, indexName)` sends DeleteIndexOperation(indexName) and never uses collectionName, so a migration dropping index "ByName" on collection Orders also deletes the identically-named index defined over Customers. Index names are database-scoped in Raven, so the collectionName parameter is misleading rather than merely unused.

#### SH-L190 — RavenDB CollectionExists reports false for an existing but empty collection

`../Birko.Data.Migrations.RavenDB/Context/RavenDBSchemaBuilder.cs:46`

It requires `count > 0` from GetCollectionStatisticsOperation, so a collection that has been created and then emptied answers false. A migration written as `if (!CollectionExists(x)) Seed(x)` re-seeds after a data purge, and the same neutral call means "container/table exists" on SQL/ES/Cosmos but "holds at least one document" here.

#### SH-L191 — RavenDB uses two different filter translators for the same JSON dialect

`../Birko.Data.Migrations.RavenDB/Context/RavenDBDataMigrator.cs:174`

UpdateDocuments/DeleteDocuments translate via ParseFilterToRql (raw RQL text with $pN parameters, line 131) while CountDocuments translates via ApplyFilterToQuery (IDocumentQuery.Where* calls). The two disagree on field-name handling — the RQL path interpolates the field name unescaped while the query path hands it to the driver — so CountDocuments and DeleteDocuments can select different documents for the same filterJson.

#### SH-L192 — RavenDB interpolates collection and field names straight into RQL

`../Birko.Data.Migrations.RavenDB/Context/RavenDBSchemaBuilder.cs:74`

RenameField builds `FROM '{collectionName}' UPDATE { this.{newName} = this.{oldName}; ... }` and DropCollection (line 34) builds `FROM '{name}'` with no escaping or validation, as does RavenDBDataMigrator.ParseFilterToRql:159 for field names. A name containing a quote or brace produces a malformed patch script — or, with a crafted name, a patch/delete over a different collection. CR-M114 parameterised the values but left the identifiers raw.

#### SH-L193 — Nullability annotations diverge from runtime tolerance in the migrator contract

`../Birko.Data.Migrations/Context/IDataMigrator.cs:7`

IDataMigrator declares `string filterJson` (non-nullable) on UpdateDocuments/DeleteDocuments but `string? filterJson` on CountDocuments/CopyData, while every implementation explicitly tolerates null (SqlDataMigrator:142, MongoDataMigrator:112, CosmosDBDataMigrator:203). Separately SchemaField (../Birko.Data.Migrations.SQL/Context/SchemaField.cs:11) passes `null!` for AbstractField's PropertyInfo and leaves the non-nullable `Table` null, so GetSelectName(withName: true) or Write() would NRE on such a field.

#### SH-L194 — SQL store INSERTs version rows while every other backend upserts

`../Birko.Data.Migrations.SQL/SqlMigrationStore.cs:341`

RecordMigration issues a plain INSERT against a Version PRIMARY KEY, so re-recording an already-applied version throws a duplicate-key DbException. MongoMigrationStore uses ReplaceOne with IsUpsert, ElasticSearch indexes by id, RavenDB and CosmosDB overwrite the dictionary entry. The same IMigrationStore.RecordMigration call is therefore idempotent on four backends and fatal on SQL — a retry after a partially-failed non-transactional batch fails on the version row rather than converging.

#### SH-L195 — CreateTablesMigration provisions on a second connection, outside the transaction

`../Birko.Data.Migrations.SQL/CreateTablesMigration.cs:69`

Up/Down ignore the IMigrationContext entirely and call _connector.CreateTable/DropTable, which opens the connector's own connection. Under the default UseTransaction = true the DDL commits independently of the runner's transaction while the version row rolls back (so a failure leaves tables created but unrecorded), and on single-writer SQLite the second connection deadlocks against the runner's write lock. The remedy is in the remarks but nothing enforces it — no guard checks that a transaction is absent.

#### SH-L196 — Dead code in the SQL and ElasticSearch migration stores

`../Birko.Data.Migrations.ElasticSearch/ElasticSearchMigrationStore.cs:17`

`private const string MigrationDocType = "_doc"` is never referenced (mapping types were removed in ES 7). In ../Birko.Data.Migrations.SQL/SqlMigrationStore.cs:256-257, CreateMigrationsTable assigns `var schema = _settings.Schema;` and `var table = _settings.MigrationsTable;` and uses neither — only fullTableName. Both are harmless but signal a reader that provider/type handling exists where it does not.

### area: repository-contract

#### SH-L197 — Comment claims the base constructor substitutes a default store; it does not

`../Birko.Data.SQL.ViewModel/Repositories/DataBaseRepository.cs:35`  ·  _restates a first-pass finding_

`DataBaseRepository(IStore<TModel>? store) : base(null)` is annotated "base constructor handles null by creating default", but AbstractViewModelRepository's constructor only does Store = store (AbstractViewModelRepository.cs:54). Passing an explicit null therefore yields a store-less repository: reads return default, Count 0, Create Guid.Empty, writes silent no-ops. Identical comment and behaviour in AsyncDataBaseRepository.cs:63.

#### SH-L198 — Async bulk ReadAsync returns IAsyncEnumerable but materializes the whole result set first

`../Birko.Data.ViewModel/Repositories/AbstractAsyncBulkViewModelRepository.cs:85`

`var models = await BulkStore.ReadAsync(filter, null, limit, offset, ct);` completes the entire underlying read before the first yield return, so the IAsyncEnumerable signature promises streaming the implementation does not provide: with no limit the full table is buffered, and LoadInstance hashes and retains every row in _modelHash before the consumer sees element one. A consumer that breaks after N items has already paid for all of them.

#### SH-L199 — Settings-keyed create/destroy keys diverge when settings or GetId() is null

`../Birko.Data.Repositories/RepositoryLocator.cs:185`

GetRepository<TRepository,TSettings>(settings) keys on `settings?.GetId() ?? string.Empty` (103), but Destroy<TRepository,TSettings>(settings) forwards settings?.GetId() unchanged and Destroy<TRepository>(key) resolves a null key to typeof(TRepository).FullName (158). For null settings — or any ISettings whose GetId() returns null — creation caches under "" while destruction looks under the repository's FullName, finds nothing, and silently leaves the instance cached.

#### SH-L200 — IViewModelCreateRepository lacks the ILoadable<TModel> constraint every sibling declares

`../Birko.Data.ViewModel/Repositories/IViewModelRepository.cs:53`

IViewModelCreateRepository<T,TModel> constrains only TModel : AbstractModel, while IViewModelReadRepository (33), IViewModelUpdateRepository (74), IViewModelDeleteRepository (94) and the aggregate IViewModelRepository (122) all also require T : Models.ILoadable<TModel>. A create-only ViewModel repository can therefore be declared over a T that cannot be loaded from TModel — a constraint set inconsistent within one interface family.

#### SH-L201 — AddOnInit/RemoveOnInit take a non-nullable hook they null-check at runtime

`../Birko.Data.SQL.ViewModel/Repositories/DataBaseRepository.cs:42`

AddOnInit(SQL.Connectors.InitConnector onInit) tests onInit != null (43/53) although the parameter is not annotated nullable, so the documented "a null hook is ignored" behaviour is unreachable from nullable-enabled callers without CS8625 — which the project's no-nullable-warnings rule forbids. Same in AsyncDataBaseRepository.cs:74/85 and in the interface declaration IDataBaseRepository.cs:14-15.

#### SH-L202 — ViewModel LoadFrom overloads are null-tolerant but declare non-nullable parameters

`../Birko.Data.Core/ViewModels/ModelViewModel.cs:26`

LoadFrom(IGuidEntity data) and LoadFrom(ModelViewModel data) wrap their bodies in `if (data != null)` while declaring the parameter non-nullable (26, 34); same in LogViewModel.cs:56/67 and AbstractLogViewModel.cs:68/79. The tolerated null is part of the observable contract (the repositories' LoadInstance path relies on nothing being thrown) but a nullable-enabled caller passing a TModel? gets CS8604, which the project bans.

#### SH-L203 — ReadMode is declared only on the async ViewModel interface

`../Birko.Data.ViewModel/Repositories/IViewModelRepository.cs:114`

IAsyncViewModelRepository declares `bool ReadMode { get; set; }` (IAsyncViewModelRepository.cs:136) but IViewModelRepository declares nothing, even though AbstractViewModelRepository implements it as a public virtual property (31) and every sync write enforces it (186/209/235 plus all six bulk overloads). A caller holding the sync interface can be rejected with InvalidOperationException("Repository is in Read Mode") yet has no way to read or clear the flag that caused it.

#### SH-L204 — AsyncDataBaseRepository implements no interface, so its SQL surface is class-only

`../Birko.Data.SQL.ViewModel/Repositories/AsyncDataBaseRepository.cs:25`

The sync family exposes Connector/AddOnInit/RemoveOnInit through IDataBaseRepository<TConnector,TViewModel,TModel>, but AsyncDataBaseRepository derives only from AbstractAsyncBulkViewModelRepository and declares no interface, so DataBaseStore, Connector, AddOnInit and RemoveOnInit are reachable only through the concrete type. Registration or substitution against an abstraction is impossible for the async half.

#### SH-L205 — ReadOne extension's four type parameters cannot be inferred, so its defaults are unusable

`../Birko.Data.SQL.ViewModel/Extensions/IDataBaseRepository.cs:12`

ReadOne<TRepository,TConnector,TViewModel,TModel> can infer only TRepository (from the receiver) and TModel (only from a non-null IFilter<TModel>); C# never infers type arguments from constraints, so TConnector and TViewModel must always be spelled out: repo.ReadOne<MyRepo, SqLiteConnector, MyVm, MyModel>(filter). The `filter = null` / `orderByExpr = null` defaults therefore cannot be used, and the natural repo.ReadOne(filter) form the signature suggests does not compile.

#### SH-L206 — Model repository Create(null) throws NRE while Save(null) returns Guid.Empty

`../Birko.Data.Repositories/AbstractRepository.cs:53`

Create forwards straight to Store.Create(data) with no null check, and DataBaseStore.CreateCore does `data.Guid ??= Guid.NewGuid()` (DataBaseStore.cs:113) — NullReferenceException. Save reaches AbstractStore.Save, which returns Guid.Empty for null data (AbstractStore.cs:135-138), so two adjacent repository methods answer the same bad input with an exception versus a success-looking sentinel. Update/Delete forward null too, and the outcome varies per backend (ES returns early, SQL dereferences).

### area: schema-index-and-ddl

#### SH-L207 — A zero [MaxLengthField] suppresses the [MaxLength]/[StringLength] fallback

`../Birko.Data.SQL/SQL/Fields/AbstractField.cs:105`

`maxLength = maxLengthField.MaxLength;` assigns unconditionally and MaxLengthField's constructor defaults to 0. The DataAnnotations fallbacks are gated on `if (maxLength == null)` (lines 142/149), so a property carrying bare `[MaxLengthField]` plus `[MaxLength(50)]` ends with maxLength == 0; the later `length > 0` test then yields a length-less StringField, i.e. TEXT instead of the declared VARCHAR(50). Treating 0 as 'unset' would fix it.

#### SH-L208 — Mixing [IndexedField] and [CompositeIndex] on one index name appends duplicate columns

`../Birko.Data.SQL/SQL/DataBase_Table.cs:254`

Both discovery passes write into the same `indexes[name]` entry and only ever Add columns — there is no dedup by ColumnName. `[IndexedField("IX_A")] TenantGuid` plus `[CompositeIndex("IX_A", nameof(TenantGuid), nameof(Number))]` yields three IndexColumns with TenantGuid twice, and the emitted `CREATE INDEX ... ("TenantGuid", "TenantGuid", "Number")` is rejected as a duplicate key column. The competing Order values also make the surviving order arbitrary.

#### SH-L209 — ParseSizeToBytes returns -1 for tb/pb sizes because the 'b' branch swallows the suffix

`../Birko.Data.ElasticSearch/IndexManagement/ElasticSearchIndexManagerAdapter.cs:188`

The chain tests gb, mb, kb, then a bare `EndsWith("b")`. `_cat/indices` reports "1.2tb" for a large index; that matches the last branch, leaving numPart = "1.2t", which double.TryParse rejects, so SizeInBytes is -1 ('not available') for exactly the indexes whose size matters most. Same for "pb".

#### SH-L210 — Duplicate column names abort table load with an opaque dictionary error

`../Birko.Data.SQL/SQL/DataBase_Table.cs:104`

`LoadFields(type).ToDictionary(x => x.Name)` throws ArgumentException('An item with the same key has already been added. Key: Code') when two properties resolve to the same column — e.g. `[NamedField("Code")] Sku` alongside a property literally named `Code`. Unlike the [CompositeIndex] mis-declaration path, which raises TableAttributeException naming attribute, property and type, this surfaces as a bare framework exception from inside LoadTable with no indication of which model or properties collided.

#### SH-L211 — GetField/GetFieldFromLambda dereference a possibly-null PropertyInfo after the AbstractModel remap

`../Birko.Data.SQL/SQL/DataBase_Field.cs:136`

After `propInfo = typeof(Models.AbstractDatabaseLogModel).GetProperty(propInfo.Name)` (and the AbstractModel branch at 134) the result is nullable but is passed as `LoadField(propInfo!)`, whose first statement calls `field.GetCustomAttributes(...)`. A property present on AbstractLogModel/AbstractModel but not re-declared on the database counterpart yields a NullReferenceException instead of the descriptive ArgumentException raised three lines earlier. Duplicated in GetFieldFromLambda (line 167); both `!` suppressions can be null.

#### SH-L212 — DataBase.Write launders nulls into a Dictionary<string, object> with the null-forgiving operator

`../Birko.Data.SQL/SQL/DataBase_Field.cs:209`

`result.Add(tableField.Name, tableField.Write(data)!)` stores null values in a dictionary whose declared value type is non-nullable `object`, so every consumer that enumerates it is told the values cannot be null while many can (Write returns null for any null property — IntegerField.cs:29 explicitly). The declared type should be Dictionary<string, object?>; the `!` hides the case rather than handling it.

#### SH-L213 — AbstractField.GetSelectName(withName: true) dereferences the null! Table back-reference

`../Birko.Data.SQL/SQL/Fields/AbstractField.cs:38`

`Table` is declared `Tables.Table = null!` and is assigned only by ComputeTable (DataBase_Table.cs:110). Fields produced through the LoadField/GetField<T,P>/GetFieldFromLambda path — the ones bulk PropertyUpdate and filter translation use — never get a Table, so any call to GetSelectName(withName: true) or the aggregate branch's `Table.Name + "."` on such a field raises NullReferenceException. The `null!` initialiser suppresses the warning that would have flagged it.

#### SH-L214 — SqLiteIndexManager.ListIndexesSql is dead code returning empty column names and zero ordinals

`../Birko.Data.SQL.SqLite/IndexManagement/SqLiteIndexManager.cs:32`

The override selects a literal `''` for column_name and `0` for ordinal_position, and its own comment says it exists only to satisfy the base contract because ListAsync is overridden. Any subclass that overrides ListAsync back to the base behaviour — or any future base-class call site — silently produces IndexInfos whose Fields are empty-named columns instead of failing. Throwing NotSupportedException would be safe; returning plausible-looking wrong data is not.

#### SH-L215 — Index-management DDL runs on its own connection, so CREATE INDEX survives a failed migration transaction

`../Birko.Data.SQL/SQL/IndexManagement/SqlIndexManager.cs:220`

ExecuteNonQueryAsync/ExecuteReaderAsync each open a fresh connection from Connector.CreateConnection(Connector.Settings), so CreateAsync/DropAsync never join an ambient store or migration transaction. A migration that creates an index then fails leaves the index behind; on a re-run the create is masked by IF NOT EXISTS, so the schema silently diverges from the migration ledger. ExistsAsync also cannot see uncommitted DDL from the transaction driving the migration.

#### SH-L216 — Portable IndexDefinition carries flags no shipped manager reads

`../Birko.Data.Patterns/IndexManagement/IndexDefinition.cs:30`

Sparse, ExpireAfter and IndexField.FieldType (Text/Geo2d/Geo2dSphere/Hashed, with public factory methods advertising them) are read by neither SqlIndexManager nor ElasticSearchIndexManagerAdapter — the only IIndexManager implementations in this capability — and the docs promise only that unsupporting providers 'ignore' them, so a caller declaring a TTL or geo index gets a plain index and no signal. IndexFieldType.Geo2d additionally has no IndexField factory while the other four do.

### area: security-and-authorization

#### SH-L217 — BirkoSecurityOptions.WildcardPermissionEnabled is a dead option — no code reads it

`../Birko.Security.AspNetCore/Extensions/SecurityServiceExtensions.cs:47`  ·  _restates a first-pass finding_

Documented as "Enable wildcard \"*\" permission that grants all access. Default: true", but nothing in the area references it. PermissionEndpointFilter (line 28) and ClaimsPermissionChecker (line 30) honour "*" unconditionally, so an operator setting it false to close the superadmin bypass gets no behaviour change and no warning.

#### SH-L218 — TokenServiceAdapter emits ClaimTypes URIs, not the JwtClaimNames the module advertises

`../Birko.Security.AspNetCore/Authentication/TokenServiceAdapter.cs:51`  ·  _restates a first-pass finding_

GenerateAccessToken writes ClaimTypes.NameIdentifier and ClaimTypes.Email, while JwtClaimNames declares UserId = "sub" and Email = "email" and is referenced by nothing except ClaimMappingOptions.TenantGuidClaim/Permission. A consumer reading "sub"/"email" directly, as JwtClaimNames advertises, sees no user id and no email; JwtClaimNames.UserId, .Email and .Role are read nowhere in the module.

#### SH-L219 — ClaimsPermissionChecker grants permissions without requiring IsAuthenticated, unlike the endpoint filter

`../Birko.Security.AspNetCore/Authorization/ClaimsPermissionChecker.cs:26`

HasPermissionAsync only compares userId to ICurrentUser.UserId and then reads Permissions — it never consults IsAuthenticated, while PermissionEndpointFilter.InvokeAsync (line 25) treats an unauthenticated caller as 401 before looking at permissions. ClaimsCurrentUser likewise reads UserId/Permissions off the principal regardless of Identity.IsAuthenticated (lines 28-62). A principal populated with claims by a non-authenticating middleware therefore passes IPermissionChecker (including via "*") but is rejected by the filter.

#### SH-L220 — Static bearer tokens are compared with ordinary ordinal equality, not a fixed-time comparison

`../Birko.Security/Authentication/AuthenticationService.cs:122`

`_expandedTokens.Contains(token)` (HashSet, StringComparer.Ordinal) and `binding.Token == token` at line 96 both short-circuit on the first differing character, while the password hashers in the same area deliberately use CryptographicOperations.FixedTimeEquals. These are long-lived shared secrets compared on a request path, so the one primitive that exists for exactly this comparison is not used here.

#### SH-L221 — No ValidAlgorithms allow-list on either symmetric JWT validation path

`../Birko.Security.Jwt/JwtTokenProvider.cs:89`

The TokenValidationParameters built at line 89 (and in AddBirkoJwtBearer, JwtBearerExtensions.cs:56) omit ValidAlgorithms, so a token signed HS384 or HS512 with the same secret validates even though GenerateToken only ever emits HS256. OidcIdTokenVerifier pins an explicit allow-list (line 35) and documents why; the symmetric paths inherit whatever the handler defaults to instead of asserting the one algorithm they mint.

#### SH-L222 — AddBirkoJwtBearer validates only that Secret is non-empty, so a 4-character HMAC key passes startup

`../Birko.Security.AspNetCore/Authentication/JwtBearerExtensions.cs:25`

The guard is `string.IsNullOrEmpty(options.Secret)` while JwtAuthenticationOptions.Secret's own doc says "minimum 32 characters recommended". A short secret survives registration and fails later inside IdentityModel at the first token signing (its own 128-bit floor), or — at 16-31 characters — signs successfully with a guessable key. The documented recommendation is enforced nowhere.

#### SH-L223 — AzureKeyVaultSettings launders null through `value!` into non-nullable base properties

`../Birko.Security.AzureKeyVault/AzureKeyVaultSettings.cs:23`

TenantId/ClientId/ClientSecret are `string?` whose setters do `Name = value!` / `UserName = value!` / `Password = value!` (lines 23, 30, 37) over Settings.Name and RemoteSettings.UserName (both `= null!` non-nullable) and PasswordSettings.Password (non-nullable). Assigning null stores a null the type system promises cannot be one. This is the exact CR-L094/CR-L353 pattern already fixed in PasswordSettings (`password ?? string.Empty`) and in VaultSettings.Token.

#### SH-L224 — BCrypt verification rejects the widely used $2y$ prefix, locking out imported hashes

`../Birko.Security.BCrypt/Hashing/BCryptPasswordHasher.cs:431`

IsValidBCryptHash accepts only 'a' or 'b' at index 2. $2y$ is what PHP's password_hash and much of the ecosystem emit, and it is semantically identical to $2b$ for the <=72-byte inputs this class enforces — the class comment even argues that case for $2a$/$2b$. A migrated user store full of $2y$ hashes returns false from Verify (and true from NeedsRehash), which reads as a wrong password rather than an unsupported format.

#### SH-L225 — BCrypt Verify honours a stored cost up to 31, so one hostile row costs 2^31 key schedules

`../Birko.Security.BCrypt/Hashing/BCryptPasswordHasher.cs:344`

`long rounds = 1L << cost` uses the cost parsed from the stored hash, not the hasher's configured work factor; HashPassword accepts anything in 4..31 (line 277). A single `$2a$31$…` row — from a corrupt migration or an attacker with write access to the password column — makes every login attempt against that account run ~2 billion ExpandKey pairs, pinning a CPU indefinitely. No verification-time ceiling exists, and the PBKDF2 sibling has the same shape with its iteration count.

#### SH-L226 — Pbkdf2PasswordHasher exposes no NeedsRehash, so IPasswordHasher consumers cannot upgrade parameters

`../Birko.Security/Hashing/Pbkdf2PasswordHasher.cs:41`

BCryptPasswordHasher ships NeedsRehash (line 81) but it is not on IPasswordHasher and Pbkdf2PasswordHasher has no equivalent. Verify also recomputes with the iteration count taken from the stored string with no floor (the constructor's 10,000 minimum is construction-only), so a hash written at 1,000 iterations verifies forever and nothing in the contract lets a caller detect it. Two implementations of one interface, two different upgrade stories.

#### SH-L227 — AddBirkoSecurity and AddBirkoJwtBearer invoke `configure` with no null guard

`../Birko.Security.AspNetCore/Extensions/SecurityServiceExtensions.cs:83`

`configure(options)` at SecurityServiceExtensions.cs:83 and JwtBearerExtensions.cs:23 dereference the delegate directly, so AddBirkoSecurity(null!) throws a bare NullReferenceException from inside the framework rather than the ArgumentNullException every other guarded member in the area raises (PermissionEndpointFilter:19, JwtTokenProvider:25, OidcIdTokenVerifier:66, all three configuration sources).

#### SH-L228 — AddBirkoJwtBearer news up JwtTokenProvider, so a registered IDateTimeProvider is ignored

`../Birko.Security.AspNetCore/Authentication/JwtBearerExtensions.cs:41`

`services.AddSingleton<ITokenProvider>(new Jwt.JwtTokenProvider(tokenOptions))` constructs the instance at registration time with the optional clock left null, so JwtTokenProvider falls back to SystemDateTimeProvider (line 26). An application that registers a TestDateTimeProvider or any custom IDateTimeProvider — the seam the constructor exists to offer, and which CR-L344 relies on for a single consistent iat/exp read — silently has no effect on issued tokens.

#### SH-L229 — PermissionResolutionMiddleware guards neither its RequestDelegate nor a null resolver result

`../Birko.Security.AspNetCore/Authorization/PermissionResolutionMiddleware.cs:30`

`public PermissionResolutionMiddleware(RequestDelegate next) => _next = next;` has no null check, and the resolver results stored at lines 43 and 48 are never validated against IUserPermissionResolver's documented "never null" contract. A resolver returning null stores null in HttpContext.Items, ResolvedPermissionsCurrentUser's `as IReadOnlySet<string>` falls through to an empty set, and the caller sees a user with no permissions rather than a broken resolver.

#### SH-L230 — AES key generation is a static on the concrete class, unreachable through IEncryptionProvider

`../Birko.Security/Encryption/AesEncryptionProvider.cs:72`

`public static byte[] GenerateKey()` is the only supported way to produce the 32-byte key that Encrypt/Decrypt demand (ValidateKey line 82), but IEncryptionProvider declares no key-generation member. A consumer injected with IEncryptionProvider must either hard-cast to AesEncryptionProvider or hand-roll RandomNumberGenerator.GetBytes(32), so the abstraction cannot be used without knowing its implementation.

#### SH-L231 — ResolveOptions mutates the caller's LocalVaultOptions instance in place

`../Birko.Security.Vault.Configuration/LocalVaultConfigurationExtensions.cs:117`

`var o = seed ?? new LocalVaultOptions();` then assigns Token/Url/User/Domain/Environment on it (lines 119-127). The caller's object is silently rewritten: environment-variable values are baked in and User/Domain/Environment are lower-cased, so a caller that reuses one options instance for a second call (or inspects it afterwards) sees values it never set. `FirstNonEmpty(o.Token, env, o.Token)` also passes the current value as its own fallback, which is dead.

#### SH-L232 — AddLocalVaultConfiguration creates a disposable VaultSecretProvider that nothing ever disposes

`../Birko.Security.Vault.Configuration/LocalVaultConfigurationExtensions.cs:57`

`new VaultSecretProvider(new VaultSettings {…})` owns the HttpClient it allocates (VaultSecretProvider.cs:39-40) and implements IDisposable, but the instance is only captured by the configuration sources and never disposed — neither the extension nor LocalVaultConfigurationSource/Provider holds a disposal path. Each call to the extension leaks one HttpClient and its handler for the process lifetime.

#### SH-L233 — VaultSecretProvider silently treats any KvVersion other than 2 as KV v1

`../Birko.Security.Vault/VaultSecretProvider.cs:251`

Every branch is `_settings.KvVersion == 2 ? … : …` (lines 121, 135, 150, 251 and the parse selection at 104). KvVersion is a plain int with no validation, so a typo'd 3 or a 0 from a bound configuration section quietly issues v1 paths and runs ParseKv1Response — which then throws KeyNotFoundException on the v2 body shape it gets back. The documented "1 or 2" domain is enforced nowhere.

#### SH-L234 — IsHealthyAsync swallows OperationCanceledException and reports the server unhealthy

`../Birko.Security.Vault/VaultSecretProvider.cs:233`

The bare `catch { return false; }` also catches cancellation of the supplied token, so a caller that cancels (shutdown, request abort) receives "Vault is unhealthy" instead of an OperationCanceledException. HttpOidcSigningKeySource in the same area deliberately excludes OperationCanceledException from its catch (line 78) for exactly this reason.

#### SH-L235 — AuthorizationContext ignores the "*" wildcard that the rest of the area treats as all-access

`../Birko.Security/Authorization/IRoleProvider.cs:38`

`HasPermission(string permission) => Permissions.Contains(permission)` is plain membership, whereas ClaimsPermissionChecker (line 29) and PermissionEndpointFilter (line 28) both accept "*" as granting everything. The same permission list therefore authorises a superadmin through one type and denies them through the other, with nothing in either type's docs marking the difference.

#### SH-L236 — Roles/permissions are joined with ';' but read back split on both ',' and ';', corrupting values

`../Birko.Security.AspNetCore/Authentication/TokenServiceAdapter.cs:63`

GenerateAccessToken joins with ';' (lines 63, 66) with no escaping or rejection of members containing a separator. ClaimsCurrentUser splits on both ',' and ';' (line 64) and ResolvedPermissionsCurrentUser splits roles the same way, while TokenServiceAdapter.ValidateToken splits on ';' only (lines 108, 112). A role or permission code containing either character is silently fragmented into two grants by one reader and kept whole by another.

#### SH-L237 — TokenServiceAdapter guards neither its constructor arguments nor a null TokenRequest.Email

`../Birko.Security.AspNetCore/Authentication/TokenServiceAdapter.cs:38`

The constructor assigns _tokenProvider and _options unchecked, unlike JwtTokenProvider (line 25) and OidcIdTokenVerifier (line 66). GenerateAccessToken then writes `claims[ClaimTypes.Email] = request.Email` (line 52) — TokenRequest's positional parameter is non-nullable but nothing enforces it, and a null value reaches `new Claim(type, value)` inside JwtTokenProvider, throwing ArgumentNullException from a frame that names neither Email nor the request.

#### SH-L238 — AuthenticationService._disposed is a non-volatile flag checked outside any synchronisation

`../Birko.Security/Authentication/AuthenticationService.cs:266`

Dispose reads and writes the plain bool `_disposed` with no lock, Interlocked or volatile, while every other field access in the class is guarded by _lock. Two concurrent Dispose calls can both pass the check and double-dispose the ReaderWriterLockSlim, and a Dispose racing an in-flight ValidateToken leaves the latter throwing ObjectDisposedException from EnterReadLock rather than returning a decision.

#### SH-L239 — JWKS cache is keyed only by provider name and has no single-flight guard

`../Birko.Security.Jwt/OpenIdConnect/HttpOidcSigningKeySource.cs:53`

The ConcurrentDictionary is keyed by provider with OidcProviderOptions.JwksUri excluded, so reconfiguring a provider's JWKS endpoint keeps serving keys fetched from the old URI for up to the cache duration. There is also no per-provider lock around the fetch, so every concurrent login at expiry issues its own GET — the two behaviours together mean the cache neither invalidates on configuration change nor collapses a burst.

### area: serialization

#### SH-L240 — Caller-supplied XmlWriterSettings reintroduce a BOM and can close the caller's stream

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:25`

`Encoding = new UTF8Encoding(false)` lives only in the `writerSettings ?? …` fallback (25). `XmlWriterSettings.Encoding` defaults to preamble-emitting `Encoding.UTF8`, so `new SystemXmlSerializer(new XmlWriterSettings{Indent=true})` makes `SerializeToBytes` (86, 97) and `Serialize(Stream,…)` (127, 137) emit `EF BB BF` while the string overloads stay BOM-free — the very stream-vs-string asymmetry the class fixed for the declaration. The same wholesale replacement lets `CloseOutput=true` reach `XmlWriter.Create`, so `using var xmlWriter` closes the caller's stream.

#### SH-L241 — XML/Protobuf/YAML async members do all their blocking work before returning the Task

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:163`

The async members of SystemXmlSerializer (163-201), ProtobufBinarySerializer (117-146) and YamlDotNetSerializer (132-158) check the token once, then run the whole synchronous serialize/deserialize — including every blocking Stream.Write/Read — on the calling thread before returning `Task.CompletedTask`/`Task.FromResult`. `await SerializeAsync(...)` on a request thread blocks for the entire payload, and a token cancelled one instruction after the check has no effect, so the members advertise async semantics they do not provide.

#### SH-L242 — Newtonsoft SerializeAsync performs blocking stream writes; only the final flush is awaited

`../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs:134`

`SerializeAsync`/`SerializeAsync<T>` call `serializer.Serialize(jsonWriter, value)` synchronously (134, 147) over a StreamWriter with a 1024-byte buffer (131, 144), so every kilobyte triggers a blocking `Stream.Write` on the caller's thread; only `await jsonWriter.FlushAsync(cancellationToken)` (135, 148) is asynchronous. For a large payload on a slow stream this is sync-over-async on a request thread, and the token is unobservable for all but the last flush.

#### SH-L243 — XML string overloads declare an encoding the returned string does not have

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:46`

`EncodedStringWriter` (46, 56, 211-216) forces the `<?xml … encoding="…"?>` declaration to name `_writerSettings.Encoding` (utf-8 by default) on a value that is a UTF-16 .NET string. The declaration is then correct only if the caller happens to persist the string as UTF-8: `File.WriteAllText(path, serializer.Serialize(v), Encoding.Unicode)` yields a document whose declaration contradicts its bytes and which strict parsers reject. The usual remedy for string output — OmitXmlDeclaration on that path — is not applied; line 28 hard-codes `OmitXmlDeclaration = false`.

#### SH-L244 — EncodedStringWriter takes a non-nullable Encoding from a settable-to-null property

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:214`

`EncodedStringWriter(Encoding encoding)` (214) declares the parameter non-nullable and stores it in a non-nullable field, but its only call sites pass `_writerSettings.Encoding` (46, 56). `XmlWriterSettings.Encoding` has no null validation, so `new SystemXmlSerializer(new XmlWriterSettings { Encoding = null! })` puts null into a non-nullable field and makes the `Encoding` override hand null to `XmlWriter.Create`. The project bans CS8600-CS8625; this is exactly the annotation gap that ban exists to catch.

#### SH-L245 — XML's (T?) cast turns a nil document into NullReferenceException for value-type T

`../Birko.Serialization/Xml/SystemXmlSerializer.cs:78`

`Deserialize<T>`, `DeserializeFromBytes<T>`, `Deserialize<T>(Stream)` and `DeserializeAsync<T>` cast with `(T?)serializer.Deserialize(xmlReader)` (78, 119, 156, 200). For an unconstrained T the compiler emits `unbox.any T`, so when `XmlSerializer.Deserialize` returns null — as it does for an `xsi:nil="true"` root — and T is a value type, the call throws NullReferenceException instead of yielding `default(T)`, despite the declared `T?` promising a null result is representable. STJ and Newtonsoft return `default(T)` for the equivalent input.

#### SH-L246 — SerializationFormat is a label nothing in the area can resolve to an implementation

`../Birko.Serialization/Core/SerializationFormat.cs:6`

The enum is exposed via `ISerializer.Format` (ISerializer.cs:22) and every impl returns a hard-coded member, but no file in the area holds a factory, registry, DI extension or switch mapping a `SerializationFormat` to an `ISerializer` — it can only be read, never used to select or validate one. It also cannot discriminate the two JSON impls (both `Json` + `application/json`) whose wire formats differ, so even a hand-written selector on it would be ambiguous. Plumbed and documented configuration with no consumer.

#### SH-L247 — ProtobufBinarySerializer exposes no configuration surface at all

`../Birko.Serialization.Protobuf/ProtobufBinarySerializer.cs:16`

The type has no constructor and no fields; all sixteen members dispatch to the static `ProtoBuf.Serializer` facade. The other five impls all take optional configuration (JsonSerializerOptions, JsonSerializerSettings, MessagePackSerializerOptions, Xml*Settings, YamlDotNet ISerializer/IDeserializer), so the abstraction's replaceable-configuration shape has a hole: the only lever — e.g. registering an unannotated type — is mutating protobuf-net's process-global default RuntimeTypeModel, shared with every other consumer in the process.

#### SH-L248 — YAML's default deserializer silently ignores keys with no matching member

`../Birko.Serialization.Yaml/YamlDotNetSerializer.cs:34`

The default `_yamlDeserializer` is built with `.IgnoreUnmatchedProperties()` (34), overriding YamlDotNet's own throw-on-unmatched default. The class doc (12-13) targets 'configuration files, CI manifests', where a misspelled key is precisely the failure a strict reader is wanted for: the property silently keeps its default and the load reports success. The opt-in is not mentioned in the remarks (17 lists only 'camelCase naming, default scalar style'), so a caller must read the constructor to find it.

#### SH-L249 — YamlDotNetSerializer's class remark points its ISerializer cref at the wrong interface

`../Birko.Serialization.Yaml/YamlDotNetSerializer.cs:17`

The remarks say "pass custom <see cref="ISerializer"/> / <see cref="IDeserializer"/> via the constructor" (17-18). Inside `namespace Birko.Serialization.Yaml` with `using YamlDotNet.Serialization;` present, `ISerializer` matches both `Birko.Serialization.ISerializer` (enclosing namespace, and the interface this class implements) and `YamlDotNet.Serialization.ISerializer`; the cref is ambiguous (CS0419) and reads as the Birko one, while the ctor params at 26-27 are explicitly YamlDotNet's — which is why every other reference in the file is fully qualified.

### area: settings-configuration-chain

#### SH-L250 — SqlSettings.GetId and its subclasses omit every provider-specific field and the password

`../Birko.Data.SQL/Stores/SqlSettings.cs:41`

GetId() = "{Location}:{Port}:{Name}:{UserName}" is not overridden by MSSql, MySql, PostgreSql, TimescaleDB or SqLite, so CommandTimeout, ConnectionTimeout, MultipleActiveResultSets, TrustServerCertificate, BulkInsertBatchSize, UseBinaryImport, TimeColumn and ChunkTimeInterval never participate in identity; SqLiteSettings also omits Password. With StoreLocator not re-applying settings on a cache hit, the second caller for a server/database inherits the first's tuning — notably TimescaleDB's hypertable column.

#### SH-L251 — SqlSettings.CommandTimeout is dead configuration for MSSql and MySQL

`../Birko.Data.SQL/Stores/SqlSettings.cs:16`

Documented as "command timeout in seconds. Default is 30", copied by LoadFrom and surfaced as MSSqlStoreFactoryOptions/MySQLStoreFactoryOptions.CommandTimeout — but neither provider emits a command-timeout keyword (MSSqlSettings.cs:32 emits only `Connection Timeout`; MySqlSettings.cs:27 likewise) and a full-tree search finds no `command.CommandTimeout =` in any SQL connector. PostgreSql, TimescaleDB and SqLite all honour it, so the option is inert on exactly two of five providers.

#### SH-L252 — PostgreSqlSettings.UseBinaryImport is plumbed and documented but never read

`../Birko.Data.SQL.PostgreSQL/Stores/PostgreSqlSettings.cs:17`

Documented as "whether to use binary import (COPY protocol) for bulk operations", copied by LoadFrom and exposed as PostgreSQLStoreFactoryOptions.UseBinaryImport, yet nothing reads it: PostgreSQLConnector.cs:314 and :360 call BeginBinaryImport/BeginBinaryImportAsync unconditionally. Setting it false to work around a COPY-incompatible type or a permission restriction has no effect and gives no signal. Contrast MySqlSettings.BulkInsertBatchSize, which MySQLConnector.cs:283/360 genuinely reads.

#### SH-L253 — TimescaleDBSettings' ctor exposes no useSecure, so a ctor-built instance can never emit SSL Mode=Require

`../Birko.Data.TimescaleDB/Stores/Settings.cs:45`

The parameterized ctor calls `base(location, name, username, password, port)`, omitting the useSecure argument MSSqlSettings, MySqlSettings and PostgreSqlSettings all expose, so UseSecure is always false. GetConnectionString (line 55) appends "SSL Mode=Require;" only when UseSecure, so `new TimescaleDBSettings(...)` always produces a plaintext PostgreSQL connection string; TLS is reachable only by mutating the property afterwards, which no sibling provider requires.

#### SH-L254 — MongoDB's parameterized constructor hard-codes port 27017 and exposes no useSecure

`../Birko.Data.MongoDB/Stores/Settings.cs:38`

`Settings(location, name, username, password)` passes a literal 27017 to base and accepts neither a port nor a useSecure flag, so a Mongo instance on a non-default port or requiring TLS cannot be configured through the constructor — the caller must know to assign Port/UseSecure afterwards. Every SQL provider and Redis accept both as optional ctor arguments, and only GetConnectionString (line 79) consults UseSecure, so the omission is silent.

#### SH-L255 — Provider default ports are applied only by parameterized ctors, so config binding yields Port 0

`../Birko.Data.SQL.MSSql/Stores/MSSqlSettings.cs:25`

`MSSqlSettings() : base() {}` leaves Port at RemoteSettings' 0; the 1433 default lives only in the parameterized ctor signature (line 27). Same for MySql (3306), PostgreSql (5432), TimescaleDB. IConfiguration/IOptions binding uses the parameterless ctor plus property assignment, so a section omitting Port produces `Server=tcp:host,0`. MongoDB Settings:26-29 and RedisSettings:40-44 DO assign their defaults in the parameterless ctor — an inconsistency, not a shared rule.

#### SH-L256 — Settings.LoadFrom(ISettings) is a silent no-op for any ISettings outside the Settings hierarchy

`../Birko.Configuration/Settings.cs:93`

The bridge is `if (data is Settings settings) LoadFrom(settings);` with no else and a void return. ISettings is public and requires only GetId, so a caller passing a custom implementation gets no copy, no exception and no signal that the load was skipped; the target keeps its previous configuration and the misconfiguration surfaces later as a connection failure.

#### SH-L257 — Location, Name and UserName are non-nullable but default to null!, unlike the fixed Password

`../Birko.Configuration/Settings.cs:35`

Location/Name (lines 35, 40) and RemoteSettings.UserName (line 187) are `string` initialized to `null!` — exactly the pattern the CR-L094 comment at lines 137-139 says was removed from Password because "a null Password the type system claims is non-null is a latent NRE". The nulls then flow out: RavenDB CreateDocumentStore does `Urls = new[] { Location }` unchecked, and MSSqlSettings interpolates them into a connection string. The project bans CS8600-CS8625.

#### SH-L258 — InfluxDB Token/Organization default to null! and GetConnectionString can return null

`../Birko.Data.InfluxDB/Stores/Settings.cs:23`

Token (line 23) and Organization (line 28) are `string` = `null!`; only the parameterized ctor coerces them with `?? string.Empty`, so a parameterless or config-bound instance holds nulls behind a non-nullable annotation. GetConnectionString (line 71) returns the bare Location, itself `null!` by default — a null returned through a non-nullable `string` return type straight into the InfluxDB client.

#### SH-L259 — InfluxDB IsTransientException classifies by bare message substring, retrying non-transient failures

`../Birko.Data.InfluxDB/Stores/Settings.cs:85`

`ex.Message.Contains("429") || Contains("503") || Contains("unavailable")` with no status-code parsing, so any message containing those digits — a record id, a measurement value, a timestamp fragment — is classified transient. InfluxDBStore.cs:497 and AsyncInfluxDBStore.cs:592 use this in a `when` filter to retry writes, so a deterministic parse failure on a batch containing "503" is retried MaxRetries times and can duplicate partially-applied points. The HttpRequestException branch (line 82) likewise retries 401/404.

#### SH-L260 — ElasticSearch IndexSettings members default to null! and MaxResultWindow's doc contradicts its default

`../Birko.Data.ElasticSearch/Stores/Settings.cs:47`

IndexSettings (line 18), IndexSettings.TypeName (line 34) and IndexSettings.Name (line 40) are non-nullable but initialized to `null!`, so the annotation is false for any default-constructed instance — consumers such as ElasticSearchStoreHelper.cs:32 defensively use `?.`, which is the tell. Separately the XML doc on MaxResultWindow states "Default is 10,000" while the property is `int?` defaulting to null; the 10,000 is the server default, not a value this object supplies.

#### SH-L261 — RemoteSettings' constructor coerces nothing for username, unlike every provider constructor

`../Birko.Configuration/Settings.cs:219`

`UserName = username` assigns straight through, while PasswordSettings' ctor (line 139) coerces `password ?? string.Empty` and every provider ctor forwards `username ?? string.Empty` (SqlSettings.cs:26, Mongo:39, Raven:24, Cosmos:34). So `new RemoteSettings(loc, name, null!, pw, port)` leaves a null in the non-nullable UserName, which then reaches `User ID={UserName}` in MSSql/MySQL and `{UserName}:{Password}@` in Mongo as an empty segment rather than being recognised as unset.

#### SH-L262 — RedisSettings.GetConnectionString is non-virtual while every sibling declares it virtual

`../Birko.Redis/RedisSettings.cs:70`

SqlSettings:35, SqLiteSettings:34, MongoDB Settings:47 and InfluxDB Settings:71 all declare `virtual GetConnectionString()`; Redis alone does not. RedisStreamSettings (Birko.MessageQueue.Redis/RedisStreamSettings.cs:9) already derives from RedisSettings, so a subclass needing different composition can only hide the method with `new`, and every call through a RedisSettings-typed reference — how RedisCache, RedisJobQueue and RedisConnectionManager all hold it — would silently bypass the override.

#### SH-L263 — RedisSettings.GetConnectionString guards a null Location but not an empty one, unlike its Port guard

`../Birko.Redis/RedisSettings.cs:80`

`sb.Append(Location ?? "localhost")` handles null but not string.Empty, while the very next line handles a bad port with a value check (`Port > 0 ? Port : 6379`). An instance whose Location was cleared — by LoadFrom from a source with an unassigned Location, or by binding an empty string — yields ":6379", which StackExchange.Redis rejects, instead of falling back to localhost as the adjacent line does. The same `?? "localhost"` at line 99 puts the empty value into sslHost.

#### SH-L264 — MSSql/MySQL/TimescaleDB always emit User ID and Password even when empty

`../Birko.Data.SQL.MySQL/Stores/MySqlSettings.cs:27`

`User ID={UserName};Password={Password};` is unconditional, so a settings object holding the string.Empty defaults produces `User ID=;Password=;`, which SqlClient/MySqlConnector read as SQL authentication with a blank user rather than "no credentials supplied" — there is no path to integrated security or socket/peer auth. MongoDB (Settings.cs:52), SqLite (SqLiteSettings.cs:37) and Redis (RedisSettings.cs:84,90) all omit the fragment when empty, so the chain holds two contradictory conventions for the same unset state.

#### SH-L265 — SqLiteSettings.Path collapses a partial configuration to null and yields an empty Data Source

`../Birko.Data.SQL.SqLite/Stores/SqLiteSettings.cs:27`

Path returns null unless BOTH Location and Name are non-empty, and GetConnectionString (line 36) interpolates it regardless, producing `Data Source=;Default Timeout=30`. A settings object carrying only Name = "app.db" (a plausible relative-path configuration) therefore yields no data source at all rather than a relative file, and the returned string looks well-formed — it only fails when the driver opens it.

#### SH-L266 — SqlSettings' NotSupportedException message names a type that does not exist

`../Birko.Data.SQL/Stores/SqlSettings.cs:38`

The base GetConnectionString throw lists the provider subclasses as "(PostgreSqlSettings, MsSqlSettings, MySqlSettings, TimescaleDBSettings)"; the actual type is MSSqlSettings (Birko.Data.SQL.MSSql.Stores.MSSqlSettings). A developer hitting this exception and searching the tree for MsSqlSettings finds nothing.

#### SH-L267 — RedisSettings.Database is documented as a 0-15 range but never validated

`../Birko.Redis/RedisSettings.cs:19`

The XML doc says "Redis database index (0-15)" and the ctor parameter repeats it, but neither the setter nor the ctor range-checks, and GetConnectionString (lines 102-106) emits `defaultDatabase=` for any non-zero value. A Database of 99 produces a connection string StackExchange.Redis accepts and the server rejects at SELECT time, far from the configuration site; GetId (line 122) also folds the invalid value into the cache key.

### area: specifications-and-paging

#### SH-L268 — RuleSpecification does not null-check its rule or rule set, unlike every sibling combinator

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:22`  ·  _restates a first-pass finding_

And/Or/NotSpecification all throw ArgumentNullException in their constructors. RuleSpecification assigns `_rule = rule` unguarded, so new RuleSpecification<T>((IRule)null) succeeds and the NullReferenceException surfaces later at the first ToExpression()/IsSatisfiedBy call. The RuleSet overload is worse: WrapRuleSet (line 53) dereferences ruleSet.IsEnabled inside a constructor initializer, so the NRE has no parameter name at all.

#### SH-L269 — WrapRuleSet shares the caller's rule List by reference and contains a dead IsEnabled assignment

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:56`

`new RuleGroup(LogicOperator.And, ruleSet.Rules)` stores the same List<IRule> instance (RuleGroup.cs:25-29 assigns without copying), so adding or disabling a rule on the RuleSet after construction silently changes what an already-built specification filters. The `{ IsEnabled = ruleSet.IsEnabled }` initializer on the same line is unreachable-false: the branch is only taken when ruleSet.IsEnabled is true, which is also RuleGroup's default.

#### SH-L270 — RuleSpecification.IsSatisfiedBy(null) throws ArgumentNullException where the base implementation tolerates null

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:46`

The override wraps the entity in ObjectRuleContext<T>, whose constructor throws ArgumentNullException(nameof(instance)). ISpecification<T>.IsSatisfiedBy(T entity) declares no nullable annotation and no class constraint, and Specification<T>'s base implementation just invokes the compiled delegate (which for a null-tolerant predicate returns a value), so swapping in a RuleSpecification behind an ISpecification<T> reference changes a returned bool into a thrown exception.

#### SH-L271 — Compiled-expression cache is published without a fence and never invalidated

`../Birko.Data.Patterns/Specification/Specification.cs:23`

`_compiledExpression ??= ToExpression().Compile()` on a non-volatile field: the reference write is not release-fenced, so on a weak memory model (arm64) another thread can observe the field non-null before the delegate's own state is visible. It is also never invalidated, so any subclass whose ToExpression() depends on mutable state (a settable filter value) keeps returning the first compiled answer from IsSatisfiedBy while ToExpression() reflects the new state — the two halves of the interface then disagree permanently.

#### SH-L272 — The &, | and ! operator overloads dereference their operands with no null check

`../Birko.Data.Patterns/Specification/Specification.cs:45`

`operator &` does `left.And(right)`, so `null & spec` (legal C# for a reference type, and reachable when a specification comes from a lookup that can miss) throws NullReferenceException from inside the operator rather than an ArgumentNullException naming the operand; `!` at line 51 has the same problem. The right-hand side is the only side that gets a named exception, from the composite constructor.

#### SH-L273 — And/Or/Not return ISpecification<T>, which declares no composition members, so composites cannot be chained

`../Birko.Data.Patterns/Specification/Specification.cs:30`

And/Or/Not and the &,|,! operators are declared on Specification<T> but typed to return ISpecification<T> (lines 30-52), while ISpecification<T> exposes only IsSatisfiedBy/ToExpression. So `a.And(b).Or(c)` and `(a & b) & c` do not compile and callers must downcast to the concrete AndSpecification<T>/OrSpecification<T> to keep composing — the pattern's whole point. Returning Specification<T> would be source-compatible.

#### SH-L274 — AndSpecification silently drops the left operand if an operand's ToExpression() returns null

`../Birko.Data.Patterns/Specification/AndSpecification.cs:25`

ExpressionParameterReplacer.AndAlso takes a nullable left (ExpressionParameterReplacer.cs:30-34) and returns `right` unchanged when it is null. ISpecification<T>.ToExpression() is typed non-nullable but nothing enforces it (a nullable-oblivious or `null!` implementation compiles), and the result is an AND that quietly loses one conjunct — a widened filter on Read/Update/Delete rather than an exception. A null right operand instead NREs at right.Parameters[0], so the two sides fail differently. OrSpecification.cs:25 is identical.

#### SH-L275 — PagedResult accepts a null Items list and stores the caller's list by reference

`../Birko.Data.Patterns/Paging/PagedResult.cs:46`  ·  _restates a first-pass finding_

The constructor performs no null check and no defensive copy. `new PagedResult<T>(null!, 0, 1, 20)` constructs happily and only fails when a consumer (serializer, grid) enumerates the non-nullable-typed Items property; and passing a mutable List<T> leaves the page's contents changeable behind the IReadOnlyList<T> facade after construction.

#### SH-L276 — TotalPages casts a double to int unchecked, so a huge TotalCount yields a negative page count

`../Birko.Data.Patterns/Paging/PagedResult.cs:34`

`(int)Math.Ceiling((double)TotalCount / PageSize)` with TotalCount long: for TotalCount above int.MaxValue*PageSize (e.g. 3e9 rows with PageSize 1, reachable since the wrappers clamp pageSize only at the low end) the double is out of int range and the unchecked conversion produces int.MinValue, so TotalPages is negative and HasNextPage (Page < TotalPages) is false on page 1 — the UI reports no further pages while billions of rows remain.

#### SH-L277 — PagedResult.Empty applies none of the wrappers' clamping, so Page can be 0 or negative

`../Birko.Data.Patterns/Paging/PagedResult.cs:57`

Empty(page = 1, pageSize = 20) forwards both arguments verbatim to the constructor, so PagedResult<T>.Empty(0, 0) yields Page 0 / PageSize 0 and a result whose HasPreviousPage is false on what a caller believes is a later page. Every other paging entry point in this area normalises page/pageSize to >= 1 (PagedRepositoryWrapper.cs:37-38), so the factory is the one way to build a PagedResult that violates the 1-based contract its own XML doc states.

#### SH-L278 — pageSize is clamped only at the low end, so an unbounded page size is forwarded as the read limit

`../Birko.Data.Patterns/Paging/PagedRepositoryWrapper.cs:38`

`if (pageSize < 1) pageSize = 1;` has no upper counterpart, so pageSize = int.MaxValue passes straight through as the repository's limit and materialises the whole result set with .ToList() (line 41) — the wrapper is the natural place for the cap since it is what an API layer binds a query-string page size to. AsyncPagedRepositoryWrapper.cs:41 is identical, and neither interface doc mentions a bound or the clamping that does happen.

#### SH-L279 — Birko.Data.Patterns/CLAUDE.md documents the async wrapper as running Read and Count in parallel; it is sequential by design

`../Birko.Data.Patterns/Paging/AsyncPagedRepositoryWrapper.cs:45`

The code awaits ReadAsync fully before CountAsync and the comment explains why (CR-L159: the wrapped repository may be a non-thread-safe store that cannot service two in-flight calls). Birko.Data.Patterns/CLAUDE.md:48 states "AsyncPagedRepositoryWrapper<T> - Wraps IAsyncBulkRepository<T>, runs Read and Count in parallel" — a reader trusting the doc would consider the sequential await a bug and "fix" it, reintroducing the concurrency hazard. The same doc's Specification section also omits RuleSpecification<T> entirely.

#### SH-L280 — A rule whose Field is null throws ArgumentNullException from GetProperty with no indication which rule failed

`../Birko.Data.Patterns/Specification/RuleSpecification.cs:74`

Rule.Field is a settable non-nullable string, so a deserialized rule missing "field" carries null. typeof(T).GetProperty(null, flags) throws ArgumentNullException (Parameter 'name') — verified — escaping ToExpression() with no rule name or field context, and the in-memory path fails the same way through ConcurrentDictionary.GetOrAdd(null). Every other malformed-leaf case in this translator degrades to a constant instead of throwing.

### area: store-crud-contract

#### SH-L281 — StoreLocator skips SetSettings when GetId() is empty, and that bucket collides with the no-settings overload

`../Birko.Data.Stores/StoreLocator.cs:38`

`if (!string.IsNullOrEmpty(id) && ... is ISettingsStore<ISettings> s)` — an ISettings whose GetId() returns string.Empty gets its store constructed and cached permanently with NO settings applied and no error. That identity is also the `string.Empty` bucket the parameterless overload uses (line 17 passes `default!`, so `settings?.GetId() ?? string.Empty`), so GetStore<MyStore>() and GetStore<MyStore,TSettings>(emptyIdSettings) share one instance.

#### SH-L282 — StoreLocator's static cache has no eviction or Destroy, unlike RepositoryLocator

`../Birko.Data.Stores/StoreLocator.cs:11`

`private static IDictionary<string, IDictionary<Type, object>>? _stores` is written by GetStore and never read for removal — no Destroy/Remove/Clear member exists. Cached stores and any connection or client they hold live for the process lifetime and nothing calls their Destroy(). The sibling RepositoryLocator does expose Destroy<TRepository>(), so the two locators diverge. A full-tree grep for `StoreLocator.` also finds no call site in the framework at all.

#### SH-L283 — CreateInstance's MissingMethodException fallback repeats the call that just failed

`../Birko.Data.Stores/AbstractStore.cs:160`  ·  _restates a first-pass finding_

`Activator.CreateInstance<T>()` and `Activator.CreateInstance(typeof(T), Array.Empty<object>())` both resolve a public parameterless constructor, so when the first throws MissingMethodException the catch block re-issues the same request and throws again. The catch buys nothing and obscures the failure origin. Same code at AbstractAsyncStore.cs:173-176.

#### SH-L284 — GetUnwrappedStore has no cycle guard and can loop forever

`../Birko.Data.Stores/StoreExtensions.cs:24`  ·  _restates a first-pass finding_

`while (current is IStoreWrapper wrapper) { var inner = wrapper.GetInnerStore(); if (inner == null) break; current = inner; }` terminates only on a null inner store. A wrapper whose GetInnerStore() returns itself, or two wrappers referencing each other (a mis-wired StoreWrapperBuilder chain), spins indefinitely — no visited-set, no iteration cap — hanging the caller instead of throwing.

#### SH-L285 — GetUnwrappedStore<T,TStore> cannot find a matching intermediate decorator

`../Birko.Data.Stores/StoreExtensions.cs:52`  ·  _restates a first-pass finding_

The typed overload walks unconditionally to the innermost store and only then applies `as TStore`, so asking for a middle layer (e.g. SoftDeleteStoreWrapper<T> beneath a tenant wrapper) returns null. IsStoreOfType<T,TStore> on the same chain can answer true via its direct-type / IStoreWrapper<T> branches, so the two helpers give contradictory answers for the same (store, TStore) pair.

#### SH-L286 — No GetUnwrappedStore overload exists for bulk stores, though IsStoreOfType has all four

`../Birko.Data.Stores/StoreExtensions.cs:43`

GetUnwrappedStore<T,TStore> is declared only for IStore<T>? (43) and IAsyncStore<T>? (96), while IsStoreOfType covers IStore<T>?, IAsyncStore<T>?, IBulkStore<T>? (149) and IAsyncBulkStore<T>? (182). A caller holding an IBulkStore<T> — the interface the whole bulk hierarchy exposes — can test the chain but cannot unwrap it without first casting to IStore<T>. Asymmetric public surface for one capability.

#### SH-L287 — SaveAsync omits ConfigureAwait(false) on both awaits while every other await in the file has it

`../Birko.Data.Stores/AbstractAsyncStore.cs:157`

`return await CreateAsync(data, processDelegate, ct);` (157) and `await UpdateAsync(data, processDelegate, ct);` (161) are the only two awaits in AbstractAsyncStore without ConfigureAwait(false) — lines 39, 43, 74, 75, 94, 95, 106, 107, 118, 119, 134, 135 all use it. In library code under a synchronization context SaveAsync resumes on the captured context, the classic sync-over-async deadlock and throughput hazard.

#### SH-L288 — AggregateAsync omits ConfigureAwait(false) on the helper await one line after using it

`../Birko.Data.InMemory/Stores/AbstractAsyncInMemoryStore.cs:229`

Line 228 is `await EnsureInitializedAsync(ct).ConfigureAwait(false);` and line 229 is `return await AggregateHelper.LinqAggregateAsync(_items.Values, query, ct);` with no ConfigureAwait — inconsistent within a single method body and the only such await in the file. Same hazard as AbstractAsyncStore.SaveAsync.

#### SH-L289 — The write delegate parameter is named processDelegate on async single-entity members, storeDelegate elsewhere

`../Birko.Data.Stores/IAsyncStore.cs:94`

IAsyncCreateStore.CreateAsync (94), IAsyncUpdateStore.UpdateAsync (114) and IAsyncStore.SaveAsync (161) name it `processDelegate`; every sync member (IStore.cs:113/132/182) and the async BULK members (AbstractAsyncInMemoryStore.cs:160/176, AbstractAsyncBulkStore.cs:34/46/87/99) name it `storeDelegate`. Named-argument call sites therefore do not port between the sync and async halves, nor between the single-entity and bulk halves of the same async store.

#### SH-L290 — Null-tolerant entity parameters across the contract are not annotated nullable, forcing CS8625 on callers

`../Birko.Data.Stores/IStore.cs:113`

`Create(T data, ...)`, `Update(T data, ...)`, `Delete(T data)` (IStore.cs:113/132/150) plus the async twins declare `T data` non-nullable, yet null is accepted and meaningfully handled everywhere: AbstractStore.Save checks `data == null` (135), AbstractInMemoryStore.CreateCore returns Guid.Empty for null (57), UpdateCore/DeleteCore use `data?.Guid` (90/100), bulk cores filter `x != null` (156/171/185). Under the project's no-nullable-warnings rule a caller passing a possibly-null entity gets CS8604/CS8625 against a callee that handles it.

#### SH-L291 — InMemoryStore's _settings is plumbed through two constructors and two setters and never read

`../Birko.Data.InMemory/Stores/InMemoryStore.cs:19`

`protected Settings? _settings` is assigned by the (Settings) constructor (34), SetSettings(Settings) (43) and indirectly SetSettings(ISettings) (60), and is never read in InMemoryStore, AbstractInMemoryStore or the async twins. Dead configuration by design (the class doc says so), but it means the ISettingsStore contract these types advertise — the one StoreLocator uses to configure a fresh store — is satisfied by a write-only field, so nothing can distinguish a configured store from an unconfigured one. Same in AsyncInMemoryStore.cs:19.

#### SH-L292 — SetSettings(ISettings) silently drops any ISettings that is not a Birko.Configuration.Settings

`../Birko.Data.InMemory/Stores/InMemoryStore.cs:56`

`if (settings is Settings concrete) { SetSettings(concrete); }` with no else — a custom ISettings implementation is discarded with no exception and no signal. The remark documents this as harmless for InMemory, but ISettingsStore<ISettings> is the ONLY path StoreLocator uses to configure a newly constructed store (StoreLocator.cs:38-41), so any store copying this shape inherits a silent misconfiguration. Same at AsyncInMemoryStore.cs:56.

#### SH-L293 — IStoreWrapper<out T>'s type parameter is unused, so the covariant test matches wrappers over unrelated types

`../Birko.Data.Stores/IStoreWrapper.cs:21`

IStoreWrapper<out T> declares only `TStore? GetInnerStoreAs<TStore>()`, which does not mention T — the parameter constrains nothing and is a bare marker. Because it is declared `out`, a wrapper implementing IStoreWrapper<DerivedInvoice> also satisfies `store is IStoreWrapper<Invoice>` in StoreExtensions.cs:79/132/165/198, so the branch that short-circuits the chain walk fires for a wrapper over a different entity type. Either use T in a signature or drop the generic interface.

#### SH-L294 — StoreException is declared but thrown, caught and referenced nowhere in the framework

`../Birko.Data.Core/Exceptions/StoreException.cs:9`

A full-tree grep for `StoreException` finds only its own declaration, the projitems entry, README/CLAUDE prose and the spec files — no throw site, no catch, no consumer. Meanwhile the contract answers every invalid input with a sentinel: null data -> Guid.Empty, unknown key -> silent no-op, null store -> false. The type meant to surface store failures is dead while the failures it exists for are unreportable. Also `StoreException(string message) : this(message, null!)` applies `!` to an argument whose parameter is already `Exception?`.

#### SH-L295 — In-memory *CoreAsync bodies ignore the cancellation token entirely

`../Birko.Data.InMemory/Stores/AbstractAsyncInMemoryStore.cs:160`

CreateCoreAsync(IEnumerable<T>,...) (160), UpdateCoreAsync(IEnumerable<T>,...) (176), DeleteCoreAsync(IEnumerable<T>,...) (191) and ReadCoreAsync (130) accept `ct` and never read it — cancellation is observed only by the EnsureInitializedAsync gate, i.e. before the work starts. A bulk create over a large sequence (or one backed by a lazy/expensive enumerator) cannot be cancelled once entered, and the unused parameter makes the store look cancellable when it is not.

#### SH-L296 — Single-item ReadCore snapshots the entire dictionary to find one match

`../Birko.Data.InMemory/Stores/AbstractInMemoryStore.cs:71`

`_items.Values.FirstOrDefault(...)` — ConcurrentDictionary<K,V>.Values materializes a fresh List of every value before FirstOrDefault sees the first element, so a single-entity read allocates O(n) even when the first candidate matches. Same at AbstractAsyncInMemoryStore.cs:75, and in CountCore (117) / the bulk ReadCore (128) where the copy is at least proportionate. `_items.Where(...)` over the live enumerator, as the Delete(filter) override at line 198 already does, avoids it.

#### SH-L297 — _initialized is a non-volatile bool read outside the lock in both abstract bases

`../Birko.Data.Stores/AbstractStore.cs:15`

Classic double-checked-locking hazard: the fast-path read (AbstractStore.cs:26, AbstractAsyncStore.cs:38) has no acquire barrier — no volatile, Interlocked, MemoryBarrier or Lazy<T> anywhere. Under the ECMA-335 memory model a reader may observe true before InitCore()'s side effects are visible, or the JIT may cache the field. Correctness rests on the stronger de-facto CLR/x86 model rather than on the code. (Also reported under store-lazy-initialization, which shares these files.)

#### SH-L298 — SemaphoreSlim _initLock is never disposed and the async bases implement no dispose contract

`../Birko.Data.Stores/AbstractAsyncStore.cs:18`

A SemaphoreSlim is allocated per instance, yet neither AbstractAsyncStore<T> nor AbstractAsyncBulkStore<T> implements IDisposable/IAsyncDisposable, and DestroyAsync is abstract with no base behaviour. Per-request store instantiation leaves undisposed semaphores for finalization. Low impact because the wait handle is lazily allocated and uncontended after init, but it is an unowned disposable on a base every async store inherits. (Also reported under store-lazy-initialization.)

#### SH-L299 — CLAUDE.md claims every store implements all four store interfaces; the code has two disjoint hierarchies

`../Birko.Data.Stores/AbstractAsyncStore.cs:14`

CLAUDE.md § Conventions states "All stores implement: IStore, IAsyncStore, IBulkStore, IAsyncBulkStore". In code AbstractStore<T> : IStore<T> and AbstractBulkStore<T> : IBulkStore<T> are sync-only; AbstractAsyncStore<T> and AbstractAsyncBulkStore<T> are async-only, with no shared root and no type here implementing both (InMemoryStore is sync-only, AsyncInMemoryStore async-only). The divergence has teeth: it is why StoreLocator's IBaseStore constraint excludes the async half and why StoreExtensions needs four near-identical IsStoreOfType overloads.

### area: store-decorator-composition

#### SH-L300 — PrevUpdatedAt is derived from the caller's in-memory UpdatedAt, writing 0001-01-01 for partial entities

`../Birko.Data.Patterns/Decorators/AsyncTimestampStoreWrapper.cs:43`

`data.PrevUpdatedAt = data.UpdatedAt;` reads the field on the object handed in, not the stored value. Updating an entity built from a DTO that never populated UpdatedAt writes PrevUpdatedAt = default(DateTime) = 0001-01-01, and updating a stale copy writes a value that was never the stored UpdatedAt. Consumers using PrevUpdatedAt as a change-detection or sync watermark read something corresponding to no prior write. Same at TimestampStoreWrapper.cs:41 and both bulk projections (TimestampBulkStoreWrapper.cs:45, AsyncTimestampBulkStoreWrapper.cs:47).

#### SH-L301 — Versioned wrappers implement only IStoreWrapper, not IStoreWrapper<T>, unlike every other decorator

`../Birko.Data.Patterns/Concurrency/VersionedStoreWrapper.cs:15`

`VersionedStoreWrapper<T> : IStore<T>, IStoreWrapper` and AsyncVersionedStoreWrapper.cs:17 likewise, while all ten decorators in Birko.Data.Patterns.Decorators declare IStoreWrapper<T> and expose GetInnerStoreAs<TInner>(). Chain-walking helpers that branch on IStoreWrapper<T> (StoreExtensions.IsStoreOfType) classify a versioned link differently from every other link, and typed inner-store access is unavailable on exactly these two wrappers.

#### SH-L302 — StoreWrapperBuilder does not null-check rawStore and can return null through a non-nullable type

`../Birko.Data.Composition/StoreWrapperBuilder.cs:37`

`IAsyncBulkStore<T> store = rawStore;` with no guard. For a T implementing no marker, Build(null) returns null from a method declared to return non-nullable IAsyncBulkStore<T> and the NullReferenceException surfaces at the first CRUD call, far from the wiring site. For a T implementing markers it instead surfaces as TargetInvocationException from Activator. Two different failures for one input, neither an ArgumentNullException, while every decorator constructor in this area does guard its inner store.

#### SH-L303 — tenantWrapperFactory's return value is used unvalidated as the chain link

`../Birko.Data.Composition/StoreWrapperBuilder.cs:77`

`store = tenantWrapperFactory(store, tenantContext);` with no null check. A factory returning null (easy when the wrapper is resolved from a container that has nothing registered) makes store null; if further decorators follow, Activator throws TargetInvocationException wrapping ArgumentNullException("innerStore"), and if none follow, Build hands back null through a non-nullable return. The delegate's own return type is non-nullable so no compiler diagnostic fires at the factory.

#### SH-L304 — Reflection construction wraps decorator ArgumentNullExceptions in TargetInvocationException

`../Birko.Data.Composition/StoreWrapperBuilder.cs:110`

`Activator.CreateInstance(closed, ctorArgs)` (also line 117) surfaces any constructor exception as TargetInvocationException. The decorators' validation contract, ArgumentNullException naming innerStore / clock / auditContext, therefore does not hold when the chain is assembled through the builder, the only supported composition path. Callers catching ArgumentNullException around Build never match and the parameter name is lost at the top level.

#### SH-L305 — Bulk Audit/Timestamp filter overloads do not null-check updates or updateAction

`../Birko.Data.Patterns/Decorators/AuditBulkStoreWrapper.cs:58`

`Update(filter, PropertyUpdate<T> updates)` calls `updates.Set(...)` immediately, so a null updates throws NullReferenceException from inside the decorator instead of ArgumentNullException at the boundary; `Update(filter, Action<T> updateAction)` (line 48) captures the delegate into a closure that NREs later inside the inner store's callback, with a stack trace pointing at the inner store. Same in AsyncAuditBulkStoreWrapper.cs:50/60, TimestampBulkStoreWrapper.cs:51/62, AsyncTimestampBulkStoreWrapper.cs:53/64. Every constructor here guards its arguments; the methods do not.

#### SH-L306 — Decorators mutate the caller's entity before the inner write, so a failed write leaves it stamped

`../Birko.Data.Patterns/Decorators/SoftDeleteStoreWrapper.cs:60`

`Delete(T)` sets `data.DeletedAt = _clock.UtcNow` and then calls `_innerStore.Update(data)`. If the inner update throws, the caller still holds an object that reports itself deleted, and any later Save persists that. Same pattern for Timestamp (CreatedAt/UpdatedAt/PrevUpdatedAt), Audit (CreatedBy/UpdatedBy) and Sluggable (Slug): all mutate in place with no copy and no rollback, so an entity that failed to persist is indistinguishable from one that did.

#### SH-L307 — SlugGenerator.EnsureUnique has no attempt cap and an unchecked suffix counter

`../Birko.Data.Patterns/Decorators/SlugGenerator.cs:76`

`while (isSlugTaken(slug)) { suffix++; slug = $"{baseSlug}-{suffix}"; }` issues one store round trip per iteration with no maximum, so N existing colliding slugs cost N+1 reads per entity (O(N^2) across a bulk create), and a predicate that always answers true loops until suffix overflows int, wrapping negative and producing slugs like "widget--2147483648". EnsureUniqueAsync (line 53) is identical and takes no CancellationToken, so the loop cannot be cancelled between probes.

#### SH-L308 — Audit create overwrites a caller-supplied CreatedBy with null when no user is authenticated

`../Birko.Data.Patterns/Decorators/AuditStoreWrapper.cs:31`

`data.CreatedBy = _auditContext.CurrentUserId; data.UpdatedBy = _auditContext.CurrentUserId;` runs unconditionally, and IAuditContext.CurrentUserId is documented as null when no user is authenticated. A background job, migration or seeding path that deliberately stamps a system principal has that value replaced with null, with no opt-out and no preserve-if-set branch. Same at AsyncAuditStoreWrapper.cs:33-34 and in both bulk Create projections.

#### SH-L309 — Timestamp decoration is unconditional with no opt-out, so imports cannot preserve original timestamps

`../Birko.Data.Composition/StoreWrapperBuilder.cs:52`

`if (typeof(ITimestamped).IsAssignableFrom(typeof(T)))` matches every AbstractLogModel descendant because AbstractLogModel implements ILogEntity, which extends ITimestamped. The decorator's Create then overwrites CreatedAt/UpdatedAt and nulls PrevUpdatedAt (AsyncTimestampBulkStoreWrapper.cs:35-37). A migration or replication write that must carry the source system's CreatedAt cannot, short of holding the raw store, and the builder exposes no flag to skip it, unlike Audit/Tenant/EventSourcing which are at least gated on a context argument.

#### SH-L310 — Non-Latin slug sources normalize to empty, collapsing every such entity onto item/item-2/item-3

`../Birko.Data.Patterns/Decorators/SlugGenerator.cs:31`

InvalidChars is `[^a-z0-9\-]` and is applied after diacritic stripping, so Cyrillic, Greek, CJK, Hebrew and Arabic titles reduce to string.Empty and EnsureUnique substitutes the fallback "item". A catalogue with non-Latin names gets item, item-2, item-3 ... with no transliteration hook and a linear number of uniqueness probes per insert. Normalize's doc ("URL-friendly slug") gives no hint that whole scripts are dropped rather than transliterated.

#### SH-L311 — TestDateTimeProvider mutates its clock field with no synchronization or volatile

`../Birko.Time.Abstractions/Providers/TestDateTimeProvider.cs:10`

`private DateTimeOffset _current;` is written by SetTime (line 26) and Advance (line 34) and read by UtcNow/OffsetUtcNow/Today, with no lock, no volatile and no Interlocked. DateTimeOffset is a multi-field struct so the write is not atomic; a test that advances the clock from one thread while a decorator under test reads _clock.UtcNow from another can observe a torn value. This is the injected clock for every decorator in the area and its docs advertise freezing/advancing during a run.

#### SH-L312 — Decorator awaits omit ConfigureAwait(false) while the store bases they wrap use it

`../Birko.Data.Patterns/Concurrency/AsyncVersionedStoreWrapper.cs:46`

`await _inner.ReadAsync(data.Guid ?? Guid.Empty, ct)` and line 55's update capture the synchronization context, as do SaveAsync's awaits at 62/65 and every await in AsyncAuditStoreWrapper.SaveAsync (50/54), AsyncDefaultStoreWrapper (43-157), AsyncSluggableBulkStoreWrapper (35-56) and AsyncSoftDeleteStoreWrapper (68/72). AbstractAsyncBulkStore uses ConfigureAwait(false) throughout, so the decorator layer diverges from the bases it wraps: the classic resume-on-captured-context hazard for a library called from a UI context.

### area: tenant-isolation

#### SH-L313 — Single-item Update/Delete/Create throw NullReferenceException on a null item

`../Birko.Data.Tenant/Stores/TenantStoreWrapper.cs:67`  ·  _restates a first-pass finding_

BelongsToCurrentTenant(data) dereferences item.TenantGuid with no null check, so Update(null)/Delete(null) NRE whenever a tenant is in scope — while the exception-construction arguments on the next lines use `data?.TenantGuid`, showing null was anticipated. SetTenantGuidIfNeeded (line 212) dereferences item the same way on the Create path. Under Permissive with no tenant the guard returns true without dereferencing and the null reaches the inner store instead, so the failure mode depends on ambient state.

#### SH-L314 — Data.Tenant TenantMiddleware clears the tenant outside try/finally, leaking it on a downstream throw

`../Birko.Data.Tenant/Middleware/TenantMiddleware.cs:78`  ·  _restates a first-pass finding_

ClearTenant() sits on the statement after `await _next(context)`, so any downstream exception skips it and the context still reports HasTenant for the rest of that async flow — visible to upstream error-handling middleware and anything resuming on the same flow. The sibling Birko.Security.AspNetCore.TenantMiddleware wraps the same call in try/finally, so the two entry points diverge on the same failure.

#### SH-L315 — Data.Tenant TenantMiddleware clears the tenant unconditionally, even when it set none

`../Birko.Data.Tenant/Middleware/TenantMiddleware.cs:78`  ·  _restates a first-pass finding_

ClearTenant() runs whether or not this middleware resolved a tenant. With RequireTenant = false and a request carrying no tenant source, a tenant already established on the same flow (an enclosing WithTenant, or a startup Tenant.Set on the singleton context) is discarded on the way out. Birko.Security.AspNetCore.TenantMiddleware:33 has the identical unconditional clear inside its finally.

#### SH-L316 — Missing tenant is answered with 401 and no WWW-Authenticate, not the 400 the design records

`../Birko.Data.Tenant/Middleware/TenantMiddleware.cs:62`

The RequireTenant path sets HttpStatusCode.Unauthorized for a request that is authenticated but tenantless. TenantScopeRequiredException's own rationale states the correct answer to a well-formed request missing tenant context is a 400 naming the cause; 401 without a WWW-Authenticate header also breaks the challenge contract, so clients read an auth failure for a request-shape problem.

#### SH-L317 — An empty X-Tenant-Name header suppresses CustomTenantNameResolver

`../Birko.Data.Tenant/Middleware/TenantMiddleware.cs:139`

ResolveTenantName returns `headerValue.FirstOrDefault()` as soon as TryGetValue succeeds, which it does for a present-but-empty header. The result is an empty-or-null tenant name and CustomTenantNameResolver is never consulted, so a client can suppress server-side name resolution by sending `X-Tenant-Name:` with no value.

#### SH-L318 — SetTenantGuidIfNeeded overwrites the caller's per-item TenantGuid inside an all-tenants scope, contrary to its doc

`../Birko.Data.Tenant/Stores/TenantStoreWrapper.cs:195`

The XML doc says 'inside an all-tenants (admin) scope it trusts the caller's per-item TenantGuid and leaves it untouched', but the early return is nested inside `if (!HasTenant)`. Entering WithAllTenants while a tenant is set still stamps item.TenantGuid = the ambient tenant and item.TenantName over whatever the caller supplied, so a cross-tenant admin create silently lands in the operator's own tenant. Same at AsyncTenantStoreWrapper.cs:198.

#### SH-L319 — TenantSyncResult is never constructed — tenant attribution is absent from every sync result

`../Birko.Data.Sync.Tenant/Models/TenantSyncResult.cs:10`

TenantSyncProvider.SyncAsync returns a plain SyncResult (line 350) and no code in the framework instantiates TenantSyncResult, so its TenantGuid/TenantName are unreachable: a caller cannot tell which tenant a returned SyncResult belongs to. Dead public type shipped as part of the tenant-sync contract.

#### SH-L320 — Tenant name is plumbed through sync options and knowledge items but never written or read

`../Birko.Data.Sync.Tenant/Models/TenantSyncKnowledgeItem.cs:42`

CreateKnowledgeItem sets only TenantGuid, so ITenant.TenantName on every persisted knowledge item stays null; TenantSyncOptions.TenantName (TenantSyncOptions.cs:20) is read by nothing, and ApplyTenantContext does not populate it from CurrentTenantName even while it copies TenantGuid. Three plumbed-and-documented properties no code path fills.

#### SH-L321 — ExecutePreviewAsync wraps its body in a catch that only rethrows

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:332`

`catch (Exception) { throw; }` is a no-op left behind by the CR-M167 fix; it adds a handler frame and invites a future edit to reinstate swallowing at a site whose whole point is that failures must propagate. Dead code on a path documented as deliberately transparent.

#### SH-L322 — ReportProgress discards the real SyncProgress, so callers never see counters or errors

`../Birko.Data.Sync.Tenant/Providers/TenantSyncProvider.cs:889`

Every call constructs a fresh SyncProgress with `TotalItems = 100, ProcessedItems = percent`, dropping the `progress` instance that accumulates CreatedItems/UpdatedItems/DeletedItems/SkippedItems/Conflicts/Errors. OnProgress consumers get a fake percentage and permanently zero counters — and the real progress.TotalItems (line 394) sums both dictionaries, double-counting every item present on both sides.

#### SH-L323 — TenantSyncQueue's context field is annotated nullable but can never be null, and Guid.Empty masks 'no tenant'

`../Birko.Data.Sync.Tenant/TenantSyncQueue.cs:30`

`_tenantContext` is assigned `tenantContext ?? Tenant.Current`, so every `_tenantContext?.` in the class is dead and the declared nullability is wrong. CurrentTenantGuid then collapses 'no tenant' to Guid.Empty; it is safe only because each call site pre-checks HasTenantContext, so any new caller reading CurrentTenantGuid directly silently keys its queue on the zero guid.

#### SH-L324 — EventContext parameters are treated as nullable but declared non-nullable

`../Birko.EventBus.Tenant/TenantEventEnricher.cs:36`

EnrichAsync guards `context is not null` and TenantEventScopeAccessor.RunWithScopeAsync uses `context?.TenantGuid`, so both accept null at runtime while their signatures declare `EventContext context`. Under the project's no-nullable-warnings rule a caller holding an `EventContext?` cannot pass it without a suppression, and the null-tolerance is invisible in the contract.

#### SH-L325 — TenantContextAdapter and SubdomainTenantResolver accept null constructor dependencies without guarding

`../Birko.Security.AspNetCore/Tenant/TenantContextAdapter.cs:13`

TenantContextAdapter stores birkoContext with no ArgumentNullException, so a null wrapped context surfaces as an NRE on the first CurrentTenantGuid read rather than at construction; SubdomainTenantResolver.cs:23 does the same with lookupAsync, deferring the failure to the first request that resolves a subdomain. Every other type in this area (both store wrappers, both event-bus halves, TenantSyncProvider) null-checks its constructor arguments.

#### SH-L326 — ModelByTenant's mutable TenantGuid is captured by reference in the returned predicate

`../Birko.Data.Tenant/Filters/ModelByTenant.cs:47`

`(x) => x.TenantGuid == TenantGuid` closes over `this`, not over a local copy, so the expression reads the public settable property whenever it is finally evaluated or funcletized. A caller (or a subclass overriding Filter) that reassigns TenantGuid after Filter() returns silently changes which tenant an already-composed, not-yet-executed predicate matches.

#### SH-L327 — Bulk Create stamps tenants lazily, so a Strict refusal surfaces from inside the inner store's enumeration

`../Birko.Data.Tenant/Stores/TenantBulkStoreWrapper.cs:23`

`data.Select(item => { SetTenantGuidIfNeeded(item); return item; })` is not materialized, unlike bulk Update/Delete which were fixed to materialize once (CR-M173). The TenantScopeRequiredException a Strict wrapper owes the caller is therefore thrown from within the inner store's iteration — after a batch/transaction may have opened — and a store that enumerates the sequence twice re-runs the stamping pass. Same at AsyncTenantBulkStoreWrapper.cs:25.

### area: views-and-aggregation

#### SH-L328 — AggregateResult.GetBucketTime returns DateTime.MinValue instead of null when bucket_time is absent

`../Birko.Data.Stores/AggregateResult.cs:46`  ·  _restates a first-pass finding_

`GetValue<TVal>` is unconstrained, so for a value type `TVal?` resolves to plain DateTime and the `return default` path yields DateTime.MinValue, implicitly widened to the DateTime? return type. The documented "if present" semantics are unreachable: a caller cannot distinguish an un-bucketed row from a genuine MinValue bucket.

#### SH-L329 — AggregateHelper never observes its CancellationToken

`../Birko.Data.Stores/AggregateHelper.cs:36`

LinqAggregateAsync accepts `ct` and returns Task.FromResult, but never calls ThrowIfCancellationRequested — not before `filtered.ToList()` (line 47, which materializes the whole source), nor inside the per-group aggregate loop. Cancelling an aggregation over a large in-memory store has no effect; the parameter is dead.

#### SH-L330 — AggregateMath.TruncateToBucket divides by bucketTicks with no zero guard

`../Birko.Data.Stores/AggregateMath.cs:19`

`(dt.Ticks / bucketTicks) * bucketTicks` throws DivideByZeroException when bucketTicks is 0, which is exactly what TimeIntervalParser.Parse returns for an unparseable interval. AggregateHelper guards with `bucketTicks > 0`, but the method is public and is the documented bucketing entry point for other providers, so any caller that forwards a parsed interval directly crashes instead of degrading.

#### SH-L331 — ApplyOrderingAndPaging treats limit 0 as "no rows" while MongoViewStore treats it as "all rows"

`../Birko.Data.Stores/AggregateHelper.cs:159`

`if (limit.HasValue) results.Take(limit.Value)` makes Limit = 0 (or negative) return an empty list, whereas MongoViewStore.BuildQueryStages only emits `$limit` for `limit > 0` (line 111) and so returns every document for the same argument. Ordering also sorts boxed values through Comparer<object>.Default, which throws ArgumentException for any non-IComparable value.

#### SH-L332 — ToSqlInterval passes an unparsed interval string through verbatim for embedding in SQL

`../Birko.Data.Stores/TimeIntervalParser.cs:60`

When Parse yields Zero (unrecognised unit, wrong locale, arbitrary text) the original string is returned unchanged for the caller to interpolate into a SQL interval literal (its documented purpose for TimescaleDB time_bucket / date_trunc). Nothing validates the string, so a caller-supplied TimeBucketInterval reaches the SQL text unescaped and unquoted.

#### SH-L333 — OrderBy.ToDictionary collapses repeated properties and loses documented sort priority

`../Birko.Data.Stores/OrderBy.cs:76`

Fields is an ordered list, but ToDictionary() (and SqlViewStore.TranslateOrderBy, line 144) projects it into a Dictionary<string,bool>. `By(o => o.Total).ThenByDescending(o => o.Total)` silently becomes a single descending sort, and every consumer that renders `orderFields.Select(...)` (AbstractConnectorBase line 558) depends on Dictionary enumeration order, which is not a contractual ordering — multi-level sort priority is not guaranteed to survive.

#### SH-L334 — ViewMapRegistry's definition dictionary is a plain Dictionary with no synchronization

`../Birko.Data.Views/ViewMapRegistry.cs:14`

`_definitions` is a `Dictionary<Type, ViewDefinition>` mutated by Register/RegisterFromAssembly and read by GetDefinition/HasDefinition/GetAll. The type is designed as a shared registry (the ModelMapRegistry pattern, normally a DI singleton); a Register concurrent with a lookup can corrupt the buckets or throw InvalidOperationException mid-enumeration. The sibling caches in this family (_viewCache, _fieldsCache) were converted to ConcurrentDictionary for exactly this reason (CR-H094).

#### SH-L335 — RegisterFromAssembly aborts the whole scan when one mapping type has no public parameterless constructor

`../Birko.Data.Views/ViewMapRegistry.cs:38`

`Activator.CreateInstance(entry.MappingType)` throws MissingMethodException for an IViewMapping<> implementation with only a parameterized constructor, and the loop has no try/catch, so every mapping after it in the enumeration is left unregistered. The method goes to some length to tolerate unloadable types (GetLoadableTypes, CR-L240) but not uninstantiable ones; subsequent GetDefinition calls then return null and callers fall back to "view not registered".

#### SH-L336 — ClampWindowSize returns Size 0 past the result window, so a deep page silently reads as end-of-data

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:419`

For `from >= 10000` the clamp yields `size = 0`, and ExecuteSimpleQueryAsync issues that request unchanged: ES returns zero documents and the store returns an empty enumerable with no error. A caller paging beyond max_result_window concludes the view is exhausted rather than learning the window was exceeded; there is no signal to switch to search_after/scroll.

#### SH-L337 — A negative offset is forwarded to ElasticSearch as From

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:108`

`var from = offset ?? 0;` then `From = from`. ClampWindowSize normalises only its own local copy (line 424), so `QueryAsync(offset: -5)` sends `from: -5` and ES rejects the search; the store then reports it as "ElasticSearch view query failed". Mongo (offset > 0 guard) and Cosmos LINQ silently ignore the same input, so the three backends give three different answers.

#### SH-L338 — Two aggregates with the same function and source field collide in the ES aggregation dictionary

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:355`

BuildAggregationName is `{function}_{SourceProperty ?? "all"}`, which ignores ViewProperty. A definition with `Sum(o.Total → v.GrossTotal)` and `Sum(o.Total → v.NetTotal)`, or two Counts (both "count_all"), produces the same key twice and `metricAggregations.Add` (line 177) throws ArgumentException at query time. StoreAggregationHelper.BuildMetricAggregations has the same collision on ResolvedAlias (line 45). The builder never checks ViewProperty/alias uniqueness.

#### SH-L339 — MongoViewStore silently ignores limit <= 0 and offset < 0 instead of honouring or rejecting them

`../Birko.Data.MongoDB.Views/MongoViewStore.cs:111`

`$limit` is emitted only for `limit > 0` and `$skip` only for `offset > 0`. QueryAsync(limit: 0) returns EVERY matching document — the opposite of AggregateHelper.ApplyOrderingAndPaging, which returns none for the same value — and a negative offset is dropped while ES forwards it to the server. A caller computing limit arithmetically (page size 0) gets an unbounded result set from a paged API.

#### SH-L340 — MongoViewManager.EnsureAsync dereferences definition with no null check and never observes ct

`../Birko.Data.MongoDB.Views/MongoViewManager.cs:30`

`definition.QueryMode` on line 32 throws NullReferenceException for a null definition, whereas ElasticSearchViewManager and RavenViewManager both throw ArgumentNullException(nameof(definition)) first. It is also the only EnsureAsync in the five that never calls ct.ThrowIfCancellationRequested. Same gap in SqlViewManager.EnsureAsync (line 26), which does check ct but not the definition.

#### SH-L341 — Raven GroupBy fallback emits a reduce key the map never projected

`../Birko.Data.RavenDB.Views/RavenViewTranslator.cs:248`

FindViewPropertyForGroupBy returns `grp.PropertyName` when no field selector matches (the exact shape ViewDefinitionBuilder.Build permits — grouped-but-not-selected). The reduce then reads `result.Status` / `Status = g.Key` for a field BuildSelectFields never emitted, producing an index Raven rejects at PutIndexesOperation with an opaque compilation error, where SqlViewTranslator rejects the same definition up front with an explanatory NotSupportedException.

#### SH-L342 — Raven map skips Sum/Min/Max clauses with a null SourceProperty while the reduce still references them

`../Birko.Data.RavenDB.Views/RavenViewTranslator.cs:92`

BuildSelectFields guards `if (agg.SourceProperty != null)` for Sum/Min/Max/Avg and emits nothing otherwise, but BuildReduceExpression unconditionally emits `g.Sum(x => x.{ViewProperty})` for those functions (lines 208-226). A ViewDefinition constructed directly (the type's constructor is internal but reachable in-assembly, and no validation forbids it) yields an index whose reduce references a field the map does not project.

#### SH-L343 — SqlViewStore's sync path throws on a non-TView row where the async path silently skips it

`../Birko.Data.SQL.Views/SqlViewStore.cs:66`

The AbstractAsyncConnector path filters with `if (item is TView view)`, discarding anything else without a trace; the fallback path (line 71) uses `.Cast<TView>()`, which throws InvalidCastException for the same row. The same view, same data and same filter therefore behave differently depending only on whether the injected connector happens to derive from AbstractAsyncConnector.

#### SH-L344 — SqlViewTranslator silently drops an aggregate when FunctionField.CreateFunctionField returns null

`../Birko.Data.SQL.Views/SqlViewTranslator.cs:148`

Both aggregate branches are wrapped in `if (functionField != null) { view.AddField(...) }` with no else. CreateFunctionField returns `functionField!` (declared non-nullable but actually null for any function name outside COUNT/AVG/SUM/MIN/MAX), so the aggregate column vanishes from the view and the view property stays at its default — the opposite of the fail-fast discipline the surrounding CR-L201 code deliberately adopts.

#### SH-L345 — ViewDefinitionBuilder never checks that view property targets are unique across fields and aggregates

`../Birko.Data.Views/ViewDefinitionBuilder.cs:190`

ValidateAggregates only checks existence and numeric type per clause. Two aggregates targeting the same ViewProperty, or two Select calls mapping different source properties onto one view property, build without complaint and then fail differently per backend: SQL silently keeps the first (View.AddField dedup), ES throws ArgumentException from AggregationDictionary.Add, Mongo emits two $project keys for the same name (a BSON duplicate-key error).

#### SH-L346 — ES ungrouped aggregate query always returns exactly one TView, even when nothing matched

`../Birko.Data.ElasticSearch.Views/ElasticSearchViewStore.cs:274`

ParseUngroupedAggregateResponse constructs `new TView()` unconditionally and only assigns metrics that have a value, so a global aggregate over a filter matching zero documents returns one row of defaults. QueryFirstAsync therefore never returns null for such a view (contradicting its "or null" contract) and a caller cannot distinguish "no data" from "sum is 0".

#### SH-L347 — ParseGroupedBuckets declares the composite key out-parameter as non-nullable string

`../Birko.Data.ElasticSearch/Aggregation/StoreAggregationHelper.cs:292`

`bucket.Key.TryGetValue(field, out string keyValue)` binds a non-nullable string to a value the composite source may return as null, which is the CS8600 family the project bans; the sibling call in ParseAggregateResponse (line 225) correctly uses `out string? keyValue`. ElasticSearchViewStore.cs:238 repeats the non-nullable form.

### area: workflow-state-machine

#### SH-L348 — Guard predicate exceptions escape FireAsync unwrapped, unlike action exceptions

`../Birko.Workflow/Execution/WorkflowEngine.cs:51`

The guard loop (lines 49-55) runs before the try block, so an exception from user guard code propagates raw out of FireAsync — no WorkflowActionException wrapping, no workflow name/instance id, no Faulted status — while an identical throw one phase later from an action is wrapped and faults the instance. GetPermittedTriggers line 143 invokes the same predicates unprotected, so a guard that throws on an unexpected payload turns a read-only 'what can I do' query into an unhandled exception.

#### SH-L349 — ES SaveAsync doc claims CreateAsync mints its own _id; it actually keys on Guid

`../Birko.Workflow.ElasticSearch/ElasticSearchWorkflowInstanceStore.cs:36`

The <remarks> states "CreateAsync mints its own _id rather than keying on Guid, so the duplicate is silent" and that concurrent saves produce duplicate documents. AsyncElasticSearchStore.CreateCoreAsync line 111 indexes with `i => i.Id(data.Guid).Index(indexName)`, so the _id IS the instance id: a concurrent double-create overwrites (last-writer-wins lost update), it does not duplicate. The stated durability characteristic is the opposite of the code's, and the spec repeats it verbatim.

#### SH-L350 — ElasticWorkflowInstanceModel.IndexName is dead configuration

`../Birko.Workflow.ElasticSearch/Models/ElasticWorkflowInstanceModel.cs:35`

`public const string IndexName = "workflow-instances"` is never read anywhere in the repo (grep: one hit, its own declaration). The real index is ElasticSearchStoreHelper.ResolveIndexName(settings, typeof(T)) — Settings.IndexSettings or typeof(T).Name ("ElasticWorkflowInstanceModel"), never "workflow-instances". The identical const was deleted from the sibling BackgroundJobs.ElasticSearch model as CR-L023 for being "unused, misleading"; this one survived and the spec documents it as authoritative.

#### SH-L351 — Six JSON models lack the empty-HistoryJson guard the XML model has

`../Birko.Workflow.SQL/Models/WorkflowInstanceModel.cs:74`

`s.Deserialize<List<StateChangeRecord>>(HistoryJson) ?? new List<...>()` only covers a JSON `null`; an empty or whitespace HistoryJson (a migration-inserted row, or a NOT NULL DEFAULT '' column) makes System.Text.Json throw JsonException, so ToInstance fails opaquely instead of using the documented empty-history fallback. XmlWorkflowInstanceModel.cs:81 added exactly this IsNullOrWhiteSpace guard as CR-M275; it was never back-ported to SQL/JSON/ES/Mongo/Raven/Cosmos.

#### SH-L352 — ToInstance validates Guid and payload but not CurrentState or the Status range

`../Birko.Workflow.SQL/Models/WorkflowInstanceModel.cs:80`

`(WorkflowStatus)Status` is an unchecked cast, so a persisted 99 restores as an undefined enum the engine treats as neither Completed nor Faulted and accepts triggers on. CurrentState (line 79) is passed through unchecked although its default is string.Empty, so a row written without it restores an instance whose every FireAsync returns NotFound forever with no error — silent inertness. Both sit beside explicit fail-fast guards for Guid and DataJson, so the omission is asymmetric. Same in all seven models.

#### SH-L353 — WorkflowInstance.History hands out the live mutable backing list

`../Birko.Workflow/Execution/WorkflowInstance.cs:11`

`public IReadOnlyList<StateChangeRecord> History => _history;` returns the List<StateChangeRecord> itself, so any consumer can `((List<StateChangeRecord>)instance.History).Clear()` and destroy the append-only audit trail that the internal AddHistoryRecord setter exists to protect. Returning _history.AsReadOnly() is the only way the stated append-only guarantee actually holds.

#### SH-L354 — Built definitions alias the builders' mutable action/guard lists

`../Birko.Workflow/Definition/StateBuilder.cs:46`

StateBuilder.Build() passes _onEntryActions/_onExitActions (live List<> fields) straight into StateDefinition, and TransitionBuilder.Build() (line 36) does the same with _guards/_actions. Calling `.OnEntry(x)` or `.Guard(p)` on a builder after WorkflowBuilder.Build() therefore mutates the already-returned, supposedly immutable WorkflowDefinition — and since Build() can be called repeatedly, every definition from one builder shares those lists, so a later addition retroactively changes earlier definitions.

#### SH-L355 — InitialState skips the null/whitespace validation its sibling declarations perform

`../Birko.Workflow/Definition/WorkflowBuilder.cs:19`

State(name) and Transition(trigger, from, to) each throw ArgumentException on null/empty/whitespace, but InitialState(state) assigns blindly. `InitialState(null!)` leaves _initialState null so Build() reports "InitialState must be set before building." — a diagnostic that flatly contradicts the call the author made; `InitialState("  ")` reports "InitialState '  ' is not defined as a state." Both send the author looking in the wrong place.

#### SH-L356 — Builders accept null delegates and null guard reasons, deferring failure to fire time

`../Birko.Workflow/Definition/TransitionBuilder.cs:22`

Guard(predicate, reason) and Action(action) (line 28), and StateBuilder.OnEntry/OnExit (lines 32/38), perform no null checks. A null predicate throws NullReferenceException from the guard loop (WorkflowEngine.cs:51) outside the try — raw and unwrapped; a null action throws inside the try and permanently Faults the instance for a wiring mistake; `Guard(p, null!)` puts a null into TransitionResult.DenialReasons, declared IReadOnlyList<string>, which the project's no-nullable-warnings rule forbids.

#### SH-L357 — FireAsync does not validate trigger, definition, or distinguish a null instance

`../Birko.Workflow/Execution/WorkflowEngine.cs:26`

A null instance fails the `is not WorkflowInstance<TData>` pattern and is reported as ArgumentException "Instance must be created via WorkflowInstance<TData>.Create()." rather than ArgumentNullException — misdirecting the caller. A null definition throws NullReferenceException at line 40. A null or empty trigger is not rejected at all: it flows into FirstOrDefault and returns TransitionResult.NotFound, so a bug that loses the trigger reads as a legitimate 'no such transition' result, unlike WorkflowBuilder.Transition which rejects it.

#### SH-L358 — The engine never assigns Active, so a NotStarted instance stays NotStarted forever

`../Birko.Workflow/Execution/WorkflowEngine.cs:99`

The only Status assignments in the engine are Completed (line 99) and Faulted (line 116); the check at 31-38 deliberately admits NotStarted. An instance restored with Status==NotStarted (also what a Status int column holds by default, since NotStarted==0) transitions through states, accumulates history and is re-saved still reporting NotStarted — so FindByStatusAsync(Active), the natural 'find in-flight workflows' query, silently omits it while FindByStatusAsync(NotStarted) returns instances that are demonstrably running.

#### SH-L359 — AddWorkflowEngine uses AddSingleton, not TryAdd, and never null-checks configure

`../Birko.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs:17`

Both overloads unconditionally AddSingleton IWorkflowEngine and IWorkflowDiagramGenerator. Calling AddWorkflowEngine() and then AddWorkflowEngine(o => o.PublishStateChanges = true) — a library default plus an app override — leaves two IWorkflowEngine registrations where GetService silently returns the last, so whether state changes are published depends on registration order with no diagnostic. `configure(options)` at line 24 also throws NullReferenceException instead of ArgumentNullException for a null delegate.

#### SH-L360 — PublishStateChanges resolves subscribers from the captured root provider per state change

`../Birko.Workflow/Extensions/WorkflowServiceCollectionExtensions.cs:34`

The singleton factory closes over `sp` (the root provider) and calls GetServices<Action<StateChangeRecord,string,Guid>>() on every transition. A subscriber registered Scoped makes GetServices throw InvalidOperationException from the root provider — and because the callback runs inside WorkflowEngine's try block (WorkflowEngine.cs:105), that DI misconfiguration is converted into a WorkflowActionException that rolls back CurrentState and permanently Faults the instance. Resolution also repeats per invocation on the hot path.

#### SH-L361 — Mermaid escaping maps spaces to underscores, collapsing distinct states into one node

`../Birko.Workflow/Visualization/MermaidDiagramGenerator.cs:36`  ·  _restates a first-pass finding_

Escape(value) => value.Replace(" ", "_") is applied to state names and triggers, so states "In Review" and "In_Review" render as the same identifier and the diagram merges both states' edges into a single node. No other Mermaid-hostile character is escaped: a state name or trigger containing ':', '-->' or a quote produces invalid diagram source (a ':' in a name makes `A --> B : t` ambiguous).

#### SH-L362 — Mermaid emits an empty-label statement for a whitespace-only description

`../Birko.Workflow/Visualization/MermaidDiagramGenerator.cs:17`

The guard is `state.Description != null`, and EscapeDescription trims. `State("Draft").Description("")` or Description("  ") therefore emits the line `    Draft : ` with nothing after the colon, which is not a valid stateDiagram-v2 statement — one empty description makes the whole rendered diagram unparseable. The check should be string.IsNullOrWhiteSpace, matching the trimming the method already performs.

#### SH-L363 — DOT generator escapes quotes but not backslashes, and skips Mermaid's newline collapsing

`../Birko.Workflow/Visualization/DotDiagramGenerator.cs:45`

Escape(value) replaces only `"` with `\"`, leaving backslashes untouched, so a state name ending in one emits `"back\"` — the trailing backslash escapes the closing quote and the DOT source is unterminated; an embedded `\l`/`\n` is silently reinterpreted as a Graphviz label directive. Its sibling MermaidDiagramGenerator gained EscapeDescription (CR-L403) to collapse CR/LF in descriptions; the DOT label at line 22 passes the raw description through, so one generator sanitizes and the other does not.

#### SH-L364 — History timestamps bypass the framework's IDateTimeProvider seam

`../Birko.Workflow/Execution/WorkflowEngine.cs:102`

StateChangeRecord.OccurredAt is stamped with a hard-coded DateTime.UtcNow, as are CreatedAt/UpdatedAt in all seven models (e.g. WorkflowInstanceModel.cs:97-98). Birko.Time.Abstractions.IDateTimeProvider exists for this and is injected into the store decorators (TimestampStoreWrapper, AuditStoreWrapper), so the workflow subsystem is the odd one out: history ordering and UpdatedAt-descending Find* cannot be exercised deterministically, and a consumer on a logical clock gets two time sources in one save.

#### SH-L365 — SQL workflow model declares no index on the columns every Find* filters and orders by

`../Birko.Workflow.SQL/Models/WorkflowInstanceModel.cs:18`

WorkflowName, CurrentState, Status and UpdatedAt carry only [NamedField]; no [IndexedField] or class-level [CompositeIndex]. All three Find* methods filter on one of the first three and ORDER BY UpdatedAt DESC, so every query full-scans and sorts __WorkflowInstances — the table accumulating every instance of every workflow. The ElasticSearch sibling maps exactly these discriminators as Keyword/Number/Date for the same queries, so the intent is clear and only the relational backend lacks it.

#### SH-L366 — A null payload surfaces as ArgumentNullException("value") from the serializer

`../Birko.Workflow.SQL/Models/WorkflowInstanceModel.cs:95`

WorkflowInstance<TData>.Create(definition, data) does not reject a null data (WorkflowInstance.cs:23 has no constraint or null check), so an instance with Data == null is constructible and drivable. The first SaveAsync hits `s.Serialize(instance.Data)`, and SystemJsonSerializer.Serialize<T> begins with ArgumentNullException.ThrowIfNull(value) — the caller gets ArgumentNullException with ParamName "value" from inside the serializer, naming neither the workflow, the instance id, nor which of Data/History was null. ToInstance guards the mirror case explicitly.

#### SH-L367 — IWorkflowInstanceStore<TData> omits the TData : class constraint every implementation requires

`../Birko.Workflow/Core/IWorkflowInstanceStore.cs:9`

The interface is declared `IWorkflowInstanceStore<TData>` with no constraint while all seven backends declare `where TData : class`. Code written generically against the interface (a service with an unconstrained TData resolving an IWorkflowInstanceStore<TData>) compiles against the abstraction but cannot be satisfied by any shipped implementation, and a struct payload is accepted by the contract yet unpersistable — the constraint belongs on the interface.

#### SH-L368 — Build accepts transitions leaving a final state, which can never fire

`../Birko.Workflow/Definition/WorkflowBuilder.cs:80`

The validation loop checks only that FromState/ToState are declared states. Declaring `State("Closed").IsFinal()` plus `Transition("reopen", "Closed", "Draft")` builds successfully, and WorkflowDefinition.GetPermittedTriggers("Closed") reports "reopen" as available — but any instance in Closed has Status==Completed, so FireAsync throws WorkflowCompletedException at line 33 before transition lookup. The declared reopen path is unreachable configuration that the builder validates as sound and the definition API advertises.
