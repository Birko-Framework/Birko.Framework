---
area: filter-expression-translation
generated-at: 84457477972f5f00ff263f6b7f13fcb6449676c9
generated-on: 2026-08-12
sources:
  - ../Birko.Data.Core/Expressions/ExpressionNormalizer.cs
  - ../Birko.Data.Core/Expressions/ExpressionParameterReplacer.cs
  - ../Birko.Data.Core/Filters/IFilter.cs
  - ../Birko.Data.Core/Filters/ModelByGuid.cs
  - ../Birko.Data.Core/Filters/ModelsByGuid.cs
  - ../Birko.Data.ElasticSearch/ElasticSearch/ElasticSearch.cs
  - ../Birko.Data.ElasticSearch/Stores/AsyncElasticSearchStore.cs
  - ../Birko.Data.ElasticSearch/Stores/ElasticSearchStore.cs
  - ../Birko.Data.SQL.MSSql/Database/Connector/MSSqlConnector.cs
  - ../Birko.Data.SQL.MySQL/Database/Connectors/MySQLConnector.cs
  - ../Birko.Data.SQL.PostgreSQL/Database/Connectors/PostgreSQLConnector.cs
  - ../Birko.Data.SQL.SqLite/Database/Connectors/SqLiteConnector.cs
  - ../Birko.Data.SQL/SQL/Conditions/Condition.cs
  - ../Birko.Data.SQL/SQL/Conditions/Join.cs
  - ../Birko.Data.SQL/SQL/Conditions/RuleConditionConverter.cs
  - ../Birko.Data.SQL/SQL/Connectors/AbstractConnectorBase.cs
  - ../Birko.Data.SQL/SQL/Connectors/IConditionStrategy.cs
  - ../Birko.Data.SQL/SQL/Connectors/SqlBuilderContext.cs
  - ../Birko.Data.SQL/SQL/Connectors/Strategies/ComparisonConditionStrategy.cs
  - ../Birko.Data.SQL/SQL/Connectors/Strategies/EqualConditionStrategy.cs
  - ../Birko.Data.SQL/SQL/Connectors/Strategies/InConditionStrategy.cs
  - ../Birko.Data.SQL/SQL/Connectors/Strategies/LikeConditionStrategy.cs
  - ../Birko.Data.SQL/SQL/Connectors/Strategies/NullConditionStrategy.cs
  - ../Birko.Data.SQL/SQL/DataBase.cs
  - ../Birko.Data.SQL/SQL/DataBase_OrderBy.cs
  - ../Birko.Data.SQL/SQL/DataBase_RuleField.cs
shaped-by:
  # TASK-111 / SH-H023 — rule fields resolve against table metadata. The evidence pass cannot run from this
  # aggregator: every source above is in a sibling repo, so the sha resolves under no `git show` here.
  - TASK-111
---

# LINQ filter expression translation to backend queries

## Purpose

Every Birko store accepts its filter as a C# lambda — `Expression<Func<T, bool>>`. Backends that own a
LINQ provider (MongoDB, CosmosDB, RavenDB) hand the tree straight to their driver; backends that
compile a delegate (InMemory, JSON, XML) just run it. The two backends specified here — **SQL** (four
providers: SQLite, PostgreSQL, MySQL, MSSQL) and **ElasticSearch** — have neither, so each carries a
hand-rolled translator that walks the expression tree and emits a native query: a tree of
`Birko.Data.SQL.Conditions.Condition` objects rendered into a parameterised `WHERE` clause, or a NEST
`QueryBase`.

Because the two translators are independent hand-written walkers, the *same* C# predicate can be
translated differently — or, for shapes neither walker understands, not at all. This document records
what each translator actually produces, including the shapes that are silently dropped or widened,
because those are the cases where a filter returns the wrong rows rather than failing. A shared
pre-pass, `ExpressionNormalizer` (in `Birko.Data.Core`), runs at the lambda boundary of both
translators and is the only piece of this capability the two backends provably share.

## Requirements

### Requirement: Filter abstraction

The system SHALL expose `IFilter<TModel>` with a single member `Expression<Func<TModel, bool>>? Filter()`
whose return value MAY be `null`, and SHALL ship two reusable implementations: `ModelByGuid<TModel>`
(returns `x => x.Guid == Guid`) and `ModelsByGuid<TModel>` (returns
`x => x.Guid != null && Guids.Contains(x.Guid.Value)`).

#### Scenario: Guid filter for a single model

- **Given** a `ModelByGuid<Foo>` constructed with the guid `11111111-1111-1111-1111-111111111111`
- **When** `Filter()` is called
- **Then** the returned lambda body compares `x.Guid` for equality against that guid, captured from the filter instance's `Guid` property

#### Scenario: Null guid collection yields no filter at all

- **Given** a `ModelsByGuid<Foo>` whose `Guids` property is `null`
- **When** `Filter()` is called
- **Then** it returns `null` rather than a lambda, so the caller sees "no filter supplied"

#### Scenario: Empty guid collection yields a real filter

- **Given** a `ModelsByGuid<Foo>` constructed with an empty `IEnumerable<Guid>`
- **When** `Filter()` is called
- **Then** it returns the `x.Guid != null && Guids.Contains(x.Guid.Value)` lambda — an empty-collection `Contains`, not `null`

### Requirement: Funcletization of parameter-free subtrees

`ExpressionNormalizer.Normalize` SHALL replace every subtree that references no `ParameterExpression`
with a `ConstantExpression` carrying that subtree's evaluated value, except subtrees whose node type is
`Constant`, `Parameter`, `Lambda` or `Quote`, and except subtrees of type `void`. When evaluation
throws, the node SHALL be left unchanged rather than propagating the exception.

#### Scenario: Closure variable collapses to a constant

- **Given** a predicate `x => x.Name == prefix + suffix` where `prefix` and `suffix` are captured locals
- **When** `ExpressionNormalizer.Normalize` visits the body
- **Then** the whole right-hand side is replaced by a single `ConstantExpression` holding the concatenated string

#### Scenario: Parameter-free ternary collapses before any parser sees it

- **Given** a predicate `x => x.Status == (useDraft ? Draft : Active)` where `useDraft` is a captured bool
- **When** the body is normalized
- **Then** the `ConditionalExpression` is gone, replaced by the constant value of the surviving branch

#### Scenario: A subtree that throws is left for the parser

- **Given** a parameter-free subtree whose evaluation throws (for example a division by a captured zero)
- **When** `TryFold` compiles and invokes it
- **Then** the exception is swallowed and the original node is returned unchanged

#### Scenario: Nested lambda keeps funcletization conservative

- **Given** a subtree that wraps an inner lambda with its own parameter
- **When** `ContainsParameter` inspects it
- **Then** the inner lambda's parameter counts as "contains a parameter", so the subtree is not folded

#### Scenario: Null input

- **Given** a `null` expression
- **When** `ExpressionNormalizer.Normalize(null)` is called
- **Then** it returns `null`

### Requirement: Desugaring boolean ternary and boolean null-coalescing

`ExpressionNormalizer` SHALL rewrite a parameter-dependent `ConditionalExpression` of type `bool` into
`(test && ifTrue) || (!test && ifFalse)`, and a `Coalesce` node of type `bool` into
`(left == true) || (left == null && right)`. A `ConditionalExpression` or `Coalesce` of any other type
SHALL be left intact for the value parser. A `ConditionalExpression` whose test folded to a constant
`bool` SHALL be replaced by the surviving branch outright.

#### Scenario: Boolean ternary becomes boolean algebra

- **Given** the predicate body `x.Vip ? x.Premium : x.Active` (all `bool`)
- **When** `VisitConditional` runs
- **Then** the result is `OrElse(AndAlso(x.Vip, x.Premium), AndAlso(Not(x.Vip), x.Active))` — only AND/OR/NOT remain

#### Scenario: Nullable-bool coalesce becomes boolean algebra

- **Given** the predicate body `x.Approved ?? x.Active` where `x.Approved` is `bool?`
- **When** `VisitBinary` sees the `Coalesce` node with `node.Type == typeof(bool)`
- **Then** it emits `(x.Approved == true) || (x.Approved == null && x.Active)` and re-visits the result

#### Scenario: Numeric ternary survives for the value parser

- **Given** the operand `(x.Vip ? x.Premium : x.Score)` of type `int` inside a comparison
- **When** `VisitConditional` runs
- **Then** the `ConditionalExpression` is returned updated but intact, to be rendered later as `CASE WHEN … END` (SQL) or a Painless ternary (ElasticSearch)

