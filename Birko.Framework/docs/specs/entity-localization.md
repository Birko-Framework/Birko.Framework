---
area: entity-localization
generated-at: 451c62884ec51420f5b68b6a970f9b7178e0fa00
generated-on: 2026-10-19
sources:
  - ../Birko.Data.Localization/Decorators/AsyncLocalizedBulkStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/AsyncLocalizedStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/LocalizedBulkStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/LocalizedEntityFields.cs
  - ../Birko.Data.Localization/Decorators/LocalizedStoreWrapper.cs
  - ../Birko.Data.Localization/Decorators/LocalizedTranslationRows.cs
  - ../Birko.Data.Localization/Expressions/LocalizedExpressionVisitor.cs
  - ../Birko.Data.Localization/Expressions/LocalizedFilterHelper.cs
  - ../Birko.Data.Localization/Expressions/LocalizedOrderByHelper.cs
  - ../Birko.Data.Localization/Expressions/LocalizedPropertyUpdateHelper.cs
  - ../Birko.Data.Localization/Filters/EntityTranslationFilter.cs
  - ../Birko.Data.Localization/Models/EntityTranslationModel.cs
  - ../Birko.Data.Localization/Models/IEntityLocalizationContext.cs
  - ../Birko.Data.Localization/Models/ILocalizable.cs
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
# 2026-10-19 regens (TASK-168, TASK-194, TASK-679): the evidence pass was not re-run; FEATURE-014 is already listed
# (TASK-679 has no feature).
shaped-by-derived: true
shaped-by-unresolved: 266
---
# Entity-level localization via store decoration

## Purpose

This capability lets an application store one entity row per record while serving and accepting
*translated* values for selected string properties, without any backend-specific support. It works by
wrapping an existing store in a decorator (`LocalizedStoreWrapper`, `LocalizedBulkStoreWrapper`, and
their async twins). The wrapper keeps every non-default culture in a side table of
`EntityTranslationModel` rows keyed by `(EntityGuid, EntityType, FieldName, Culture)` and reads the
entity's own columns as the **default-culture** copy of the data whenever a row is missing. Those
columns are preserved as the default-culture copy on update: on a non-default culture the wrapper reads
the stored values back and hands the inner store a detached copy carrying them, so the caller's
translated text reaches the translation row and the default-culture value survives. A `Create` has no
stored row to preserve, so there the caller's values populate both the entity's own columns and the
translation row.

On read, the wrapper returns a **detached** copy whose localizable properties carry the current culture's
translation, leaving the instance the inner store returned untouched. On write, it persists the incoming
values as translation rows. Filters that mention a localizable property are rewritten on every path that
takes one — reads, filter-based updates and filter-based deletes alike — so a destructive statement
selects the same rows as the equivalent read: the predicate is evaluated against the translation table in
memory, and the localized part of the filter is replaced with a GUID-membership test that the inner store
can execute. Ordering on a localizable property forces the whole matching set to be loaded, translated,
sorted and paged in memory.

The consumer supplies two things: an entity type implementing `ILocalizable.GetLocalizableFields()`,
and an `IEntityLocalizationContext` giving `CurrentCulture` and `DefaultCulture`. The project ships no
implementation of that context and no dependency-injection registration helper — both are the
consumer's responsibility.

## Requirements

### Requirement: Localizable entity and translation row contract

The system SHALL treat a property as localizable only when its name is returned by
`ILocalizable.GetLocalizableFields()` on the entity type, and SHALL refuse with `InvalidOperationException`,
naming the type and the field, a declared name that is not a public instance `string` property with a public
getter and setter — when the wrapper is constructed for the store's type, on every culture, and when an entity
whose own declaration differs is translated or about to be written, before the inner store's write. It SHALL store each translated value as one
`EntityTranslationModel` carrying `EntityGuid`, `EntityType`, `FieldName`, `Culture`, `Value` and
`UpdatedAt`, where `EntityType` is the store's type name (`typeof(T).Name`) and `FieldName` is the
property name, and SHALL identify a row by `(EntityGuid, EntityType, FieldName, Culture)` on every read,
write and delete. Wrapped entity types SHALL derive from `Birko.Data.Models.AbstractModel` and implement
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

#### Scenario: Localizable field list is read once, at construction

