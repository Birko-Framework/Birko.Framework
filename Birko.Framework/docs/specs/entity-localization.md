---
area: entity-localization
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/AsyncLocalizedStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs
  - ../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs
  - ../Birko.Data.Localization/Expressions/LocalizedFilterHelper.cs
  - ../Birko.Data.Localization/Expressions/LocalizedOrderByHelper.cs
  - ../Birko.Data.Localization/Expressions/LocalizedPropertyUpdateHelper.cs
  - ../Birko.Data.Localization/Filters/EntityTranslationFilter.cs
  - ../Birko.Data.Localization/Models/EntityTranslationModel.cs
  - ../Birko.Data.Localization/Models/IEntityLocalizationContext.cs
  - ../Birko.Data.Localization/Models/ILocalizable.cs
source-commits:   # sibling HEADs when this spec was last written (2026-07-30 16:07:38,
                  # commit acbbe9d). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.Localization: e2e01bb
shaped-by: []
shaped-by-derived: true
shaped-by-unresolved: 80
---

# Entity-level localization via store decoration

## Purpose

This capability lets an application store one entity row per record while serving and accepting
*translated* values for selected string properties, without any backend-specific support. It works by
wrapping an existing store in a decorator (`LocalizedStoreWrapper`, `LocalizedBulkStoreWrapper`, and
their async twins). The wrapper keeps every non-default culture in a side table of
`EntityTranslationModel` rows keyed by `(EntityGuid, EntityType, FieldName, Culture)` and reads the
entity's own columns as the **default-culture** copy of the data whenever a row is missing. Those
columns are *not* preserved as a default-culture copy on write: every write path hands the entity to the
inner store exactly as the caller holds it, so on a non-default culture the localized value is persisted
into the entity's own columns as well as into a translation row, and nothing restores the previous
default-culture value.

On read, the wrapper overwrites the entity's localizable properties in place with the current culture's
translation. On write, it persists the incoming values as translation rows. Filters that mention a
localizable property are rewritten: the predicate is evaluated against the translation table in memory,
and the localized part of the filter is replaced with a GUID-membership test that the inner store can
execute. Ordering on a localizable property forces the whole matching set to be loaded, translated,
sorted and paged in memory.

The consumer supplies two things: an entity type implementing `ILocalizable.GetLocalizableFields()`,
and an `IEntityLocalizationContext` giving `CurrentCulture` and `DefaultCulture`. The project ships no
implementation of that context and no dependency-injection registration helper — both are the
consumer's responsibility.

## Requirements

### Requirement: Localizable entity and translation row contract

The system SHALL treat a property as localizable only when its name is returned by
`ILocalizable.GetLocalizableFields()` on the entity type, and SHALL store each translated value as one
`EntityTranslationModel` carrying `EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value` and
`UpdatedAt`, where `EntityType` is the runtime `Type.Name` of the entity instance and `FieldName` is the
property name. Wrapped entity types SHALL derive from `Birko.Data.Models.AbstractModel` and implement
`ILocalizable`; the translation store SHALL be a bulk store of `EntityTranslationModel`
(`IBulkStore<EntityTranslationModel>` for the sync wrappers, `IAsyncBulkStore<EntityTranslationModel>`
for the async wrappers) even when the wrapped entity store is non-bulk.

#### Scenario: Translation row identity

- **Given** an entity `Product` with `Guid = G` whose `GetLocalizableFields()` returns `["Name", "Description"]`
- **When** the wrapper persists translations for culture `"sk"`
- **Then** it writes at most one `EntityTranslationModel` per field, each with `EntityType = "Product"`, `FieldName = "Name"` / `"Description"`, `Culture = "sk"` and `EntityGuid = G`

#### Scenario: Translation model copies all its own fields

- **Given** an `EntityTranslationModel` instance
- **When** `CopyTo(null)` is called
- **Then** a new `EntityTranslationModel` is returned with `EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value` and `UpdatedAt` copied, after `AbstractModel.CopyTo` has copied the base fields

#### Scenario: Localizable field list is read from a fresh instance for filter and order-by decisions

- **Given** a wrapper whose inner store's `CreateInstance()` returns a new entity
- **When** the wrapper needs the localizable field list for filter rewriting, order-by detection or `PropertyUpdate` inspection
- **Then** it calls `_innerStore.CreateInstance().GetLocalizableFields()` (a fresh instance), whereas translation application and persistence call `GetLocalizableFields()` on the *entity being processed*

### Requirement: Culture gating

The system SHALL consider the current culture non-default when
`_context.CurrentCulture.Name != _context.DefaultCulture.Name` — an ordinal, case-sensitive string
comparison of the culture *names* — and SHALL perform no translation reads, no translation writes and no
filter rewriting while the current culture is the default one.

#### Scenario: Default culture is a pass-through for reads