#### Scenario: Constant test collapses the ternary

- **Given** a ternary whose test funcletized to `Constant(true)`
- **When** `VisitConditional` runs
- **Then** the `ifTrue` branch is returned and the `ifFalse` branch is discarded

### Requirement: Parameter-sharing predicate composition

`ExpressionParameterReplacer` SHALL combine two `Expression<Func<T, bool>>` lambdas with `AndAlso` /
`OrElse` by rewriting the right lambda's parameter to the left lambda's parameter — never by emitting
an `InvocationExpression` — and SHALL return the right lambda unchanged when the left is `null`.

#### Scenario: Two predicates share one parameter

- **Given** `left = a => a.Active` and `right = b => b.Age > 18`
- **When** `ExpressionParameterReplacer.AndAlso(left, right)` is called
- **Then** the returned lambda has exactly one parameter (`left`'s) and its body is `AndAlso(a.Active, a.Age > 18)` with every occurrence of `b` replaced by `a`

#### Scenario: Null left side

- **Given** `left = null` and any `right`
- **When** `OrElse(left, right)` is called
- **Then** `right` is returned by reference, unmodified

### Requirement: SQL predicate parsing starts at the lambda boundary

`DataBase.ParseConditionExpression` SHALL, on encountering a `LambdaExpression`, take the first
parameter's type as the column-resolution type, run `ExpressionNormalizer.Normalize` over the body, and
then parse the normalized body.

#### Scenario: Normalizer runs exactly once, at the boundary

- **Given** any predicate lambda
- **When** `ParseConditionExpression` is called with it
- **Then** the body is normalized before any condition object is built, so the recursive parser below never encounters a parameter-free ternary, `??`, or closure arithmetic

#### Scenario: Parameter type drives column resolution

- **Given** the lambda `(Invoice x) => x.Number == "FV1"`
- **When** the body is parsed
- **Then** `typeof(Invoice)` is threaded through as `exprType` and used by `ResolveColumnName` to map `Number` to its SQL select name

### Requirement: SQL constant-boolean predicates

`DataBase.ParseConditionExpression` SHALL translate a predicate whose normalized body is
`Constant(true)` into an EMPTY condition sequence (meaning "no filter"), and a body of
`Constant(false)` into a single condition with `Name = "1"`, `Values = { 0 }` and
`Type = ConditionType.Equal` (rendering an always-false `1 = @param(0)`). The same two outcomes SHALL be
produced when an AND/OR tree short-circuits to a constant.

#### Scenario: Always-true predicate produces no WHERE clause

- **Given** the predicate `_ => true`
- **When** `ParseConditionExpression` runs and the resulting (empty) sequence reaches `AbstractConnectorBase.AddWhere`
- **Then** `ConditionDefinition` returns an empty string and no ` WHERE ` text is appended to the command

#### Scenario: Always-false predicate produces an impossible WHERE

- **Given** the predicate `_ => false`
- **When** the condition is rendered by `EqualConditionStrategy`
- **Then** the SQL is `1 = @WHERE10_0` with the parameter bound to `0`

#### Scenario: AND with a false operand short-circuits

- **Given** `x => false && x.Active`
- **When** `TryGetLiteralBool` resolves both operands without allocating conditions
- **Then** `MakeFalseCondition` is returned and `x.Active` is never parsed

#### Scenario: OR with a true operand widens to no filter

- **Given** `x => true || x.Active`
- **When** the AND/OR branch resolves the literal bools
- **Then** an empty condition sequence is returned — the predicate matches every row

#### Scenario: A surviving operand is unwrapped, not wrapped in a group

- **Given** `x => true && x.Active`
- **When** the left operand resolves to constant true
- **Then** `ReturnSingleSubCondition` transfers the right condition's `Name`, `Values`, `Type`, `IsNot` and `SubConditions` onto the parent (or returns it standalone) so no redundant group is emitted

### Requirement: SQL comparison operators

`DataBase.ParseConditionExpression` SHALL map `Equal` to `ConditionType.Equal`, `NotEqual` to
`ConditionType.Equal` with `IsNot = true`, `LessThan` to `Less`, `LessThanOrEqual` to `LessAndEqual`,
`GreaterThan` to `Greather`, and `GreaterThanOrEqual` to `GreatherAndEqual`; and the rendering
strategies SHALL invert each comparison operator when `IsNot` is set.

#### Scenario: Not-equal renders as `<>`

- **Given** `x => x.Status != 3`
- **When** `EqualConditionStrategy.BuildSql` renders the resulting condition
- **Then** the SQL is `Status <> @param` because `IsNot` is set

#### Scenario: Negated greater-than inverts the operator

- **Given** a `Greather` condition with `IsNot = true`
- **When** `ComparisonConditionStrategy.GetOperator` runs
- **Then** it returns `" <= "`, not `"NOT >"`

#### Scenario: An ordering comparison with the constant on the left keeps the written operator

- **Given** `x => 5 > x.Age`
- **When** the comparison branch assigns `ConditionType.Greather` from the node type, then binds the constant `5` as the condition's value and `Age` as its name
- **Then** the rendered SQL is `Age > @param(5)` — the operands are swapped without flipping the operator, so the predicate means `Age > 5` rather than `Age < 5`. (Only the value-expression path, via `BuildValueComparison`/`FlipComparison`, flips.)

#### Scenario: A comparison the switch does not cover defaults to equality

- **Given** a binary node that is neither AND/OR nor one of the six comparison node types (for example `ExpressionType.Coalesce` reaching the comparison branch)
- **When** the condition type is chosen
- **Then** it stays at its initialised value `ConditionType.Equal`

### Requirement: SQL parse-time evaluation of parameter-free comparisons

When neither side of a non-AND/OR binary comparison references a lambda parameter,
`DataBase.ParseConditionExpression` SHALL evaluate the whole comparison at parse time and return the
empty sequence for `true` or `MakeFalseCondition` for `false`; if evaluation throws, it SHALL fall
through to normal parsing.

#### Scenario: A closure-only comparison folds away

- **Given** `x => configId == null` where `configId` is a captured nullable and is in fact `null`
- **When** the comparison is parsed
- **Then** `EvaluateExpression` yields `true` and an empty condition sequence is returned — no column is referenced

#### Scenario: Evaluation failure degrades to normal parsing

- **Given** a parameter-free comparison whose evaluation throws
- **When** the `try` block catches the exception
- **Then** parsing continues into the value-expression / operand branches instead of propagating

### Requirement: SQL null and HasValue translation

The SQL translator SHALL emit `ConditionType.IsNull` for a comparison against the literal `null`, for a
closure operand that evaluates to `null`, and for `Nullable<T>.HasValue`; `HasValue` SHALL toggle
`IsNot` so a bare `x.Prop.HasValue` becomes `IS NOT NULL` and `!x.Prop.HasValue` becomes `IS NULL`.
`NullConditionStrategy` SHALL render `IsNull` as `Name IS NULL` or `Name IS NOT NULL` and SHALL never
bind a parameter.

#### Scenario: Literal null equality becomes IS NULL

- **Given** `x => x.DeletedAt == null`
- **When** the right operand (a null `ConstantExpression`) is parsed under the comparison's parent condition
- **Then** `parent.Type` is set to `ConditionType.IsNull` and the SQL is `DeletedAt IS NULL`

#### Scenario: A closure variable holding null becomes IS NULL, not `= NULL`

- **Given** `x => x.ConfigId == configId` where the captured `configId` evaluates to `null`
- **When** the normalizer has folded that closure member to `Constant(null)` and the constant branch finds a parent whose `Type` is not `In`
- **Then** `parent.Type` becomes `ConditionType.IsNull`, avoiding a `Col = NULL` predicate that SQL would evaluate as UNKNOWN for every row

#### Scenario: Bare HasValue becomes IS NOT NULL

- **Given** `x => x.ClosedAt.HasValue`
- **When** `TryParseHasValue` runs with `isNegated: false`
- **Then** the condition is `IsNull` with `IsNot` toggled on, rendering `ClosedAt IS NOT NULL`

#### Scenario: Negated HasValue becomes IS NULL

- **Given** `x => !x.ClosedAt.HasValue`
- **When** the `Not` branch calls `TryParseHasValue` with `isNegated: true`
- **Then** `IsNot` is left alone, rendering `ClosedAt IS NULL`

#### Scenario: HasValue on a closure nullable is a bool constant

- **Given** `x => x.Flag == filterId.HasValue` where `filterId` is a captured nullable
- **When** the closure `HasValue` member is parsed under a parent condition
- **Then** the evaluated bool is bound as the parent's single value rather than becoming a null check

### Requirement: SQL bare boolean column access

The SQL translator SHALL translate a `bool`-typed member accessed directly off the lambda parameter
(optionally through a `Convert`) into an explicit equality condition with `Values = { true }` and the
column name resolved with the table qualifier.

#### Scenario: Bare boolean member

- **Given** `s => s.IsActive`
- **When** the top-level `MemberExpression` reaches the bare-boolean branch
- **Then** the condition is `Name = ResolveColumnName(exprType, "IsActive", withTableName: true)`, `Type = Equal`, `Values = { true }`

#### Scenario: Unresolvable column falls back to the property name

- **Given** a bare boolean member whose property has no mapped field and no `ResolveFieldSelectName` hit
- **When** the condition name is assigned
- **Then** the raw C# member name is used as the column name

### Requirement: SQL string pattern methods

The SQL translator SHALL map `StartsWith` to `ConditionType.StartsWith`, `EndsWith` to
`ConditionType.EndsWith`, and `String.Contains` to `ConditionType.Like`; SHALL take ONLY the first
argument as the search pattern for those three methods, ignoring any `StringComparison`, `bool` or
`CultureInfo` overload argument; and `SqlBuilderContext.FormatValue` SHALL wrap the bound value with
`value%`, `%value%` and `%value` respectively. `LikeConditionStrategy` SHALL render ` LIKE ` or
` NOT LIKE `.

#### Scenario: Culture-aware Contains ignores the comparison argument

- **Given** `x => x.Title.Contains(query, StringComparison.OrdinalIgnoreCase)`
- **When** the arguments are processed
- **Then** only `query` is parsed as the pattern, the enum argument is skipped, and the SQL is `Title LIKE @param` with the parameter bound to `%query%`

#### Scenario: StartsWith wildcards only on the right

- **Given** a `StartsWith` condition with the string value `"FV"`
- **When** `FormatValue` runs
- **Then** the bound parameter value is `"FV%"`

#### Scenario: Negated pattern match

- **Given** a `Like` condition with `IsNot = true`
- **When** `LikeConditionStrategy.BuildSql` runs
- **Then** the SQL operator is ` NOT LIKE `

### Requirement: SQL method calls outside the recognised set

For a method call whose name is not one of `StartsWith`, `EndsWith`, `Contains`, `ToLower`,
`ToLowerInvariant`, `ToUpper`, `ToUpperInvariant`, `DataBase.ParseConditionExpression` SHALL leave the
condition type at its incoming value (`ConditionType.Equal` for a freshly created condition), parse each
non-skipped argument into that condition, then parse the call's `Object` into it, and return the
condition.

#### Scenario: String.IsNullOrEmpty produces an always-unknown equality

- **Given** `x => string.IsNullOrEmpty(x.Name)`
- **When** the method name matches no case in the switch, the single argument `x.Name` resolves the condition's `Name`, and no value is ever bound
- **Then** the condition is `Type = Equal` with `Values = null`, which `EqualConditionStrategy` renders as `Name = NULL` — UNKNOWN for every row, so the predicate matches nothing. ElasticSearch translates the same call to `BoolQuery.MustNot(WildcardQuery "*")`

#### Scenario: A conversion operator call is recursed through

- **Given** the `op_Implicit` array-to-span conversion .NET inserts as the source argument of `MemoryExtensions.Contains`
- **When** the call reaches the method-call branch, matches no case, and its arguments are parsed
- **Then** the wrapped array expression is reached and evaluated as the condition's values, so the `In` condition is still populated

### Requirement: SQL non-operand method arguments are skipped

`DataBase.IsNonOperandArgument` SHALL return true — and the argument SHALL be skipped — for arguments
typed `StringComparison`, `CultureInfo`, non-generic `IEqualityComparer`/`IComparer`, or generic
`IEqualityComparer<>`/`IComparer<>`, regardless of whether the argument's value is null.

#### Scenario: Trailing null comparer of the span Contains overload

- **Given** `statuses.Contains(x.Status)` where `statuses` is an array of an enum type, which on .NET 9+ binds `MemoryExtensions.Contains(ReadOnlySpan<T>, T, IEqualityComparer<T>?)` with a trailing `null` comparer
- **When** the arguments are iterated
- **Then** the `IEqualityComparer<T>` argument is skipped, so it cannot flip the condition to `IsNull` and the `IN` predicate keeps its operand

#### Scenario: A non-null comparer is skipped too

- **Given** `set.Contains(x.Name, StringComparer.OrdinalIgnoreCase)`
- **When** the arguments are iterated
- **Then** the comparer is skipped and its comparison semantics are delegated to the column collation

### Requirement: SQL IN translation and empty sets

A non-`String` `Contains` SHALL produce `ConditionType.In`. `InConditionStrategy` SHALL render an
EMPTY value set as the constant `1 = 0` when not negated and `1 = 1` when negated — never `Col IN ()`.
A collection operand whose materialised, null-filtered contents are empty SHALL leave the condition an
`In` with an empty value array rather than degrading to `IsNull`.

#### Scenario: Empty IN is always false on every provider

- **Given** an `In` condition whose `Values` enumerates zero elements
- **When** `InConditionStrategy.BuildSql` runs
- **Then** it returns `"1 = 0"` with no parameters — valid on SQLite, PostgreSQL, MySQL and MSSQL alike, where `Col IN ()` is a syntax error on the latter two

#### Scenario: Empty NOT IN is always true

- **Given** the same empty `In` condition with `IsNot = true`
- **When** it is rendered
- **Then** it returns `"1 = 1"`, because "not a member of the empty set" is true of every row — the predicate is not inverted

#### Scenario: All-null collection collapses to matches-nothing

- **Given** `ids.Contains(x.Guid)` where `ids` yields only nulls
- **When** `InvokeExpression` filters nulls and the materialised array is empty while `parent.Type == In`
- **Then** `parent.Values` is set to `Array.Empty<object>()` and the clause renders `1 = 0` instead of `Col IS NULL`

#### Scenario: A NULL collection variable also matches nothing

- **Given** `x => ids.Contains(x.Guid)` where `ids` is a captured local whose value is `null`
- **When** the normalizer has already folded that closure `MemberExpression` to `Constant(null)` at the lambda boundary, so the collection operand reaches the `ConstantExpression` branch under the `In` parent condition and `InvokeExpression` yields `null`
- **Then** `parent.Values` is set to `Array.Empty<object>()` and the rendered SQL is `1 = 0`, exactly as for an empty collection and as ElasticSearch's `MatchNoneQuery` — the closure `MemberExpression` branch's `IsNull` path is not reached, because every entry point hands `ParseConditionExpression` a lambda and the normalizer folds every evaluatable closure operand before the parser sees it

#### Scenario: Empty inline array literal

- **Given** a `NewArrayExpression` with no initialisers as the collection operand
- **When** the array branch iterates its (zero) expressions
- **Then** `parent.Values` stays `null`, `IsEmpty` reports empty, and `1 = 0` is rendered

#### Scenario: Populated set is fully parameterised

- **Given** an `In` condition with three values and `IsField = false`
- **When** `BuildInClause` runs
- **Then** the SQL is `Name IN (@WHEREName0_n, @WHEREName1_n, @WHEREName2_n)` with one bound parameter per element

### Requirement: SQL negated groups keep their negation

`AbstractConnectorBase.AppendSubConditionsTo` SHALL join a condition's sub-conditions with the
PARENT's `IsOr` flag (` OR ` when set, otherwise ` AND `), SHALL parenthesise when there is more than
one child or when the group is negated, and SHALL prefix `NOT ` when the group's `IsNot` is set.
`ParseConditionExpression` SHALL implement `Not` by toggling `IsNot` on the condition it passes down.

#### Scenario: Negated OR group

- **Given** `x => !(x.A == 1 || x.B == 2)`
- **When** the `Not` branch toggles `IsNot` and the OR branch sets `IsOr` on the same condition
- **Then** the rendered SQL is `NOT (A = @p1 OR B = @p2)` — the negation is not dropped

#### Scenario: Single-child negated group is still parenthesised

- **Given** a negated group with exactly one child
- **When** `AppendSubConditionsTo` finishes
- **Then** parentheses are inserted anyway (because `condition.IsNot` is set) and `NOT ` is prefixed

#### Scenario: Children's own IsOr flags are ignored inside a group

- **Given** a parent condition with `IsOr = false` whose children each carry `IsOr = true`
- **When** the group is rendered
- **Then** the children are joined with ` AND `, because only the parent's flag selects the separator

#### Scenario: A top-level condition list uses each element's own flag

- **Given** a sequence of three sibling conditions where the second and third have `IsOr = true`
- **When** `ConditionDefinition(IEnumerable<Condition>, DbCommand)` renders them
- **Then** the separators are taken from each subsequent element's `IsOr`, so the SQL is `c1 OR c2 OR c3`

### Requirement: SQL value-expression operands in predicates

When either side of a comparison (after stripping `Convert`) is numeric arithmetic, a `Coalesce`, or a
`ConditionalExpression`, `DataBase` SHALL render that side to a RAW SQL fragment — `(A + B)`,
`COALESCE(a, b)`, `CASE WHEN … THEN … ELSE … END` — place it in the condition's `Name`, and compare it
to the other side. Constants inside such a fragment SHALL be inlined as portable SQL literals rather
than parameterised. When the value expression is on the LEFT and the constant on the right, the
operator SHALL be used as-is; when the constant is on the left and the parameter-bearing expression on
the right, the comparison SHALL be flipped via `FlipComparison`. When both sides reference the
parameter, the right fragment SHALL be emitted verbatim with `IsField = true`.

#### Scenario: Column arithmetic in WHERE

- **Given** `x => x.A + x.B > 5`
- **When** `BuildValueComparison` sees a parameter-bearing left and a constant right
- **Then** the condition is `Name = "(Table.A + Table.B)"`, `Type = Greather`, `Values = { 5 }`, rendering `(Table.A + Table.B) > @param`

#### Scenario: Null-coalescing operand

- **Given** `x => (x.Score ?? 0) > 5`
- **When** the operand is rendered
- **Then** the fragment is `COALESCE(Table.Score, 0)` with the `0` inlined as a literal

#### Scenario: CASE in WHERE

- **Given** `x => (x.Vip ? x.Premium : x.Score) > 100`
- **When** `RenderValueFragment` handles the `ConditionalExpression`
- **Then** the fragment is `CASE WHEN (Table.Vip <> 0) THEN Table.Premium ELSE Table.Score END`, the boolean test rendered by `RenderBoolFragment` with a bare bool column compared to `0`

#### Scenario: Column-versus-column comparison

- **Given** `x => x.Total == x.A + x.B`
- **When** both sides reference the parameter
- **Then** the condition is created with `isField: true` and the right fragment is emitted verbatim, not bound as a parameter

#### Scenario: Constant on the left flips the operator

- **Given** a comparison whose left side is a constant and right side a value expression with `Type = Less`
- **When** `BuildValueComparison` takes the flip branch
- **Then** the condition type becomes `Greather` so the column expression stays on the left of the rendered operator

#### Scenario: Null-valued equality inside a value comparison becomes IS NULL

- **Given** a value comparison whose evaluated constant side is `null` and whose type is `Equal`
- **When** `MakeValueCondition` runs
- **Then** it returns an `IsNull` condition with no values

#### Scenario: Non-portable literal fails loud

- **Given** a value fragment containing a `DateTime` or `Guid` constant
- **When** `InlineConstant` is reached
- **Then** a `NotSupportedException` is thrown rather than emitting non-portable SQL

#### Scenario: Untranslatable operand fails loud

- **Given** a parameter-bearing operand inside a value fragment that is none of column access, arithmetic, coalesce, ternary or nullable `.Value`
- **When** `RenderValueFragment` reaches its default branch
- **Then** a `NotSupportedException` naming the operand is thrown instead of silently dropping it

#### Scenario: Boolean sub-expression it cannot inline fails loud

- **Given** a CASE test containing a string `LIKE`-style method call
- **When** `RenderBoolFragment` reaches its default branch and the expression contains a parameter
- **Then** a `NotSupportedException` is thrown

### Requirement: SQL case-normalisation and date truncation wrappers

The SQL translator SHALL wrap a resolved column in `LOWER(...)` for `ToLower`/`ToLowerInvariant`,
`UPPER(...)` for `ToUpper`/`ToUpperInvariant`, and `DATE(...)` for a `DateTime.Date` member access
whose inner expression resolves to a column; a `DateTime.Date` access that does not resolve to a column
SHALL be evaluated as a constant.

#### Scenario: Case-insensitive comparison via LOWER

- **Given** `x => x.Name.ToLower() == "abc"`
- **When** the `ToLower` case runs and the inner member resolves to `Table.Name`
- **Then** the condition name becomes `LOWER(Table.Name)`

#### Scenario: Date truncation on the column side

- **Given** `x => x.OpenedAt.Date == someDate`
- **When** the `.Date` member is parsed and the inner member contains the parameter
- **Then** the condition name becomes `DATE(Table.OpenedAt)`

#### Scenario: Date truncation on the constant side

- **Given** the right-hand side `filter.From.Date` where `filter` is a closure
- **When** the `.Date` branch finds no parameter
- **Then** the evaluated `DateTime` is bound as the parent's value and an EMPTY condition sequence is returned

### Requirement: SQL condition rendering strategy dispatch

`AbstractConnectorBase` SHALL register exactly five condition strategies —
`EqualConditionStrategy`, `ComparisonConditionStrategy`, `LikeConditionStrategy`,
`InConditionStrategy`, `NullConditionStrategy` — into a map keyed by `ConditionType`, SHALL throw
`NotSupportedException` for a condition type with no strategy, and SHALL throw
`InvalidOperationException` when a non-group condition has a null or empty `Name`.

#### Scenario: Unmapped condition type

- **Given** a condition whose `Type` has no registered strategy
- **When** `BuildSingleCondition` looks it up
- **Then** a `NotSupportedException` naming the type is thrown

#### Scenario: Nameless leaf condition

- **Given** a condition with no sub-conditions and `Name = null`
- **When** `BuildSingleCondition` runs
- **Then** an `InvalidOperationException` "Condition name cannot be null or empty for non-subconditions" is thrown

#### Scenario: A condition with values but no name never reaches SQL silently

- **Given** a predicate whose column could not be resolved, leaving `Name` empty while values were bound
- **When** the condition is rendered
- **Then** the build throws rather than emitting a malformed clause

### Requirement: SQL value binding and parameter naming

`SqlBuilderContext.GenerateParameterName` SHALL produce `@WHERE{sanitizedFieldName}{index}_{parameterCount}`
where the field name is stripped of every character outside `[a-zA-Z0-9_]`, and SHALL throw
`ArgumentException` for an empty field name. Each strategy SHALL bind the value as a parameter only
when `IsField` is false and the value is non-null; otherwise it SHALL emit the value's `ToString()` (or
the literal `NULL`) directly into the SQL.