- **Given** a wrapper whose inner store's `CreateInstance()` returns a new entity
- **When** the wrapper is constructed, and later needs the localizable field list for filter rewriting, order-by detection or `PropertyUpdate` inspection
- **Then** it calls `_innerStore.CreateInstance()` once, in the constructor, validates the list and keeps it; translation application and persistence use the validated `GetLocalizableFields()` of the *entity being processed*

#### Scenario: A misdeclared field is refused when the wrapper is built

- **Given** a model whose `GetLocalizableFields()` returns `"Nmae"` (no such property), a `decimal` property, or a string property without a public setter
- **When** any of the four wrappers is constructed for it, on the default culture or another
- **Then** `InvalidOperationException` names the type and the field, and no wrapper is created

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

The system SHALL, on a non-default culture, return a **detached copy** of the entity whose localizable
properties carry the current culture's values: it loads the translation rows for the read entity's GUID,
the store's entity type and the current culture, builds a `FieldName → Value` dictionary of the entity's
localizable fields, and assigns those values onto the copy.
The instance the inner store returned SHALL NOT be written to. On the default culture the wrapper SHALL
return that instance unchanged.

The copy is taken whenever the culture is non-default and the entity has a GUID, not only when a
translation row is found, so the rule a caller can rely on is *a non-default-culture read returns a
detached entity*.

#### Scenario: Read by GUID returns the translated entity

- **Given** entity `G` has base `Name = "Chair"` and a translation row `(G, "Name", "sk", "Stolička")`, with `CurrentCulture = "sk"`
- **When** `ReadAsync(G)` is called
- **Then** the returned entity's `Name` is `"Stolička"`

#### Scenario: Entity without translations keeps its base values

- **Given** entity `G` has no translation rows for the current culture
- **When** the entity is read
- **Then** the translation dictionary is empty and a detached copy carrying every base property unchanged is returned

#### Scenario: A localized read does not write through to the store

- **Given** a store that returns the instance it holds rather than a copy (`Birko.Data.InMemory`, or a caching decorator), entity `G` with base `Name = "Chair"` and a `"sk"` translation `"Stolička"`, with `CurrentCulture = "sk"`
- **When** `Read(G)` is called and then the culture is switched back to `"en"` and `Read(G)` is called again
- **Then** the first read returns `"Stolička"`, the instance held by the inner store still reads `"Chair"`, and the second read returns `"Chair"`

#### Scenario: Entity with a null GUID is skipped

- **Given** an entity whose `Guid` is `null`
- **When** `Localize` runs
- **Then** it returns that same instance without copying it or querying the translation store

#### Scenario: Only declared fields are assigned

- **Given** a translation row whose `FieldName` is not among the entity's localizable fields
- **When** the entity is read
- **Then** that row is ignored; a misdeclared field never reaches this point, because it is refused when the wrapper is built

#### Scenario: Duplicate translation rows resolve to the last one enumerated

- **Given** two translation rows exist for the same `(EntityGuid, FieldName, Culture)` with values `"A"` then `"B"` in the store's enumeration order
- **When** the translation dictionary is built by `translationDict[t.FieldName] = t.Value`
- **Then** the applied value is `"B"` — the last row enumerated wins, with no defined ordering guarantee

#### Scenario: Null entity is not dereferenced

- **Given** the inner store returns `null` for a read
- **When** `Read(guid)` / `ReadAsync(guid)` / `Read(filter)` returns
- **Then** `Localize` is not called and `null` is propagated

#### Scenario: A row of another entity type is not applied

- **Given** entity `G` and a `"sk"` row for `(G, "Name")` whose `EntityType` is not the store's type
- **When** `G` is read on `"sk"`
- **Then** the row is not applied and the base value is returned — the read and the filter resolution use the same four-part key

### Requirement: Bulk reads translate every returned entity

The system SHALL apply translations to each entity of a bulk read result, materializing the inner store's
sequence to a list first, and SHALL read the translations of the whole list in **one** translation-store read per
500 entities (`LocalizedTranslationRows.ReadBatchSize`): `EntityGuid IN (…)` for the store's entity type and the
current culture, grouped in memory. An empty list, or the default culture, reads nothing. The two single-entity
wrappers return one entity per read and cost one translation read each.

#### Scenario: Read-all translates each entity