- **Given** `CurrentCulture.Name == DefaultCulture.Name == "en"`
- **When** `ReadAsync(filter)` is called on `AsyncLocalizedStoreWrapper`
- **Then** the inner store's `ReadAsync(filter)` result is returned unchanged, no translation store query is issued, and the filter is not rewritten

#### Scenario: Default culture is a pass-through for writes

- **Given** `CurrentCulture.Name == DefaultCulture.Name`
- **When** `Create(entity)` or `Update(entity)` is called
- **Then** the inner store is called and `SaveTranslations` returns immediately without touching the translation store

#### Scenario: Culture names differing only in case are treated as different cultures

- **Given** `DefaultCulture.Name == "en-US"` and `CurrentCulture.Name == "EN-US"`
- **When** any wrapper method runs
- **Then** `IsNonDefaultCulture()` returns true and the full localization path (translation reads/writes, filter rewriting) is taken

#### Scenario: Delete always removes translations regardless of culture

- **Given** `CurrentCulture.Name == DefaultCulture.Name`
- **When** `Delete(entity)` is called
- **Then** `DeleteTranslations` still runs and deletes the entity's translation rows, because it has no culture gate

### Requirement: Applying translations on read

The system SHALL, on a non-default culture, load every translation row for the read entity's GUID and the
current culture — via `EntityTranslationFilter.ByEntityAndCulture`, which leaves `EntityType`
unconstrained — build a `FieldName → Value` dictionary, and overwrite the entity's localizable properties
in place with the matching values.

#### Scenario: Read by GUID returns the translated entity

- **Given** entity `G` has base `Name = "Chair"` and a translation row `(G, "Name", "sk", "Stolička")`, with `CurrentCulture = "sk"`
- **When** `ReadAsync(G)` is called
- **Then** the returned entity's `Name` is `"Stolička"`

#### Scenario: Entity without translations keeps its base values

- **Given** entity `G` has no translation rows for the current culture
- **When** the entity is read
- **Then** the translation dictionary is empty, the method returns early, and every base property is left untouched

#### Scenario: Entity with a null GUID is skipped

- **Given** an entity whose `Guid` is `null`
- **When** `ApplyTranslations` runs
- **Then** it returns without querying the translation store

#### Scenario: Only writable string properties are assigned

- **Given** `GetLocalizableFields()` names a property that is not `typeof(string)`, is read-only, or does not exist on the runtime type
- **When** a translation exists for that field name
- **Then** the value is not assigned and no exception is raised (the property lookup uses `BindingFlags.Public | BindingFlags.Instance` and requires `PropertyType == typeof(string) && CanWrite`)

#### Scenario: Duplicate translation rows resolve to the last one enumerated

- **Given** two translation rows exist for the same `(EntityGuid, FieldName, Culture)` with values `"A"` then `"B"` in the store's enumeration order
- **When** the translation dictionary is built by `translationDict[t.FieldName] = t.Value`
- **Then** the applied value is `"B"` — the last row enumerated wins, with no defined ordering guarantee

#### Scenario: Null entity is not dereferenced

- **Given** the inner store returns `null` for a read
- **When** `Read(guid)` / `ReadAsync(guid)` / `Read(filter)` returns
- **Then** `ApplyTranslations` is not called and `null` is propagated

#### Scenario: The read lookup ignores entity type while filter resolution does not

- **Given** translation rows written with `EntityType = entity.GetType().Name`, read through a wrapper whose `T` is a base type of the stored instance
- **When** `ApplyTranslations` loads them and, separately, `ResolveMatchingGuids` resolves a localized filter condition
- **Then** the read lookup matches on `(EntityGuid, Culture)` only and applies the rows regardless of their `EntityType`, while the filter resolution constrains `EntityType = typeof(T).Name` and therefore matches no row — the two paths read the same rows through different entity-type criteria

### Requirement: Bulk reads translate every returned entity

The system SHALL apply translations to each entity of a bulk read result, materializing the inner store's
sequence to a list first.

#### Scenario: Read-all translates each entity

- **Given** `CurrentCulture` is non-default and the inner store returns three entities from `Read()` / `ReadAsync(ct)`
- **When** the parameterless bulk read completes
- **Then** all three entities are materialized to a list and each is passed through `ApplyTranslations`

#### Scenario: Default-culture bulk read short-circuits

- **Given** `CurrentCulture.Name == DefaultCulture.Name`
- **When** `LocalizedBulkStoreWrapper.Read(filter, orderBy, limit, offset)` is called
- **Then** the inner store's result is materialized and returned with no per-entity translation loop, while `AsyncLocalizedBulkStoreWrapper.ReadAsync(filter, orderBy, limit, offset)` still runs the loop — whose body returns immediately, so the observable result is identical