#### Scenario: Raw fragment name is sanitised into the parameter name

- **Given** a condition whose `Name` is the fragment `(Table.A + Table.B)`
- **When** a parameter name is generated for index 0 on a command with no parameters
- **Then** the name is `@WHERETableATableB0_0`

#### Scenario: Null value renders as the SQL literal NULL

- **Given** an `Equal` condition whose first value is `null`
- **When** `EqualConditionStrategy.BuildValueExpression` runs
- **Then** it returns the string `"NULL"` and binds no parameter, producing `Name = NULL`

#### Scenario: Missing values collection renders as NULL

- **Given** a `Less` condition with `Values = null`
- **When** `ComparisonConditionStrategy.BuildValueExpression` runs
- **Then** it returns `"NULL"`, producing `Name < NULL`

#### Scenario: Field comparison emits the operand verbatim

- **Given** a condition with `IsField = true` and a single value `"Other.Col"`
- **When** the value expression is built
- **Then** `Other.Col` is written straight into the SQL with no parameter

### Requirement: Enum parameter normalisation across providers

`AbstractConnectorBase.NormalizeParameterValue` SHALL convert a boxed enum to its underlying integral
value and pass every other value through unchanged, and EVERY provider's `AddParameter` override SHALL
funnel its value through it because those overrides do not chain to the base implementation.