- **Given** `CurrentCulture` is non-default and the inner store returns three entities from `Read()` / `ReadAsync(ct)`
- **When** the parameterless bulk read completes
- **Then** all three entities are materialized to a list, their translations are read in one query, and each is replaced in it by its localized copy; an entity with no rows becomes a detached copy carrying its base values

#### Scenario: A list larger than one batch

- **Given** 1 001 entities on a non-default culture
- **When** they are read
- **Then** the translations cost three reads of at most 500 GUIDs each — kept under the providers' parameter limits

#### Scenario: Default-culture bulk read short-circuits

- **Given** `CurrentCulture.Name == DefaultCulture.Name`
- **When** `LocalizedBulkStoreWrapper.Read(filter, orderBy, limit, offset)` is called
- **Then** the inner store's result is materialized and returned with no translation read; `AsyncLocalizedBulkStoreWrapper.ReadAsync(filter, orderBy, limit, offset)` short-circuits identically

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
or `EndsWith` on such a property with one argument that evaluates to a string, or two arguments of which
the second evaluates to a `StringComparison`. A part that is not extracted SHALL remain in the residual
filter, evaluated by the inner store against the base column, **only when it reads no localizable field**
or is a comparison of a localizable field with `null`; any other part that reads a localizable field SHALL
be refused with `NotSupportedException` naming the field.

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
- **Then** no condition is produced, and because the part still reads `Name` the split throws `NotSupportedException` naming `Name`

#### Scenario: Null comparison stays on the base column

- **Given** `Name` is localizable and the filter is `x => x.Name == null`, or compares `Name` with a captured variable holding `null`
- **When** extraction runs
- **Then** the evaluated constant is `null`, the `constantValue != null` guard fails, the part is exempt from the refusal and left in the residual filter, and the inner store tests the entity's own `Name` column rather than the translation

#### Scenario: A disjunction reading a localizable field is refused

- **Given** the filter is `x => x.Name == "Stolička" || x.Code == "C1"`
- **When** `FlattenAndAlso` runs
- **Then** the whole `OrElse` node is one indivisible part, no localized condition is extracted from it, and because it reads `Name` the read throws `NotSupportedException` naming `Name` instead of testing the base column

#### Scenario: A negation reading a localizable field is refused

- **Given** the filter is `x => !x.Name.Contains("a")`
- **When** extraction runs
- **Then** the `Not` node matches neither the binary nor the method-call shape, and it is refused with `NotSupportedException`

#### Scenario: Chained, static and column-to-column forms are refused