#### Scenario: Single-result bulk read reaches the hidden overload by cast

- **Given** `LocalizedBulkStoreWrapper<TStore, T>` wrapping a bulk inner store
- **When** its `Read(Expression<Func<T,bool>>? filter)` single-result overload runs
- **Then** it casts the inner store to `IReadStore<T>` before calling `Read(filter)`, so the bulk collection-returning `Read` overload is not selected

### Requirement: Filter rewriting for localized field conditions

The system SHALL, on a non-default culture, split a filter into conditions on localizable fields and the
remaining conditions; resolve the localized conditions against the translation store to a set of entity
GUIDs; replace them with a GUID-membership predicate; and `AndAlso`-combine that predicate with the
remaining filter. When the split finds no localized conditions the original filter SHALL be passed through
unchanged (including `null`).

#### Scenario: Mixed filter is split and recombined

- **Given** `Name` is localizable and the filter is `x => x.Name == "Stolička" && x.Active`
- **When** `RewriteFilter` runs on culture `"sk"`
- **Then** the translation store is queried for `(EntityType = "Product", FieldName = "Name", Culture = "sk")`, the matching GUIDs `{G1}` are collected, and the inner store receives `x => x.Active && (x.Guid != null && guidList.Contains(x.Guid.Value))`

#### Scenario: Fully localized filter becomes only the GUID predicate

- **Given** the filter is `x => x.Name == "Stolička"` and nothing else
- **When** `RewriteFilter` runs
- **Then** `split.RemainingFilter` is `null` and the GUID filter alone is passed to the inner store

#### Scenario: Filter with no localized condition is untouched

- **Given** the filter is `x => x.Active` and `Active` is not localizable
- **When** `RewriteFilter` runs
- **Then** the same expression instance is returned and no translation store query is issued

#### Scenario: Null filter with localizable fields present

- **Given** `filter == null`
- **When** `LocalizedExpressionAnalyzer.Split(null, fields)` runs
- **Then** it returns a split with `RemainingFilter = null` and no localized conditions, so `RewriteFilter` returns `null` and the inner store performs an unfiltered read

#### Scenario: Entity type with no localizable fields

- **Given** `GetLocalizableFields()` returns an empty list
- **When** `Split` runs
- **Then** it returns immediately with `RemainingFilter = filter` and no localized conditions

### Requirement: Multiple localized conditions are intersected

The system SHALL query the translation store once per extracted localized condition and intersect the
resulting GUID sets, so an entity must satisfy every localized condition.

#### Scenario: Two localized conditions on different fields

- **Given** `Name` and `Description` are localizable and the filter is `x => x.Name.Contains("a") && x.Description.Contains("b")`
- **When** the conditions are resolved
- **Then** two translation queries run and the final GUID set is the intersection of the two match sets

#### Scenario: Two localized conditions on the same field

- **Given** the filter is `x => x.Name.StartsWith("St") && x.Name.EndsWith("ka")`
- **When** the conditions are resolved
- **Then** both are extracted independently and their GUID sets are intersected, so only entities whose `"sk"` `Name` translation satisfies both remain

#### Scenario: A localized condition matching nothing empties the intersection

- **Given** one condition matches `{G1, G2}` and another matches no translation values
- **When** the sets are intersected
- **Then** the result is an empty GUID set

### Requirement: GUID membership filter construction

The system SHALL build the membership predicate as `x => x.Guid != null && guidList.Contains(x.Guid.Value)`
over a `List<Guid>` (not the `HashSet<Guid>` it was given), and SHALL build `x => false` when the GUID set
is empty.

#### Scenario: Non-empty set builds a list-backed Contains

- **Given** a GUID set with two entries
- **When** `LocalizedFilterHelper.BuildGuidFilter<T>` runs
- **Then** the returned lambda closes over a `List<Guid>` constant and calls `List<Guid>.Contains(Guid)`, guarded by a `Guid != null` null check

#### Scenario: Empty set matches nothing

- **Given** no translation row satisfied any localized condition
- **When** `BuildGuidFilter<T>` receives the empty set
- **Then** it returns `x => false`, so the inner store returns no rows and `Count` returns 0

#### Scenario: Large GUID sets are not chunked

- **Given** a localized condition matches 50 000 entities
- **When** the membership filter is built
- **Then** all 50 000 GUIDs are placed in a single `List<Guid>` constant with no chunking, which a SQL backend renders as one oversized `IN` list

#### Scenario: Combining rebinds the right-hand parameter

- **Given** two lambdas with distinct `ParameterExpression` instances
- **When** `LocalizedFilterHelper.CombineFilters(left, right)` runs
- **Then** the right body's parameter is rewritten to the left lambda's parameter by `ParameterReplacer` and the two bodies are joined with `AndAlso`

### Requirement: Localized condition extraction support matrix