#### Scenario: Enum bound as its integer

- **Given** a bound value that is an enum with underlying type `int`
- **When** `NormalizeParameterValue` runs
- **Then** the boxed `int` is returned, so Npgsql (which rejects an unmapped CLR enum) accepts it

#### Scenario: All four providers normalise

- **Given** `SqLiteConnector`, `MSSqlConnector`, `PostgreSQLConnector` and `MySQLConnector`
- **When** each one's `AddParameter` is inspected
- **Then** each calls `NormalizeParameterValue(value)` as its first statement before creating or updating the parameter

#### Scenario: SQLite additionally stringifies Guid

- **Given** a `Guid` value bound through `SqLiteConnector.AddParameter`
- **When** the value is normalised
- **Then** it is additionally converted with `ToString()`, because SQLite stores `DbType.Guid` as TEXT

#### Scenario: Null becomes DBNull

- **Given** a `null` value
- **When** any provider's `AddParameter` runs
- **Then** the parameter value is set to `DBNull.Value`

### Requirement: Provider identifier quoting

`AbstractConnectorBase.QuoteIdentifier` SHALL default to ANSI double quotes with internal `"` doubled;
`MSSqlConnector` SHALL override it to `[...]` with `]` doubled; `MySQLConnector` SHALL override it to
backticks with `` ` `` doubled; SQLite and PostgreSQL SHALL keep the default.

#### Scenario: MSSQL bracket quoting

- **Given** the identifier `Order]s`
- **When** `MSSqlConnector.QuoteIdentifier` runs
- **Then** the result is `[Order]]s]`

#### Scenario: MySQL backtick quoting

- **Given** the identifier `` a`b ``
- **When** `MySQLConnector.QuoteIdentifier` runs
- **Then** the result is `` `a``b` ``