- **Given** the filter is `x => x.Name.ToLower().Contains("a")`, `x => string.IsNullOrEmpty(x.Name)`, `x => x.Name == x.Code`, or one of these reading the field through a cast (`((INamed)x).Name`)
- **When** extraction runs
- **Then** none is extracted (the call's receiver is not the property itself, or the operand reads the entity), and each is refused with `NotSupportedException`

#### Scenario: A disjunction that reads no localizable field passes through

- **Given** the filter is `x => x.Code == "C1" || x.Code == "C2"`
- **When** extraction runs
- **Then** the part reads no localizable field, so it stays in the residual filter and the inner store evaluates it

#### Scenario: Nested member access is not localized

- **Given** the filter is `x => x.Owner.Name == "Ann"` where `member.Expression` is not the lambda parameter
- **When** `ExtractFieldName` runs
- **Then** it returns `null` because `member.Expression == param` fails, and the part stays in the residual filter

#### Scenario: A StringComparison argument is honoured

- **Given** the filter is `x => x.Name.Contains("st", StringComparison.OrdinalIgnoreCase)`
- **When** extraction runs
- **Then** the condition is extracted and the predicate is `v.Contains("st", StringComparison.OrdinalIgnoreCase)`; with one argument each method keeps its .NET default — `Contains` ordinal, `StartsWith` / `EndsWith` culture-sensitive

#### Scenario: Method-name matching is not overload-checked

- **Given** the localizable property's declaring type exposes any instance method named `Contains`, `StartsWith` or `EndsWith` whose first argument is a string, with at most one further argument that evaluates to a `StringComparison`
- **When** extraction runs
- **Then** the condition is extracted purely on `methodCall.Method.Name`, without verifying the declaring type or overload; any other argument shape is not extracted and is refused

#### Scenario: String method predicates guard against null translation values

- **Given** a translation row whose `Value` is `null`
- **When** a `Contains` / `StartsWith` / `EndsWith` predicate is evaluated against it
- **Then** the predicate returns false because of its `v != null &&` guard, instead of throwing `NullReferenceException`

### Requirement: Constant evaluation for extracted conditions

The system SHALL resolve the compared value from a `ConstantExpression` directly, from a field or property
access on a `ConstantExpression` (a closure capture) by reflection, and otherwise by compiling and invoking
the subexpression. An operand that reads the lambda parameter SHALL yield `null` without being compiled.
An exception thrown while invoking SHALL propagate to the caller, unwrapped from
`TargetInvocationException`.

#### Scenario: Captured local variable is resolved

- **Given** `var q = "Stolička";` and the filter `x => x.Name == q`
- **When** `EvaluateExpression` runs on the closure member access
- **Then** the field value `"Stolička"` is read from the compiler-generated closure constant

#### Scenario: Compile-and-invoke fallback

- **Given** the compared operand is `someObject.Prop.Nested`
- **When** neither the constant nor the single-level closure shape matches
- **Then** `Expression.Lambda(expression).Compile().DynamicInvoke()` is used to obtain the value

#### Scenario: An evaluation failure is reported

- **Given** the compared operand throws when invoked (for example it dereferences null)
- **When** `EvaluateExpression` invokes it
- **Then** the operand's own exception reaches the caller of the read, and no query runs

#### Scenario: An operand that reads the entity is not a value

- **Given** the filter is `x => x.Name == x.Code`
- **When** `EvaluateExpression` runs on `x.Code`
- **Then** it returns `null` without compiling, so nothing is extracted and the part is refused because it reads `Name`

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
- **Then** the inner store is called with only the rewritten filter (no order, no limit, no offset), every matching entity is translated with the batched read (one per 500 entities), the list is sorted in memory, and `Skip(20).Take(10)` is applied afterwards. Loading the whole matching set is inherent to sorting translated text in memory and is a stated limit (SH-M081): the cost grows with the match, not the page

#### Scenario: Null orderBy never triggers in-memory sorting

- **Given** `orderBy == null`
- **When** `LocalizedOrderByHelper.ReferencesLocalizedField` runs
- **Then** it returns false and the read is delegated with the rewritten filter, limit and offset

#### Scenario: In-memory paging with no bounds

- **Given** `offset == null` and `limit == null`
- **When** `ApplyInMemoryPaging` runs
- **Then** the full list is returned as a new list, with neither `Skip` nor `Take` applied

#### Scenario: Negative paging bounds are refused

- **Given** `offset = -5`, or `limit = -1`
- **When** `ApplyInMemoryPaging` runs on the in-memory path
- **Then** `ArgumentOutOfRangeException` names the argument, instead of `Skip` ignoring the offset or `Take` returning an empty page

### Requirement: In-memory ordering semantics

The system SHALL sort in memory by reflecting each `OrderBy` field's property value, using a defensive
comparer that orders `null` first, collates two strings with the localization context's current culture
(`CultureInfo.CompareInfo`), compares other same-typed `IComparable` values directly, and otherwise falls
back to `string.CompareOrdinal` of `ToString()`. A field whose property cannot be found on `typeof(T)` SHALL
be refused with `ArgumentException`, wherever it appears and even when there is nothing to sort, as
`OrderByHelper` refuses it on the path that is not translated; names resolve case-insensitively, as there. Null placement and string collation are
deliberately not aligned with the backends, which disagree among themselves.

#### Scenario: Multi-field ordering builds a ThenBy chain

- **Given** `orderBy.Fields` is `[{Name, ascending}, {Code, descending}]` and both properties exist
- **When** `ApplyInMemoryOrderBy` runs
- **Then** the list is ordered by `Name` ascending then `Code` descending, using `SafeObjectComparer` for both keys

#### Scenario: Translated text is collated with the localization culture

- **Given** `CurrentCulture = "sk"`, the thread culture is invariant, and the translated names are `"dom"`, `"čaj"`, `"cesta"`
- **When** the bulk read orders by `Name`
- **Then** the order is `"cesta"`, `"čaj"`, `"dom"` — Slovak collation, where `č` follows `c` — not the invariant `"čaj"`, `"cesta"`, `"dom"`

#### Scenario: Null keys sort first

- **Given** two entities, one with `Name == null`
- **When** ascending ordering is applied
- **Then** the null-valued entity comes first (`Compare` returns -1 for a null `x`)

#### Scenario: Mismatched key types do not throw

- **Given** two key values of different runtime types
- **When** `SafeObjectComparer.Compare` runs
- **Then** it falls back to `string.CompareOrdinal(x.ToString(), y.ToString())` instead of throwing `InvalidOperationException`

#### Scenario: Trivial input is returned unchanged

- **Given** the list holds one or zero entities, or `orderBy.Fields` is empty, and every field resolves
- **When** `ApplyInMemoryOrderBy` runs
- **Then** the same list instance is returned without sorting

#### Scenario: An unknown order-by field is refused

- **Given** `orderBy.Fields` is `[{"NoSuchProp"}, {"Name"}]` or `[{"Name"}, {"NoSuchProp"}]`
- **When** `ApplyInMemoryOrderBy` resolves the fields, before sorting anything
- **Then** `ArgumentException` names `NoSuchProp`

### Requirement: Translation persistence on create and update

The system SHALL, after the inner store's write succeeds and only on a non-default culture, write one
translation row per localizable string property through the single producer `LocalizedTranslationRows`,
keyed by the GUID the inner store's create **returned** (on update, the entity's GUID): a `null` value
deletes the rows for `(EntityGuid, EntityType, FieldName, Culture)`; otherwise an existing row with that key
is updated in place with the new `Value` and `UpdatedAt = DateTime.UtcNow`, and a missing one is created with
the same fields. `EntityType` SHALL be the store's type name, `typeof(T).Name`, which is what every filter
queries; a row with the same GUID under another type name is another store's and is left alone.

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