The system SHALL extract a localized condition only from a top-level `AndAlso`-chain part that is either a
binary `Equal` / `NotEqual` between a direct `string` property of the lambda parameter named in the
localizable set and a non-null string constant, or an instance method call named `Contains`, `StartsWith`
or `EndsWith` on such a property whose first argument evaluates to a string. All other parts SHALL remain
in the residual filter and therefore be evaluated by the inner store against the **base column**.

#### Scenario: Equality on a localizable field is extracted

- **Given** the filter part is `x.Name == "Stolička"`
- **When** `TryExtractLocalizedCondition` runs
- **Then** a condition with `FieldName = "Name"` and predicate `v => v == "Stolička"` is produced

#### Scenario: Reversed operand order is supported

- **Given** the filter part is `"Stolička" == x.Name`
- **When** extraction runs
- **Then** the left/right pair is retried in reverse order and the same condition is produced

#### Scenario: Comparison operators other than equality are not extracted

- **Given** the filter part is `x.Name.CompareTo("A") > 0` expressed as a binary `GreaterThan` on the property
- **When** extraction runs
- **Then** the operator falls to the `_ => null!` switch arm, no condition is produced, and the part stays in the residual filter

#### Scenario: Null comparison stays on the base column

- **Given** `Name` is localizable and the filter is `x => x.Name == null`
- **When** extraction runs
- **Then** the evaluated constant is `null`, the `constantValue != null` guard fails, the part is left in the residual filter, and the inner store tests the entity's own `Name` column rather than the translation

#### Scenario: Disjunctions are never localized

- **Given** the filter is `x => x.Name == "Stolička" || x.Code == "C1"`
- **When** `FlattenAndAlso` runs
- **Then** the whole `OrElse` node is one indivisible part, no localized condition is extracted from it, and the inner store evaluates `x.Name == "Stolička"` against the base column

#### Scenario: Negation is never localized

- **Given** the filter is `x => !x.Name.Contains("a")`
- **When** extraction runs
- **Then** the `Not` node matches neither the binary nor the method-call shape, so it stays in the residual filter and is evaluated against the base column

#### Scenario: Nested member access is not localized

- **Given** the filter is `x => x.Owner.Name == "Ann"` where `member.Expression` is not the lambda parameter
- **When** `ExtractFieldName` runs
- **Then** it returns `null` because `member.Expression == param` fails, and the part stays in the residual filter

#### Scenario: A StringComparison argument is ignored

- **Given** the filter is `x => x.Name.Contains("st", StringComparison.OrdinalIgnoreCase)`
- **When** extraction runs
- **Then** the condition IS extracted (method name matches and `Arguments[0]` is a string) and the predicate is the default case-sensitive `v.Contains("st")` — the comparison argument is discarded

#### Scenario: Method-name matching is not overload-checked

- **Given** the localizable property's declaring type exposes any instance method named `Contains`, `StartsWith` or `EndsWith` whose first argument is a string
- **When** extraction runs
- **Then** the condition is extracted purely on `methodCall.Method.Name`, without verifying the declaring type or overload

#### Scenario: String method predicates guard against null translation values

- **Given** a translation row whose `Value` is `null`
- **When** a `Contains` / `StartsWith` / `EndsWith` predicate is evaluated against it
- **Then** the predicate returns false because of its `v != null &&` guard, instead of throwing `NullReferenceException`

### Requirement: Constant evaluation for extracted conditions

The system SHALL resolve the compared value from a `ConstantExpression` directly, from a field or property
access on a `ConstantExpression` (a closure capture) by reflection, and otherwise by compiling and invoking
the subexpression; any exception during compile-and-invoke SHALL be swallowed and yield `null`.

#### Scenario: Captured local variable is resolved

- **Given** `var q = "Stolička";` and the filter `x => x.Name == q`
- **When** `EvaluateExpression` runs on the closure member access
- **Then** the field value `"Stolička"` is read from the compiler-generated closure constant

#### Scenario: Compile-and-invoke fallback

- **Given** the compared operand is `someObject.Prop.Nested`
- **When** neither the constant nor the single-level closure shape matches
- **Then** `Expression.Lambda(expression).Compile().DynamicInvoke()` is used to obtain the value

#### Scenario: Evaluation failure degrades to a base-column predicate

- **Given** the compared operand throws when invoked (for example it dereferences null)
- **When** `EvaluateExpression` catches the exception and returns `null`
- **Then** no localized condition is extracted, the part stays in the residual filter, and the condition silently applies to the base column with no diagnostic

#### Scenario: Non-string constants are rejected

- **Given** the filter is `x => x.Name == 5` compiled through an object comparison
- **When** `ExtractFieldAndConstant` casts the value with `as string`
- **Then** the result is `null` and the part is not extracted

### Requirement: Not-equal on a localizable field matches only entities that have a translation row