#### Scenario: PostgreSQL inherits ANSI quoting

- **Given** `PostgreSQLConnector`, which declares no `QuoteIdentifier` override
- **When** an identifier is quoted
- **Then** the base double-quote form is used

### Requirement: SQL WHERE assembly and pagination

`AbstractConnectorBase.AddWhere` SHALL append ` WHERE ` plus the rendered clause only when both the
command and the condition sequence are non-null AND the rendered clause is non-empty.
`LimitOffsetDefinition` SHALL emit ` LIMIT @LIMIT[ OFFSET @OFFSET]` by default and `MSSqlConnector`
SHALL override it to `[ OFFSET @OFFSET ROWS] FETCH NEXT @LIMIT ROWS ONLY` — appending the
` OFFSET @OFFSET ROWS` half only when `offset` is non-null, so a limit-only read renders a bare
` FETCH NEXT @LIMIT ROWS ONLY`, which T-SQL rejects without a preceding `OFFSET`; both SHALL emit
nothing when `limit` is null.

#### Scenario: Empty rendered clause appends nothing

- **Given** a non-null but empty condition sequence
- **When** `AddWhere` runs
- **Then** `ConditionDefinition` returns an empty string and the command text is left untouched

#### Scenario: Offset without limit is ignored

- **Given** `limit = null` and `offset = 20`
- **When** `LimitOffsetDefinition` runs on the base connector
- **Then** it returns `null` and no parameters are bound

#### Scenario: MSSQL pagination shape

- **Given** `limit = 10`, `offset = 20` on `MSSqlConnector`
- **When** `LimitOffsetDefinition` runs
- **Then** the fragment is ` OFFSET @OFFSET ROWS FETCH NEXT @LIMIT ROWS ONLY` with both parameters bound

#### Scenario: MSSQL limit without offset omits the OFFSET T-SQL requires

- **Given** `limit = 10`, `offset = null` on `MSSqlConnector`
- **When** `LimitOffsetDefinition` runs
- **Then** the fragment is ` FETCH NEXT @LIMIT ROWS ONLY` with only `@LIMIT` bound — no ` OFFSET @OFFSET ROWS` precedes it, so the statement is invalid T-SQL

### Requirement: SQL join condition assembly

`AbstractConnectorBase.CreateSelectCommand` SHALL map `JoinType.Inner` to ` INNER JOIN `,
`JoinType.LeftOuter` to ` LEFT OUTER JOIN ` and anything else to ` CROSS JOIN `, SHALL render the
join's conditions inside ` ON (...)` for non-Cross joins, and SHALL EMIT NO JOIN AT ALL for a join
group whose conditions collection is empty.

#### Scenario: Inner join with an ON clause

- **Given** a `Join` from `Invoice` to `Customer` of type `Inner` carrying one condition
- **When** the select command is built
- **Then** the text contains ` INNER JOIN "Customer" ON (` followed by the rendered condition

#### Scenario: Conditionless join is dropped

- **Given** `Join.Create("A", "B", JoinType.Cross)` with no conditions
- **When** the join groups are filtered by `Where(x => x.Value.Any())`
- **Then** the group is skipped entirely and no `CROSS JOIN` text is emitted

#### Scenario: Join conditions are added cumulatively

- **Given** a `Join` and two successive `AddCondition` calls
- **When** the second is added
- **Then** `Conditions` is the concatenation of both, never replaced

### Requirement: SQL value-position expression rendering

`DataBase.ParseExpression` SHALL render an expression in VALUE position (an UPDATE SET right-hand side
or a view field) as SQL text and SHALL collect every constant it meets into the supplied parameter
dictionary under keys `@Const{count}`. It SHALL render a `ConditionalExpression` as
`CASE WHEN … THEN … ELSE … END`, a `Coalesce` as `COALESCE(left, right)`, an equality against a null
constant as `(operand IS NULL)` / `(operand IS NOT NULL)`, arithmetic and comparison operators inside
parentheses, `Replace` as `REPLACE(...)`, `ToLower`/`ToLowerInvariant` as `LOWER(...)`,
`ToUpper`/`ToUpperInvariant` as `UPPER(...)`, and `DateTime.Date` on a column as `DATE(column)`. Any
other method call SHALL be evaluated and bound as a constant.

#### Scenario: Ternary SET value

- **Given** the value expression `x => x.Vip ? x.Premium : x.Score`
- **When** `ParseExpression` runs
- **Then** it returns `CASE WHEN Table.Vip THEN Table.Premium ELSE Table.Score END`

#### Scenario: Null test inside a CASE becomes IS NULL

- **Given** the CASE test `x.DeletedAt == null`
- **When** the binary node is rendered
- **Then** it becomes `(Table.DeletedAt IS NULL)`, because `= NULL` would be UNKNOWN

#### Scenario: Constants are parameterised, not inlined

- **Given** the value expression `x => x.Price * 2`
- **When** the constant `2` is reached
- **Then** a key `@Const{n}` is added to the parameters dictionary and that key is emitted into the SQL

#### Scenario: Convert wrappers are transparent

- **Given** a `Convert` unary node in value position
- **When** `ParseExpression` handles it
- **Then** it recurses into the operand and returns its rendering

#### Scenario: An unhandled node type yields null

- **Given** a node that is none of lambda, conditional, binary, method call, `Convert` unary, member or constant
- **When** `ParseExpression` runs
- **Then** it returns `null` (and a `Convert`-less unary node also returns `null`)

### Requirement: Rules-to-SQL condition conversion

`RuleConditionConverter` SHALL skip any rule, rule group or rule set whose `IsEnabled` is false; SHALL
convert a leaf `Rule` into a `Condition` whose name is the **resolved column** for the rule's `Field`
(never the `Field` string itself — see the rule-field resolution requirement below); SHALL map `NotEqual`,
`NotContains`, `NotIn` and `IsNotNull` to their positive `ConditionType` with `IsNot` inverted relative
to the rule's own `IsNegated`; SHALL wrap a group's children in a sub-condition; and SHALL return an
empty sequence for a group with no rules or no enabled rules.

#### Scenario: Disabled rule contributes nothing

- **Given** a `Rule` with `IsEnabled = false`
- **When** `ToConditions(rule)` is called
- **Then** an empty sequence is returned, and its `Field` is never checked — a disabled rule carrying a
  SQL payload is dropped before conversion rather than refused

### Requirement: Rule fields resolve against table metadata before reaching the WHERE clause

Every condition strategy interpolates `Condition.Name` directly into `CommandText`
(`EqualConditionStrategy` renders `$"{condition.Name}{op}{value}"`), so a rule's `Field` is an identifier
sink, not a value. `RuleConditionConverter` SHALL therefore refuse any `Field` it cannot account for,
before any statement is built.

Given an entity type — `ToConditions<T>(…)` or `ToConditions(Type, …)` — it SHALL resolve the `Field`
through `DataBase.ResolveRuleField`, matching a CLR property name first and then a mapped column name, and
SHALL emit the resolved, **table-qualified** select name. Without an entity type, the type-less overloads
SHALL require the `Field` to be a bare, optionally table-qualified SQL identifier
(`DataBase.ValidateRuleFieldIdentifier`) and SHALL emit it unchanged.