#### Scenario: A null value removes the translation

- **Given** entity `G` has a `"sk"` translation `Name = "Stolička"` and the caller sets `entity.Name = null`
- **When** `Update(entity)` runs on `"sk"`
- **Then** the `(G, "Name", "sk")` row is deleted, and a later `"sk"` read falls back to the base value

#### Scenario: A subclass instance is stamped with the store's type

- **Given** a store typed for `Product` and a `SpecialProduct : Product` instance created on `"sk"`
- **When** its translation row is written
- **Then** `EntityType` is `"Product"`, so a localized filter on the `Product` store finds it

#### Scenario: Empty string is persisted

- **Given** `entity.Name = ""`
- **When** `SaveTranslations` runs
- **Then** the translation row's `Value` becomes `""` (only `null` removes the row)

#### Scenario: Non-string localizable field is refused

- **Given** an entity being written whose own `GetLocalizableFields()` names an `int` property or a missing one
- **When** its translations are persisted
- **Then** `InvalidOperationException` names the field

#### Scenario: Create keys translations by the returned GUID

- **Given** the inner store's `Create` returns a GUID but does not set `data.Guid` on the passed instance
- **When** `Create(data)` / `CreateAsync(data)` / `SaveAsync(data)` completes on a non-default culture
- **Then** the translation rows are written under the returned GUID

#### Scenario: Entity with no GUID persists no translations

- **Given** an update or a bulk create of an entity whose `Guid` is null or empty
- **When** translation persistence runs
- **Then** it returns immediately and no row is written

#### Scenario: Timestamps come from the system clock

- **Given** any translation upsert
- **When** `UpdatedAt` is assigned
- **Then** the value is `DateTime.UtcNow` read directly, not obtained from an injected `IDateTimeProvider`

#### Scenario: Update preserves the default-culture value in the entity's own column

- **Given** `CurrentCulture = "sk"`, `DefaultCulture = "en"`, entity `G` whose stored `Name` is `"Chair"`, and the caller setting `entity.Name = "Stolička"`
- **When** `Update(entity)` runs
- **Then** the stored `Name` is read back, a detached copy carrying `"Chair"` is handed to the inner store, and the `"sk"` translation row is written with `"Stolička"` — so the entity's own column still reads `"Chair"`

#### Scenario: The caller's entity is returned unchanged

- **Given** the caller holds `entity.Name = "Stolička"` across an `Update`
- **When** the update completes, whether it succeeded or threw
- **Then** the caller's instance still reads `"Stolička"` — the base value is put onto a copy, never onto the object the caller owns

#### Scenario: Create has no default-culture value to preserve