The system SHALL, for an extracted `NotEqual` condition, return only the GUIDs of translation rows whose
`Value` differs from the constant, so entities with no translation row for that field and culture are
excluded from the result rather than treated as "value differs".

#### Scenario: Entity without a translation is excluded from a not-equal match

- **Given** entities `G1` (translation `Name = "A"` for `"sk"`) and `G2` (no `"sk"` translation for `Name`), and the filter `x => x.Name != "A"`
- **When** the filter is rewritten on culture `"sk"`
- **Then** the resolved GUID set is empty and neither entity is returned, even though `G2`'s value is not `"A"`

### Requirement: Count applies the same rewriting

The system SHALL rewrite the filter before delegating to the inner store's `Count`, and SHALL delegate the
original filter unchanged on the default culture.

#### Scenario: Count on a localized filter

- **Given** `CurrentCulture` is non-default and the filter mentions a localizable field
- **When** `Count(filter)` / `CountAsync(filter)` runs
- **Then** the rewritten GUID-based filter is passed to the inner store's count

#### Scenario: Count with no localized matches

- **Given** the localized condition resolves to an empty GUID set
- **When** `Count` delegates `x => false` to the inner store
- **Then** the count is 0

### Requirement: Ordering on a localizable field forces in-memory sort and paging

The system SHALL detect whether any `OrderBy<T>.Fields` entry's `PropertyName` is localizable; when none
is, it SHALL pass `orderBy`, `limit` and `offset` to the inner store; when one is, it SHALL read **all**
entities matching the rewritten filter with no limit or offset, apply translations, sort in memory, and
then apply `offset`/`limit` in memory.

#### Scenario: Ordering on a non-localized field is pushed down

- **Given** `orderBy` sorts by `CreatedAt` and no localizable field
- **When** `ReadAsync(filter, orderBy, limit, offset)` runs on a non-default culture
- **Then** the rewritten filter, `orderBy`, `limit` and `offset` are all handed to the inner store, and translations are applied to the returned page

#### Scenario: Ordering on a localized field loads everything

- **Given** `orderBy` sorts by the localizable `Name` and `limit = 10, offset = 20`
- **When** the bulk read runs on a non-default culture
- **Then** the inner store is called with only the rewritten filter (no order, no limit, no offset), every matching entity is translated, the list is sorted in memory, and `Skip(20).Take(10)` is applied afterwards

#### Scenario: Null orderBy never triggers in-memory sorting

- **Given** `orderBy == null`
- **When** `LocalizedOrderByHelper.ReferencesLocalizedField` runs
- **Then** it returns false and the read is delegated with the rewritten filter, limit and offset

#### Scenario: In-memory paging with no bounds

- **Given** `offset == null` and `limit == null`
- **When** `ApplyInMemoryPaging` runs
- **Then** the full list is returned as a new list, with neither `Skip` nor `Take` applied

#### Scenario: Negative paging bounds

- **Given** `offset = -5` and `limit = -1`
- **When** `ApplyInMemoryPaging` runs
- **Then** `Skip(-5)` skips nothing and `Take(-1)` yields nothing, so the result is an empty list

### Requirement: In-memory ordering semantics

The system SHALL sort in memory by reflecting each `OrderBy` field's property value, using a defensive
comparer that orders `null` first, compares same-typed `IComparable` values directly, and otherwise falls
back to `string.CompareOrdinal` of `ToString()`. Fields whose property cannot be found on `typeof(T)`
SHALL be skipped.

#### Scenario: Multi-field ordering builds a ThenBy chain

- **Given** `orderBy.Fields` is `[{Name, ascending}, {Code, descending}]` and both properties exist
- **When** `ApplyInMemoryOrderBy` runs
- **Then** the list is ordered by `Name` ascending then `Code` descending, using `SafeObjectComparer` for both keys

#### Scenario: Null keys sort first

- **Given** two entities, one with `Name == null`
- **When** ascending ordering is applied
- **Then** the null-valued entity comes first (`Compare` returns -1 for a null `x`)

#### Scenario: Mismatched key types do not throw

- **Given** two key values of different runtime types
- **When** `SafeObjectComparer.Compare` runs
- **Then** it falls back to `string.CompareOrdinal(x.ToString(), y.ToString())` instead of throwing `InvalidOperationException`

#### Scenario: Trivial input is returned unchanged

- **Given** the list holds one or zero entities, or `orderBy.Fields` is empty
- **When** `ApplyInMemoryOrderBy` runs
- **Then** the same list instance is returned without sorting

#### Scenario: First order-by field missing while a later one exists

- **Given** `orderBy.Fields` is `[{"NoSuchProp"}, {"Name"}]`
- **When** `ApplyInMemoryOrderBy` iterates
- **Then** index 0 is skipped by `continue`, index 1 takes the `else` branch and dereferences the still-null `ordered` via `ordered!.ThenBy(...)`, raising `NullReferenceException`