A `Field` that is blank, unresolvable, or not a bare identifier SHALL raise `ArgumentException` naming the
field (and, on the type-aware path, the entity type). The resolved identifier SHALL NOT be quoted, for the
same reason recorded for the ORDER BY sink: this codebase emits column identifiers bare everywhere and
quotes only table names, so quoting would break a working filter on PostgreSQL, where an unquoted DDL
identifier folds to lower case.

#### Scenario: A remapped property filters on its mapped column

- **Given** an entity with `[NamedField("label_col")] string Label` and a rule with `Field = "Label"`
- **When** `ToConditions<T>(rule)` is called
- **Then** the condition's name is `TableName.label_col`, and the emitted clause is
  `TableName.label_col = @param` — previously the CLR name was emitted and the database answered
  *no such column: Label*, so a remapped property could not be filtered at all

#### Scenario: The mapped column name is also accepted

- **Given** a rule whose `Field` is the mapped column name (`"label_col"`) rather than the property name
- **When** `ToConditions<T>(rule)` is called
- **Then** it resolves to the same table-qualified name — a caller passing the column name directly worked
  before the guard existed and is drawn from the same metadata, so it is equally safe

#### Scenario: A field carrying SQL is refused before a statement is built

- **Given** a rule whose `Field` is `Rank OR 1=1 --`, `Rank; CREATE TABLE Pwned (x INTEGER); --`,
  `(SELECT count(*) FROM sqlite_master)` or `1=1 OR 1=1 --`
- **When** conditions are requested through either the type-aware or the type-less overload
- **Then** `ArgumentException` naming the field is raised and no statement is executed. Measured against
  SQLite before this held: the first returned every row for a filter matching none, the second **created
  the table**, the third evaluated the subquery as the left operand. The trailing ` = @param` the strategy
  appends is not a mitigation — `--` comments it out

#### Scenario: A field naming no column of the entity is refused

- **Given** a rule with `Field = "NoSuchProperty"` and a type-aware conversion
- **When** conditions are requested
- **Then** `ArgumentException` names both the field and the entity type, rather than letting the database
  answer with a column name it never heard of

#### Scenario: The guard reaches every rule in a tree

- **Given** a payload field on a rule nested inside an AND group, an OR group, or a `RuleSet`
- **When** conditions are requested
- **Then** the conversion raises rather than emitting it. `RuleSet` conversion is materialised rather than
  lazy so the refusal surfaces at the call, not later from inside the connector's statement builder

#### Scenario: NotIn becomes IN with IsNot

- **Given** a rule with `Operator = NotIn`, `IsNegated = false` and a collection value
- **When** `ConvertLeaf` runs
- **Then** the condition is `Type = In`, `IsNot = true`, and the value is passed through as an `IEnumerable` rather than wrapped in a single-element array

#### Scenario: Double negation cancels

- **Given** a rule with `Operator = NotEqual` and `IsNegated = true`
- **When** `ConvertLeaf` runs
- **Then** `IsNot` becomes `false` (`!IsNegated`), so the condition is a plain equality

#### Scenario: IsNull carries no values

- **Given** a rule with `Operator = IsNull`
- **When** `MapOperator` runs
- **Then** the values are `null`, matching `NullConditionStrategy` which binds nothing

#### Scenario: Empty group is dropped

- **Given** a `RuleGroup` whose `Rules` collection is empty, or all of whose rules are disabled
- **When** `ConvertGroup` runs
- **Then** an empty sequence is returned rather than an empty sub-condition

#### Scenario: An OR rule group is wrapped in an AND sub-condition

- **Given** a `RuleGroup` with `Logic = LogicOperator.Or` and two enabled leaf rules
- **When** `ConvertGroup` calls `SetOr(children)` — which sets `IsOr = true` on every child after the first — and passes the result to `Condition.AndSubCondition`, whose wrapper is constructed with `isOr: false`
- **Then** the wrapper's `IsOr` is `false`, and because `AppendSubConditionsTo` selects its separator from the PARENT's flag and ignores the children's, the group renders as `(a AND b)`

#### Scenario: A Between rule keeps only its lower bound

- **Given** a leaf rule with `Operator = ComparisonOperator.Between`
- **When** `MapOperator` runs
- **Then** it returns `(ConditionType.GreatherAndEqual, [rule.Value])` — a single `>=` comparison with no upper bound

#### Scenario: Negated group sets IsNot on the wrapper

- **Given** an AND `RuleGroup` with `IsNegated = true` and two enabled children
- **When** `ConvertGroup` runs
- **Then** a single sub-condition with `IsNot = true` wrapping both children is returned, which `AppendSubConditionsTo` renders as `NOT (a AND b)`

### Requirement: ElasticSearch filter translation guard