- **Given** `CurrentCulture = "sk"` and an entity with no stored row
- **When** `Create(entity)` runs
- **Then** the caller's entity is handed to the inner store unchanged, so `"Stolička"` populates both the entity's own `Name` column and the `"sk"` translation row; a later default-culture `Update` replaces the column

#### Scenario: Update of an entity the inner store does not hold

- **Given** an entity whose GUID has no stored row, or a null/empty GUID
- **When** `Update(entity)` runs on a non-default culture
- **Then** the base-value read yields nothing, the caller's entity is passed through unchanged, and no copy is made

#### Scenario: The collection overload reads base values in one query

- **Given** a non-default culture and an `Update(IEnumerable<T>)` carrying several entities
- **When** the update runs
- **Then** the base values for the whole batch are fetched with a single GUID-membership read rather than one read per entity, and each entity with a stored row is replaced by a copy carrying its own base values

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
entity's GUID and the store's entity type — across all cultures and field names — and SHALL skip the deletion
when the entity's GUID is null. A row with the same GUID under another entity type survives.

#### Scenario: All cultures are removed

- **Given** entity `G` has `"sk"` and `"de"` translations
- **When** `Delete(entity)` / `DeleteAsync(entity)` runs
- **Then** a filter on `(G, store type)` selects both rows and they are removed with a single bulk delete

#### Scenario: Bulk delete of a collection

- **Given** three entities are passed to `Delete(IEnumerable<T>)`
- **When** the call completes
- **Then** the sequence is materialized once, the inner store deletes all three, and `DeleteTranslations` runs once per entity

#### Scenario: Entity without a GUID

- **Given** an entity whose `Guid` is null
- **When** `DeleteTranslations` runs
- **Then** it returns without querying or deleting anything

### Requirement: Filter-based bulk delete reads before deleting and localizes the filter

The system SHALL implement `Delete(filter)` by reading the matching entities from the inner store with the
**rewritten** filter on a non-default culture (and the original filter on the default culture), deleting
that materialized set through the inner store, and then deleting each entity's translations — never using
the inner store's native filter-delete.

#### Scenario: Delete by filter cascades to translations

- **Given** two entities match `x => x.Active == false`
- **When** `Delete(filter)` / `DeleteAsync(filter)` runs
- **Then** both entities are read, deleted as a collection, and each one's translation rows are removed

#### Scenario: Delete by a filter on a localizable field targets the translated rows

- **Given** `CurrentCulture = "sk"`, `Name` is localizable, one entity whose `"sk"` translation is `"Stolička"`, a second entity carrying `"Stolička"` in its own default-culture column, and the filter `x => x.Name == "Stolička"`
- **When** `Delete(filter)` runs
- **Then** `RewriteFilter` is invoked, the first entity is deleted and the second is not — the set deleted is the set the equivalent `Read(filter)` returns

### Requirement: Filter-based bulk update with an action

The system SHALL implement `Update(filter, Action<T>)` with the **rewritten** filter on a non-default
culture and the original filter on the default culture, where it delegates the action unchanged. On a
non-default culture it SHALL show the action each entity with the current culture's translations applied,
write translations only for the localizable fields the action **changed**, keep each entity's own column on
the default-culture value, and write those translation rows only **after** the inner store's update for
that entity has returned; the sync and async wrappers SHALL do so by different mechanisms.

#### Scenario: Sync wrapper delegates to the inner store's native filter update

- **Given** `LocalizedBulkStoreWrapper.Update(filter, updateAction)` on a non-default culture
- **When** it runs
- **Then** it calls `_innerStore.Update(<rewritten filter>, callback)` exactly once; the callback captures the base values, applies the entity's translations, runs the action, records the changed localizable fields, and restores the base values in a `finally`; the recorded translations are written after `_innerStore.Update` returns

#### Scenario: Async wrapper does read-modify-write per entity

- **Given** `AsyncLocalizedBulkStoreWrapper.UpdateAsync(filter, updateAction)`
- **When** it runs
- **Then** it reads all matching entities via `_innerStore.ReadAsync(<filter>, null, null, null, ct)`. On the default culture it applies the action to each and issues one inner update. On a non-default culture it works on a detached copy of each: translations applied, action run, base values restored, inner update issued, and then the changed fields' translations written

#### Scenario: Filter-based update on a non-default culture preserves the base column