#### Scenario: All order-by fields missing

- **Given** every `PropertyName` is absent from `typeof(T)`
- **When** the loop completes with `ordered == null`
- **Then** `ordered?.ToList() ?? entities` returns the unsorted input list

### Requirement: Translation persistence on create and update

The system SHALL, after the inner store's write succeeds and only on a non-default culture, upsert one
translation row per localizable string property whose value is not null: an existing row for
`(EntityGuid, FieldName, Culture)` is updated in place with the new `Value` and `UpdatedAt = DateTime.UtcNow`,
otherwise a new row is created with the same fields plus `EntityType` from the runtime type name.

#### Scenario: First write creates a translation row

- **Given** `CurrentCulture = "sk"`, `DefaultCulture = "en"`, and no existing `"sk"` translation for entity `G`
- **When** `Create(entity)` completes with `entity.Name = "Stolička"`
- **Then** the inner store is called first, then one `EntityTranslationModel` is created with `Value = "Stolička"` and `UpdatedAt` set to the current UTC time

#### Scenario: Second write updates the existing row

- **Given** a translation row already exists for `(G, "Name", "sk")`
- **When** `Update(entity)` runs with a new `Name`
- **Then** the existing row's `Value` and `UpdatedAt` are overwritten and the translation store's `Update` is called — no second row is created

#### Scenario: Only the first duplicate row is updated

- **Given** two translation rows exist for the same `(G, "Name", "sk")`
- **When** the upsert calls `existing.FirstOrDefault()`
- **Then** only the first row enumerated is updated and the other is left stale

#### Scenario: Null property value leaves the previous translation in place

- **Given** entity `G` has a `"sk"` translation `Name = "Stolička"` and the caller sets `entity.Name = null`
- **When** `SaveTranslations` reaches `Name`
- **Then** the `value == null` guard skips the field, the old translation row is neither updated nor deleted, and a later read re-applies `"Stolička"` to the entity

#### Scenario: Empty string is persisted

- **Given** `entity.Name = ""`
- **When** `SaveTranslations` runs
- **Then** the translation row's `Value` becomes `""` (only `null` is skipped)

#### Scenario: Non-string localizable field is skipped

- **Given** `GetLocalizableFields()` names an `int` property
- **When** `SaveTranslations` reflects the property
- **Then** the `PropertyType != typeof(string)` guard skips it with no exception

#### Scenario: Entity with no GUID persists no translations

- **Given** the inner store's `Create` returns a GUID but does not set `data.Guid` on the passed instance
- **When** `SaveTranslations(data)` runs
- **Then** the `entity.Guid == null` guard returns immediately and the translation row is silently not written

#### Scenario: Timestamps come from the system clock

- **Given** any translation upsert
- **When** `UpdatedAt` is assigned
- **Then** the value is `DateTime.UtcNow` read directly, not obtained from an injected `IDateTimeProvider`

#### Scenario: The localized value is also written to the entity's own column

- **Given** `CurrentCulture = "sk"`, `DefaultCulture = "en"`, entity `G` whose stored `Name` is `"Chair"`, and the caller setting `entity.Name = "Stolička"`
- **When** `Create(entity)` / `Update(entity)` runs
- **Then** the entity is handed to the inner store exactly as the caller holds it, so `"Stolička"` is persisted into the entity's own `Name` column, and the `"sk"` translation row is written afterwards with the same value — the default-culture `"Chair"` is neither preserved nor restored

### Requirement: Bulk create and update persist translations per item

The system SHALL materialize the incoming sequence exactly once before handing it to the inner store, then
iterate the same materialized list to persist translations.

#### Scenario: One-shot enumerable is not enumerated twice

- **Given** a lazily-generated `IEnumerable<T>` that can only be enumerated once
- **When** `Create(data)` / `CreateAsync(data)` / `Update(data)` / `UpdateAsync(data)` runs
- **Then** it is materialized via `data as IList<T> ?? data.ToList()` and the same list is passed to the inner store and used for the translation loop

#### Scenario: Existing list is reused without copying

- **Given** the caller passes an `IList<T>`
- **When** the materialization runs
- **Then** the same instance is used (the `as IList<T>` branch), with no defensive copy

### Requirement: Deleting an entity deletes all of its translations

The system SHALL, after the inner store deletes the entity, delete every translation row matching the
entity's GUID — across all cultures, all field names and without filtering by entity type — and SHALL skip
the deletion when the entity's GUID is null.

#### Scenario: All cultures are removed

- **Given** entity `G` has `"sk"` and `"de"` translations
- **When** `Delete(entity)` / `DeleteAsync(entity)` runs
- **Then** `EntityTranslationFilter.ByEntity(G)` selects both rows and they are removed with a single bulk delete