`ElasticSearch.ParseFilterQuery` SHALL return `null` for a `null` filter (meaning "read everything on
purpose"), SHALL throw `NotSupportedException` when a filter was supplied but `ParseExpression`
returned `null` or threw, and SHALL pass an explicit `MatchNoneQuery` through untouched.
`ParseRequiredFilterQuery` SHALL additionally throw `ArgumentNullException` for a `null` filter. Every
read/count/aggregate site in `ElasticSearchStore` and `AsyncElasticSearchStore` SHALL use
`ParseFilterQuery`, and the four destructive by-query paths (`Delete(filter)`, `Update(filter, updates)`
and their async counterparts) SHALL use `ParseRequiredFilterQuery`.

#### Scenario: Untranslatable filter throws instead of matching all documents

- **Given** a supplied filter whose top-level translation yields `null`
- **When** `ParseFilterQuery` inspects the result
- **Then** a `NotSupportedException` is thrown, naming the filter and suggesting a simpler shape — rather than leaving `Query = null`, which ElasticSearch reads as match-all

#### Scenario: Translator exception is wrapped

- **Given** a filter whose translation throws (for example a `NotSupportedException` from `ScriptValue`)
- **When** `ParseFilterQuery` catches it
- **Then** a `NotSupportedException` with the original as `InnerException` is thrown

#### Scenario: Null filter on a destructive path is refused

- **Given** `null` passed to `ParseRequiredFilterQuery`
- **When** the guard runs
- **Then** an `ArgumentNullException` is thrown, because a missing filter on `_delete_by_query` would target every document

#### Scenario: Matches-nothing is a valid translation, not an error

- **Given** a filter whose only clause is an empty-collection `Contains`
- **When** `ParseContains` returns `MatchNoneQuery`
- **Then** `ParseFilterQuery` returns it unchanged and no exception is raised

#### Scenario: Null filter on a read path omits the query

- **Given** `ReadCore(filter: null)`
- **When** `ParseFilterQuery` returns `null` and `ReadStream(QueryContainer?, …)` receives it
- **Then** `request.Query` is left unset and the search returns everything

### Requirement: ElasticSearch predicate-position handling

`ParsePredicate` SHALL be used for a lambda body and for the operands of `&&`, `||`, `&`, `|` and `!`,
and SHALL unwrap `Convert`, translate any parameter-free `bool` expression to `MatchAllQuery` /
`MatchNoneQuery`, and translate a bare `bool` member of the parameter to `TermQuery { Field, Value = true }`.
`ParseBinary` SHALL treat bitwise `&`/`|` on `bool` as logical AND/OR.

#### Scenario: Constant true predicate

- **Given** `x => true`
- **When** `ParsePredicate` evaluates the parameter-free bool
- **Then** a `MatchAllQuery` is returned

#### Scenario: Constant false predicate

- **Given** `x => false`, or a closure bool that evaluates to false
- **When** `ParsePredicate` runs
- **Then** a `MatchNoneQuery` is returned

#### Scenario: Bare boolean member

- **Given** `x => x.IsActive`
- **When** `ParsePredicate` recognises a direct `bool` member of the parameter
- **Then** a `TermQuery` on the camelCase field `isActive` with `Value = true` is returned

#### Scenario: Bitwise operators on booleans are logical

- **Given** `x => x.A & x.B`
- **When** `ParseBinary` matches `ExpressionType.And when binary.Type == typeof(bool)`
- **Then** a `BoolQuery` with both clauses in `Must` is produced

#### Scenario: AND produces Must, OR produces Should

- **Given** `x => x.A == 1 || x.B == 2`
- **When** `CombineBool(isOr: true)` runs
- **Then** a `BoolQuery` with both clauses in `Should` is returned

### Requirement: ElasticSearch comparison translation

`ParseComparison` SHALL derive the field from whichever operand yielded an `ITermQuery` with a
non-null `Field` and the value from whichever yielded a non-null `Value`, and SHALL emit
`NumericRangeQuery` for `>`/`>=`/`<`/`<=`, `TermQuery` for `==`, and a `BoolQuery.MustNot` wrapping a
`TermQuery` for `!=`. When the field is known and the value is null it SHALL emit
`BoolQuery.MustNot(ExistsQuery)` for `==` and `ExistsQuery` for `!=`, and `null` for any other
operator. It SHALL return `null` when either field or value is unavailable.

#### Scenario: Numeric greater-than becomes a range query

- **Given** `x => x.Age > 18`
- **When** the comparison is translated
- **Then** a `NumericRangeQuery { Field = "age", GreaterThan = 18 }` is produced

#### Scenario: Null equality becomes a negated exists

- **Given** `x => x.DeletedAt == null`
- **When** the field is resolved and the value is null
- **Then** a `BoolQuery` with `MustNot = [ExistsQuery { Field = "deletedAt" }]` is returned

#### Scenario: Null inequality becomes exists

- **Given** `x => x.DeletedAt != null`
- **When** the comparison is translated
- **Then** an `ExistsQuery` on `deletedAt` is returned

#### Scenario: Ordering comparison against null is untranslatable

- **Given** `x => x.Age > (int?)null`
- **When** the null-value branch matches neither `Equal` nor `NotEqual`
- **Then** `null` is returned, which `ParseFilterQuery` converts into a `NotSupportedException`

#### Scenario: Constant on the left is not flipped

- **Given** `x => 18 > x.Age`
- **When** `ParseComparison` takes `field` from the right operand and `value` from the left, then switches on `binary.NodeType`
- **Then** a `NumericRangeQuery { Field = "age", GreaterThan = 18 }` is produced, meaning `Age > 18` — the operator is not flipped to compensate for the swapped operands

#### Scenario: A value that will not convert to double yields an unbounded range

- **Given** `x => x.CreatedAt > someDateTime`
- **When** `TryConvertToDouble` catches the `InvalidCastException` and returns `null`
- **Then** a `NumericRangeQuery` with `GreaterThan = null` is produced — a range with no bound

### Requirement: ElasticSearch field naming

`FormatFieldName` SHALL lower-case the first character of the property name and leave the rest
unchanged, SHALL return `null` for a null/empty name, and SHALL prefix the result with
`{prefix}.` when a field prefix is in effect. `ParseMember` and `TryScriptFieldName` SHALL append
`.keyword` to a `string`-typed field unless it already ends with `.keyword` (case-insensitively).
`IsDirectMemberOfParameter` SHALL accept a member accessed off the parameter, off a `TypeAs`, or off a
`Convert` of the parameter.

#### Scenario: camelCase conversion

- **Given** the property `OrderNumber`
- **When** `FormatFieldName` runs with no prefix
- **Then** the field name is `orderNumber`

#### Scenario: String field gets the keyword sub-field

- **Given** `x => x.Name == "abc"`
- **When** `ParseMember` resolves the `string` member
- **Then** the term query field is `name.keyword`

#### Scenario: Nested prefix from Any

- **Given** `x => x.Lines.Any(l => l.Sku == "A")`
- **When** `ParseAny` recurses with the outer field name as the prefix
- **Then** the inner field is `lines.sku.keyword` and the whole thing is wrapped in a `NestedQuery` with `Path = lines`

#### Scenario: Interface property through a Convert

- **Given** a member accessed as `((ITenanted)x).TenantGuid`, which the compiler emits as `Convert(param)` then member access
- **When** `IsDirectMemberOfParameter` runs
- **Then** it returns true and the field resolves normally

### Requirement: ElasticSearch string and collection methods

`ParseMethodCall` SHALL translate `IsNullOrEmpty` to `BoolQuery.MustNot(WildcardQuery value "*")`,
`StartsWith` to `PrefixQuery`, `EndsWith` to `WildcardQuery` with value `"*" + value`,
`String.Contains` to `QueryStringQuery` on the field, `MultiMatch` to `MultiMatchQuery`, `Any` to
`NestedQuery`, `Property(name)` to a field-only `TermQuery`, and `ToLower`/`ToLowerInvariant`/
`ToUpper`/`ToUpperInvariant` transparently to the wrapped expression. Any other method SHALL be
evaluated as a constant, yielding a value-only `TermQuery` or `null`.

#### Scenario: EndsWith uses a leading wildcard

- **Given** `x => x.Name.EndsWith("son")`
- **When** the call is translated
- **Then** a `WildcardQuery { Field = "name.keyword", Value = "*son" }` is produced

#### Scenario: Case normalisation is transparent

- **Given** `x => x.Name.ToLower() == "abc"`
- **When** `ParseMethodCall` sees `ToLower`
- **Then** it returns the translation of `call.Object`, so case-sensitivity is delegated to the field's analyzer

#### Scenario: Missing operand makes the method untranslatable

- **Given** `StartsWith` whose object does not resolve to a field, or whose argument has no value
- **When** `ParseStartsWith` runs
- **Then** `null` is returned

#### Scenario: An unknown parameter-bound method call is untranslatable

- **Given** `x => x.Name.Substring(2) == "ab"`
- **When** `ParseConstantCall` calls `EvaluateExpression`, which returns `null` for a parameter-bound expression
- **Then** `null` is returned and the surrounding comparison cannot resolve a value

### Requirement: ElasticSearch collection membership

`ParseContains` on a non-string `Contains` SHALL pick the parameter-bearing operand as the field and
the parameter-free operand as the value(s), handling both the instance form `collection.Contains(x.F)`
and the static form `Enumerable.Contains(source, item)`; SHALL return `null` when both or neither
operand references the parameter; SHALL emit `TermsQuery` for a non-empty collection value, a single
`TermQuery` for a scalar value, and `MatchNoneQuery` for BOTH a null value and an empty (or all-null)
collection.

#### Scenario: IN pattern

- **Given** `x => ids.Contains(x.Guid)` with three ids
- **When** the call is translated
- **Then** a `TermsQuery { Field = "guid", Terms = [3 ids] }` is produced

#### Scenario: Empty collection matches nothing and survives clause combination

- **Given** `x => ids.Contains(x.Guid) && x.Status == active` with an EMPTY `ids`
- **When** `ParseContains` returns `MatchNoneQuery`
- **Then** `CombineBool` keeps it as a `Must` clause, so the query matches nothing — it does not collapse to `x.Status == active`

#### Scenario: Null collection matches nothing

- **Given** `x => ids.Contains(x.Guid)` where `ids` is `null`
- **When** `EvaluateExpression` yields `null`
- **Then** a `MatchNoneQuery` is returned

#### Scenario: Negated empty membership matches everything

- **Given** `x => !ids.Contains(x.Guid)` with an empty `ids`
- **When** `ParseNot` wraps the `MatchNoneQuery` in `MustNot`
- **Then** the query is "must not match nothing", i.e. every document — the correct reading of an empty `NOT IN`

#### Scenario: Array membership with a scalar argument

- **Given** `x => x.Tags.Contains("red")`
- **When** the field is the parameter-bearing operand and the value is the scalar `"red"`
- **Then** a `TermQuery { Field = "tags.keyword", Value = "red" }` is produced

#### Scenario: Both operands parameter-bound is untranslatable

- **Given** `x => x.TagsA.Contains(x.TagB)`
- **When** neither branch of the field/value split matches
- **Then** `null` is returned

### Requirement: ElasticSearch nullable and negation handling

`ParseMember` SHALL translate `Nullable<T>.HasValue` on a direct member of the parameter into an
`ExistsQuery`, SHALL evaluate a parameter-free member as a value-only `TermQuery` (or `null` when the
value is null), and SHALL recurse into `member.Expression` for a parameter-bound sub-expression.
`ParseUnary` SHALL make `Convert` transparent, wrap `Not` in `BoolQuery.MustNot`, and return `null` for
every other unary node type.

#### Scenario: HasValue becomes exists

- **Given** `x => x.ClosedAt.HasValue`
- **When** `ParseMember` matches the `HasValue` branch
- **Then** an `ExistsQuery { Field = "closedAt" }` is returned

#### Scenario: Negation wraps in MustNot

- **Given** `x => !x.IsActive`
- **When** `ParseNot` translates the operand via `ParsePredicate`
- **Then** a `BoolQuery { MustNot = [TermQuery isActive = true] }` is returned

#### Scenario: Negation of an untranslatable operand yields null

- **Given** `x => !(something untranslatable)`
- **When** `ParseNot` finds `operandQuery == null`
- **Then** `null` is returned and the guard turns it into a `NotSupportedException` if it reaches the top level

#### Scenario: Unsupported unary node type

- **Given** a `TypeAs`, `Negate` or `ArrayLength` unary node in predicate position
- **When** `ParseUnary` falls through its switch
- **Then** `null` is returned

### Requirement: ElasticSearch Painless script comparisons

When either operand of a comparison (after unwrapping `Convert`) is arithmetic, a `Coalesce` or a
`ConditionalExpression`, `BuildScriptComparison` SHALL emit a `ScriptQuery` whose Painless body is
`(left op right)`, SHALL prefix an existence guard `(doc['f'].size() > 0 && …) ? body : false` for every
field referenced OUTSIDE a coalesce, and SHALL throw `NotSupportedException` for an operator or operand
it cannot script. A field on the left of `??` SHALL be scripted as
`(doc['f'].size() == 0 ? fallback : doc['f'].value)` and SHALL NOT be added to the existence guard.

#### Scenario: Column arithmetic becomes a guarded script

- **Given** `x => x.A + x.B > 5`
- **When** `BuildScriptComparison` collects `a` and `b` as required fields
- **Then** the script is `(doc['a'].size() > 0 && doc['b'].size() > 0) ? ((doc['a'].value + doc['b'].value) > 5) : false`, so a missing field excludes the document exactly as C# null-propagation would

#### Scenario: Coalesce handles its own absence

- **Given** `x => (x.Score ?? 0) > 5`
- **When** the coalesce branch scripts the left field
- **Then** the emitted value is `(doc['score'].size() == 0 ? 0 : doc['score'].value)` and `score` is NOT added to the guard set

#### Scenario: Value ternary becomes a Painless ternary

- **Given** `x => (x.Vip ? x.Premium : x.Score) > 100`
- **When** `ScriptValue` handles the `ConditionalExpression`
- **Then** the body is `((doc['vip'].value) ? doc['premium'].value : doc['score'].value) > 100` with the boolean test rendered by `ScriptBool`

#### Scenario: Null check inside a script

- **Given** the ternary test `x.DeletedAt == null`
- **When** `ScriptBool` matches the null-constant branch
- **Then** it emits `(doc['deletedAt'].size() == 0)`

#### Scenario: Coalesce of something other than a field fails loud

- **Given** `x => ((x.A + x.B) ?? 0) > 5`, where the left of `??` is not a direct field and does reference the parameter
- **When** `ScriptValue` reaches the coalesce branch
- **Then** a `NotSupportedException` "ElasticSearch script coalescing supports only `field ?? value`" is thrown

#### Scenario: Non-scriptable constant fails loud

- **Given** a `DateTime` or `Guid` constant inside a script value
- **When** `ScriptConstant` runs
- **Then** a `NotSupportedException` is thrown rather than inlining an unusable literal

#### Scenario: String constants are escaped

- **Given** the string constant `it's\ok`
- **When** `ScriptConstant` renders it
- **Then** backslashes are doubled and single quotes are backslash-escaped before being wrapped in single quotes

### Requirement: ElasticSearch expression evaluation refuses parameter-bound trees

`ElasticSearch.EvaluateExpression` SHALL return `null` immediately for any expression that references a
lambda parameter, SHALL unwrap `op_Implicit`/`op_Explicit` special-name single-argument conversions by
evaluating their argument, and SHALL cache compiled fallback delegates keyed by the expression's
`ToString()`.

#### Scenario: Parameter-bound expression is never compiled

- **Given** an unrecognised method call containing the lambda parameter
- **When** `EvaluateExpression` is entered
- **Then** it returns `null` rather than compiling a lambda that would throw "variable 'x' referenced from scope"

#### Scenario: Span conversion is unwrapped

- **Given** the `int[] → ReadOnlySpan<int>` conversion .NET binds for `MemoryExtensions.Contains`
- **When** `EvaluateExpression` sees the special-name `op_Implicit` call
- **Then** it evaluates the array argument instead of invoking the ref-struct-returning conversion via reflection

### Requirement: SQL expression evaluation and parameter detection

`DataBase.EvaluateExpression` SHALL read `ConstantExpression` values directly, read field/property
values reflectively (falling through to lambda compilation when a `TargetException` says the container
was null), invoke method calls reflectively on evaluated operands, make `Convert` transparent,
materialise `NewArrayInit` into a typed array, and compile a cached parameterless lambda only when the
expression contains no parameter — returning `null` otherwise. `DataBase.ContainsParameter` SHALL be
memoized per expression instance via a `ConditionalWeakTable` and SHALL treat any
`LambdaExpression` with at least one parameter as containing a parameter.

#### Scenario: Closure field access

- **Given** a captured local wrapped in the compiler's closure class
- **When** `EvaluateExpression` reads the `FieldInfo` off the evaluated container
- **Then** the captured value is returned without compiling anything

#### Scenario: Parameter member access yields null gracefully

- **Given** `x.Name` where `x` is the lambda parameter
- **When** `EvaluateExpression` evaluates the container (the parameter) to `null` and the property getter throws `TargetException`
- **Then** the exception is caught and `null` is ultimately returned

#### Scenario: Array initialiser is materialised with its element type

- **Given** `new[] { 1, 2, 3 }` as a collection operand
- **When** the `NewArrayInit` branch runs
- **Then** an `int[]` of length 3 is created via `Array.CreateInstance(elementType, count)`

#### Scenario: Inline array of an unknown element type yields null

- **Given** a `NewArrayExpression` whose `Type.GetElementType()` is `null`
- **When** the branch runs
- **Then** `null` is returned

#### Scenario: Compiled delegates are cached by expression identity

- **Given** the same `Expression` instance evaluated twice
- **When** `_expressionCache.GetOrAdd` is consulted
- **Then** the delegate is compiled once and reused, keyed by reference (not by `ToString()`) so the cache cannot leak across differently-shaped trees with identical text

### Requirement: SQL collection materialisation drops nulls

`DataBase.InvokeExpression` SHALL return `null` for a null evaluated value, a single-element array for
a `string`, the null-filtered elements for any other `IEnumerable`, and a single-element array
otherwise.

#### Scenario: String is not treated as a character collection

- **Given** an evaluated value `"abc"`
- **When** `InvokeExpression` runs
- **Then** it returns `new object[] { "abc" }`, not three characters

#### Scenario: Nulls inside a collection are removed

- **Given** an evaluated collection `[g1, null, g2]`
- **When** `InvokeExpression` runs
- **Then** only `g1` and `g2` survive, and an all-null collection materialises empty

### Requirement: Silent widening of untranslatable SQL predicates

For an expression node the SQL predicate parser does not handle AND with no parent condition,
`DataBase.ParseConditionExpression` SHALL return an EMPTY condition sequence, which
`AbstractConnectorBase.AddWhere` SHALL render as no `WHERE` clause at all.

#### Scenario: A type test disappears from the query

- **Given** the predicate `x => x.Payload is string` (a `TypeBinaryExpression`)
- **When** `ParseConditionExpression` matches none of its lambda/unary/binary/method-call/member branches and `parent` is `null`
- **Then** `Array.Empty<Condition>()` is returned, `ConditionDefinition` yields an empty string, and the emitted SQL has no `WHERE` — every row is selected

#### Scenario: ElasticSearch refuses the same predicate

- **Given** the same `TypeBinaryExpression` predicate handed to `ElasticSearch.ParseFilterQuery`
- **When** `ParseExpression` falls through its switch and returns `null`
- **Then** a `NotSupportedException` is thrown — the two backends diverge, SQL widening where ElasticSearch fails

### Requirement: Dropped ElasticSearch sub-clauses

`CombineBool` SHALL include only the non-null translations of its two operands, SHALL return `null`
only when BOTH operands translated to `null`, and SHALL therefore produce a `BoolQuery` containing a
single clause when exactly one operand was untranslatable.

#### Scenario: One untranslatable operand of an AND is dropped

- **Given** `x => (x.Payload is string) && x.Status == active`
- **When** `ParsePredicate` returns `null` for the left operand and a `TermQuery` for the right
- **Then** the result is a `BoolQuery { Must = [TermQuery status] }` — the left clause is gone, and because the top-level result is non-null the `ParseFilterQuery` guard does not fire

#### Scenario: Both operands untranslatable

- **Given** an AND whose two operands both translate to `null`
- **When** `CombineBool` finds no queries
- **Then** `null` is returned and the top-level guard throws