- **Given** `CurrentCulture = "sk"`, `Name` is localizable, the stored `Name` is `"Chair"`, and `Update(filter, e => e.Name = "Stolička")`
- **When** the update completes
- **Then** a `"sk"` translation row is written with `"Stolička"` and the entity's own column still reads `"Chair"`

#### Scenario: A non-localizable change made by the same action still persists

- **Given** the action sets both a localizable `Name` and a non-localizable `Code`
- **When** the update completes on a non-default culture
- **Then** the new `Code` is persisted — the capture and restore cover the localizable fields only

#### Scenario: An untouched localizable field keeps its translation

- **Given** `CurrentCulture = "sk"`, entity `G` with a `"sk"` `Name` translation `"Stolička"` and no `"sk"` `Description` row, and the action `e => e.Code = "C2"`
- **When** the update completes
- **Then** the `"sk"` `Name` row still reads `"Stolička"` and no `Description` row is created

#### Scenario: The action sees the translated values

- **Given** the same entity `G`
- **When** the action reads `e.Name`
- **Then** it sees `"Stolička"`, as a `"sk"` read would show it

#### Scenario: A failed inner update leaves no translation row

- **Given** a non-default culture and an inner update that throws
- **When** the filter-based update runs
- **Then** the exception propagates and no translation row is written for that entity. The sync wrapper writes none for the whole call, since it writes after `_innerStore.Update` returns; the async wrapper keeps the rows of entities updated earlier in the call (the two writes share no boundary, TASK-683)

### Requirement: Native PropertyUpdate falls back when it touches a localizable field

The system SHALL inspect a `PropertyUpdate<T>`'s assignments and, when the current culture is non-default
and at least one assignment targets a localizable field, convert the update to an `Action<T>` via
`PropertyUpdate.ApplyTo` and route it through the read-modify-write path; otherwise it SHALL delegate the
native `PropertyUpdate` straight to the inner store.

On the read-modify-write path it SHALL refuse, with `NotSupportedException`, an update that carries any
increment at all, because replaying it there would silently drop the increment's atomicity; the refusal
throws before the inner store or the translation store is called. An increment can never target a localizable
field: those are strings, which `Increment` does not accept, and a numeric one is refused at construction.

#### Scenario: PropertyUpdate on a non-localizable field stays native

- **Given** the update assigns only `x => x.Active`
- **When** `Update(filter, updates)` runs on a non-default culture
- **Then** `TouchesLocalizableField` returns false and the native `PropertyUpdate` is delegated, preserving the backend's SET — with the **rewritten** filter on a non-default culture, because the predicate may name a localizable field even when no assignment does

#### Scenario: PropertyUpdate on a localizable field falls back

- **Given** `Name` is localizable and the update assigns `x => x.Name`
- **When** `Update(filter, updates)` runs on a non-default culture
- **Then** it is rewritten to `Update(filter, LocalizedPropertyUpdateHelper.ToAction(updates))` — with the **original** filter, because that overload resolves it itself — so the translation row is written and the base column is preserved

#### Scenario: Default culture always uses the native path

- **Given** `CurrentCulture.Name == DefaultCulture.Name` and the update `Set`s a localizable field
- **When** `Update(filter, updates)` runs
- **Then** the `IsNonDefaultCulture()` guard short-circuits and the native `PropertyUpdate` is delegated unchanged

#### Scenario: A numeric field declared localizable is refused before any increment

- **Given** a model whose `GetLocalizableFields()` names `decimal Price`
- **When** a bulk wrapper is constructed for it, on the default culture or another
- **Then** `InvalidOperationException` names `Price`; TASK-498's per-increment refusal for this case was removed as unreachable (rule 53)

#### Scenario: An increment beside a localizable Set is refused on a non-default culture

- **Given** `CurrentCulture = "sk"`, `Label` is localizable and `Stock` is not, and the update is `Set(x => x.Label, "x").Increment(x => x.Stock, 1)`
- **When** `Update(filter, updates)` runs
- **Then** `TouchesLocalizableField` selects the read-modify-write fallback and `RefuseIncrementOnReadModifyWriteFallback` throws `NotSupportedException` naming `T.Stock` before the update is converted to an action; on the default culture the same update is delegated natively and both assignments apply

#### Scenario: An increment on a non-localizable field stays native