#### Scenario: Bulk delete of a collection

- **Given** three entities are passed to `Delete(IEnumerable<T>)`
- **When** the call completes
- **Then** the sequence is materialized once, the inner store deletes all three, and `DeleteTranslations` runs once per entity

#### Scenario: Entity without a GUID

- **Given** an entity whose `Guid` is null
- **When** `DeleteTranslations` runs
- **Then** it returns without querying or deleting anything

### Requirement: Filter-based bulk delete reads before deleting and does not localize the filter

The system SHALL implement `Delete(filter)` by reading the matching entities from the inner store with the
**original, un-rewritten** filter, deleting that materialized set through the inner store, and then deleting
each entity's translations — never using the inner store's native filter-delete.

#### Scenario: Delete by filter cascades to translations

- **Given** two entities match `x => x.Active == false`
- **When** `Delete(filter)` / `DeleteAsync(filter)` runs
- **Then** both entities are read, deleted as a collection, and each one's translation rows are removed

#### Scenario: Delete by a filter on a localizable field targets the base column

- **Given** `CurrentCulture = "sk"`, `Name` is localizable, and the filter is `x => x.Name == "Stolička"`
- **When** `Delete(filter)` runs
- **Then** `RewriteFilter` is **not** invoked and the inner store matches against the entity's own default-culture `Name` column, so the set deleted differs from what the equivalent `Read(filter)` would have returned

### Requirement: Filter-based bulk update with an action

The system SHALL implement `Update(filter, Action<T>)` by applying the caller's action and then persisting
translations for each affected entity, using the **original, un-rewritten** filter; the sync and async
wrappers SHALL do so by different mechanisms.

#### Scenario: Sync wrapper delegates to the inner store's native filter update

- **Given** `LocalizedBulkStoreWrapper.Update(filter, updateAction)`
- **When** it runs
- **Then** it calls `_innerStore.Update(filter, item => { updateAction(item); SaveTranslations(item); })` exactly once, so the entity selection, iteration and persistence are owned by the inner store and translations are written from inside its mutation callback

#### Scenario: Async wrapper does read-modify-write per entity

- **Given** `AsyncLocalizedBulkStoreWrapper.UpdateAsync(filter, updateAction)`
- **When** it runs
- **Then** it reads all matching entities via `_innerStore.ReadAsync(filter, null, null, null, ct)`, then for each entity applies the action, calls `_innerStore.UpdateAsync(item)` and `SaveTranslationsAsync(item)` — one inner update per entity

#### Scenario: Filter-based update on a non-default culture also rewrites the base column

- **Given** `CurrentCulture = "sk"`, `Name` is localizable, and `Update(filter, e => e.Name = "Stolička")`
- **When** the update completes
- **Then** the inner store persists `Name = "Stolička"` into the entity's own (default-culture) column **and** a `"sk"` translation row is written with the same value, so the default-culture value is overwritten with the Slovak text

#### Scenario: Entities read for the update are not translated first

- **Given** the async wrapper reads matching entities through `_innerStore.ReadAsync`
- **When** the caller's action inspects `item.Name`
- **Then** it sees the base (default-culture) value, because `ApplyTranslationsAsync` is not applied on this path

### Requirement: Native PropertyUpdate falls back when it touches a localizable field

The system SHALL inspect a `PropertyUpdate<T>`'s assignments and, when the current culture is non-default
and at least one assignment targets a localizable field, convert the update to an `Action<T>` via
`PropertyUpdate.ApplyTo` and route it through the read-modify-write path; otherwise it SHALL delegate the
native `PropertyUpdate` straight to the inner store.

#### Scenario: PropertyUpdate on a non-localizable field stays native

- **Given** the update assigns only `x => x.Active`
- **When** `Update(filter, updates)` runs on a non-default culture
- **Then** `TouchesLocalizableField` returns false and `_innerStore.Update(filter, updates)` is called, preserving the backend's native SET

#### Scenario: PropertyUpdate on a localizable field falls back

- **Given** `Name` is localizable and the update assigns `x => x.Name`
- **When** `Update(filter, updates)` runs on a non-default culture
- **Then** it is rewritten to `Update(filter, LocalizedPropertyUpdateHelper.ToAction(updates))`, so the translation row is written rather than only the base column

#### Scenario: Default culture always uses the native path

- **Given** `CurrentCulture.Name == DefaultCulture.Name` and the update assigns a localizable field
- **When** `Update(filter, updates)` runs
- **Then** the `IsNonDefaultCulture()` guard short-circuits and the native `PropertyUpdate` is delegated unchanged

#### Scenario: Boxing conversions in the assignment selector are unwrapped

- **Given** an assignment lambda whose body is a `UnaryExpression` convert wrapping the member access (for example a value-typed property selected as `object`)
- **When** `GetMemberName` inspects it
- **Then** it unwraps `unary.Operand as MemberExpression` and still recognises the property name

#### Scenario: No assignments or no localizable fields

- **Given** `updates.Assignments.Count == 0`, or the localizable field list is empty
- **When** `TouchesLocalizableField` runs
- **Then** it returns false without inspecting anything

### Requirement: Save dispatches on GUID presence

The system SHALL treat a null or `Guid.Empty` GUID as a create and anything else as an update, and SHALL
return a GUID whose source differs between the sync and async wrappers.

#### Scenario: Empty GUID creates

- **Given** `data.Guid` is `null` or `Guid.Empty`
- **When** `Save(data)` / `SaveAsync(data)` runs
- **Then** the create path runs, including translation persistence

#### Scenario: Populated GUID updates

- **Given** `data.Guid` is a non-empty GUID
- **When** `Save(data)` / `SaveAsync(data)` runs
- **Then** the update path runs and the method returns `data.Guid ?? Guid.Empty`

#### Scenario: Return value on create diverges between sync and async

- **Given** an inner store that returns a new GUID from `Create`/`CreateAsync` without assigning it to the passed instance
- **When** `Save(data)` returns versus `SaveAsync(data)` returns
- **Then** the sync wrappers return the inner store's GUID (`return Create(data, storeDelegate);`), while the async wrappers discard it and return `data.Guid ?? Guid.Empty` — i.e. `Guid.Empty`

### Requirement: Translation filter composition

The system SHALL express an `EntityTranslationFilter` as a single conjunction in which each of
`EntityGuid`, `EntityType`, `FieldName` and `Culture` is skipped when the filter property is `null`, and
SHALL provide factory methods `ByEntity`, `ByEntityAndCulture`, `ByEntityFieldAndCulture`, `ByEntityType`
and `ByEntityTypeAndCulture`.

#### Scenario: All-null filter matches every translation

- **Given** a default-constructed `EntityTranslationFilter`
- **When** `ToExpression()` is evaluated
- **Then** every clause short-circuits to true and the expression matches all rows

#### Scenario: Field-and-culture lookup during resolution

- **Given** the wrapper resolves localized conditions for entity type `"Product"`, field `"Name"`, culture `"sk"`
- **When** the filter is built with `EntityType`, `FieldName` and `Culture` set but `EntityGuid` left null
- **Then** the expression constrains those three columns and leaves the entity GUID unconstrained, returning every `"sk"` `Name` translation of every `Product`

#### Scenario: Filter criteria are closure captures, not inlined constants

- **Given** `ToExpression()` is called
- **When** the resulting expression tree is inspected
- **Then** the criteria appear as member accesses on the filter instance (`EntityGuid`, `EntityType`, `FieldName`, `Culture`), so a backend query translator must funcletize them

#### Scenario: Localized condition values are filtered in memory, not by the backend

- **Given** a localized condition with a `Contains` predicate
- **When** the conditions are resolved
- **Then** the translation store is queried by entity type, field and culture only, and `condition.ValuePredicate` is applied client-side over the returned rows via `translations.Where(...)`

### Requirement: Wrapper construction and inner-store access

The system SHALL reject a null inner store, translation store or localization context with
`ArgumentNullException`, SHALL delegate `Init`/`InitAsync`, `Destroy`/`DestroyAsync` and `CreateInstance`
straight to the inner store and to the inner store only — the translation store is never initialized or
destroyed by the wrapper — and SHALL expose the inner store via `IStoreWrapper.GetInnerStore()` and
`GetInnerStoreAs<TInner>()`.

#### Scenario: Null constructor argument

- **Given** a null `translationStore`
- **When** the wrapper is constructed
- **Then** `ArgumentNullException` is thrown naming `translationStore`

#### Scenario: Lifecycle calls are pass-through

- **Given** any localized wrapper
- **When** `Init()` / `InitAsync()` / `Destroy()` / `DestroyAsync()` / `CreateInstance()` is called
- **Then** the call is forwarded to the inner store with no localization behaviour added

#### Scenario: Typed unwrapping is single-level

- **Given** a chain `LocalizedStoreWrapper → TenantStoreWrapper → ConcreteStore`
- **When** `GetInnerStoreAs<ConcreteStore>()` is called on the outermost wrapper
- **Then** it returns `null`, because the implementation is `_innerStore as TInner` with no recursion into further wrappers

#### Scenario: The translation store is not part of the lifecycle

- **Given** a localized wrapper whose entities have translation rows
- **When** `Init()` / `InitAsync()` and later `Destroy()` / `DestroyAsync()` are called
- **Then** only the inner store is initialized and destroyed; the translation store receives neither call, so its rows survive the wrapper's `Destroy()`