- **Given** `Stock` is not localizable and the update is `Increment(x => x.Stock, 2)` with no other assignment
- **When** `Update(filter, updates)` runs on either culture
- **Then** neither refusal fires and the native `PropertyUpdate` is delegated — with the rewritten filter on a non-default culture — so the increment keeps the backend's atomic form

#### Scenario: Boxing conversions in the assignment selector are unwrapped

- **Given** an assignment lambda whose body is a `UnaryExpression` convert wrapping the member access (for example a value-typed property selected as `object`)
- **When** `GetMemberName` inspects it
- **Then** it unwraps `unary.Operand as MemberExpression` and still recognises the property name

#### Scenario: No assignments or no localizable fields

- **Given** `updates.Assignments.Count == 0`, or the localizable field list is empty
- **When** `TouchesLocalizableField` runs
- **Then** it returns false without inspecting anything

### Requirement: Save dispatches on GUID presence

The system SHALL treat a null or `Guid.Empty` GUID as a create and anything else as an update, SHALL
create the entity under its own GUID when that update reports `EntityNotFoundException` for it, and SHALL
return the GUID the create path returned on both the sync and async wrappers. The decision is delegated
to the shared `StoreSave.Save` / `StoreSave.SaveAsync`, passed the wrapper's own `Create` / `Update`, so
translation persistence runs on whichever path is taken.

#### Scenario: Empty GUID creates

- **Given** `data.Guid` is `null` or `Guid.Empty`
- **When** `Save(data)` / `SaveAsync(data)` runs
- **Then** the create path runs, including translation persistence

#### Scenario: Populated GUID updates

- **Given** `data.Guid` is a non-empty GUID
- **When** `Save(data)` / `SaveAsync(data)` runs
- **Then** the update path runs and the method returns `data.Guid ?? Guid.Empty`

#### Scenario: Populated GUID that is not stored is created under that GUID

- **Given** `data.Guid` is a non-empty GUID with no stored row, and an inner store whose update throws `EntityNotFoundException` naming that GUID
- **When** `Save(data)` / `SaveAsync(data)` runs
- **Then** the exception is caught and the wrapper's create path runs with the same `data` (persisting translations), the store delegate is applied at most once across the update and create attempts, and the create path's GUID is returned
- **And** an `EntityNotFoundException` that does not name `data.Guid` propagates

#### Scenario: Return value on create is the inner store's GUID on both wrappers

- **Given** an inner store that returns a new GUID from `Create`/`CreateAsync` without assigning it to the passed instance
- **When** `Save(data)` or `SaveAsync(data)` runs on an entity with an empty GUID
- **Then** both the sync and async wrappers return the inner store's GUID

#### Scenario: Null data is a no-op

- **Given** `data == null`
- **When** `Save(null)` / `SaveAsync(null)` runs
- **Then** `Guid.Empty` is returned and neither the inner store nor the translation store is touched

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
`ArgumentNullException`, SHALL initialize both the inner store and the translation store on `Init` /
`InitAsync`, SHALL on `Destroy` / `DestroyAsync` destroy the inner store and then delete every translation row
of the store's entity type — never the whole translation store, which other localized stores share — SHALL
delegate `CreateInstance` to the inner store, and SHALL expose the inner store via
`IStoreWrapper.GetInnerStore()` and `GetInnerStoreAs<TInner>()`.

#### Scenario: Null constructor argument

- **Given** a null `translationStore`
- **When** the wrapper is constructed
- **Then** `ArgumentNullException` is thrown naming `translationStore`

#### Scenario: CreateInstance is pass-through

- **Given** any localized wrapper
- **When** `CreateInstance()` is called
- **Then** the call is forwarded to the inner store with no localization behaviour added

#### Scenario: Typed unwrapping is single-level

- **Given** a chain `LocalizedStoreWrapper → TenantStoreWrapper → ConcreteStore`
- **When** `GetInnerStoreAs<ConcreteStore>()` is called on the outermost wrapper
- **Then** it returns `null`, because the implementation is `_innerStore as TInner` with no recursion into further wrappers

#### Scenario: The translation store follows the lifecycle

- **Given** a localized `Product` wrapper with translation rows, sharing its translation store with an `Order` wrapper
- **When** `Init()` / `InitAsync()` and later `Destroy()` / `DestroyAsync()` are called
- **Then** both stores are initialized; on destroy the inner store is destroyed and every `"Product"` row is deleted, while the `"Order"` rows remain
