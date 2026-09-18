---
area: specifications-and-paging
generated-at: 804f0b7619d3e502e6a0a9d33119a3e62097c562
generated-on: 2026-08-07
sources:
  - ../Birko.Data.Patterns/Paging/AsyncPagedRepositoryWrapper.cs
  - ../Birko.Data.Patterns/Paging/IPagedRepository.cs
  - ../Birko.Data.Patterns/Paging/PagedRepositoryWrapper.cs
  - ../Birko.Data.Patterns/Paging/PagedResult.cs
  - ../Birko.Data.Patterns/Specification/AndSpecification.cs
  - ../Birko.Data.Patterns/Specification/ISpecification.cs
  - ../Birko.Data.Patterns/Specification/NotSpecification.cs
  - ../Birko.Data.Patterns/Specification/OrSpecification.cs
  - ../Birko.Data.Patterns/Specification/RuleSpecification.cs
  - ../Birko.Data.Patterns/Specification/Specification.cs
source-commits:   # sibling HEADs when this spec was last written (2026-08-07 13:16:35,
                  # commit 55e37bd). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Data.Patterns: 87c0ed3
shaped-by: [FEATURE-014]
# false, and NOT because nobody tried: the evidence pass cannot run from this aggregator — every source
# glob points into a sibling repo, so no task's `pr:` sha resolves under `git show` here. FEATURE-014 comes
# from the regenerating task's own `feature:` field, not from evidence.
shaped-by-derived: true
shaped-by-unresolved: 80
---

# Specification composition and paged results

## Purpose

This capability gives application code two reusable building blocks for querying a Birko store or
repository without hand-writing LINQ at every call site.

The **Specification** half wraps a business rule as an object that can answer two questions about the
same rule: "does this one in-memory entity satisfy it?" (`IsSatisfiedBy`) and "what LINQ expression
should I hand to a store's `Read`/`Count`/bulk-`Update`/bulk-`Delete`?" (`ToExpression`). Specifications
compose with `And` / `Or` / `Not` (and the `&` / `|` / `!` operators), and `RuleSpecification<T>` adapts a
data-driven `Birko.Rules` rule tree (the shape a UI rule builder or a stored filter produces) into the
same interface. Because the two answers are produced by two different mechanisms — a LINQ expression tree
for the store, the `Birko.Rules` evaluator for memory — the places where those two mechanisms disagree are
part of this specification and are called out explicitly.

The **Paging** half is a thin adapter: given a bulk repository that can `Read(filter, orderBy, limit,
offset)` and `Count(filter)`, the two wrappers turn a 1-based page number into that limit/offset pair and
return a `PagedResult<T>` carrying the items plus total-count metadata (`TotalPages`, `HasNextPage`,
`HasPreviousPage`) that a UI grid or an API response envelope can render directly.

Both halves live in `Birko.Data.Patterns` and are consumed by application code; nothing else inside the
framework currently references `ISpecification<T>` or `IPagedRepository<T>`.

## Requirements

### Requirement: Specification base contract

The system SHALL expose every specification through `ISpecification<T>`, which declares exactly two
members — `bool IsSatisfiedBy(T entity)` for in-memory evaluation and `Expression<Func<T, bool>>
ToExpression()` for store-level filtering — and SHALL provide the abstract base class `Specification<T>`
in which `ToExpression()` is abstract and `IsSatisfiedBy` is `virtual`.

#### Scenario: Subclass supplies only the expression

- **Given** a class `ActiveUsers : Specification<User>` overriding only `ToExpression()` to return `u => u.IsActive`
- **When** the caller invokes `IsSatisfiedBy(user)` on an instance
- **Then** the base implementation compiles `ToExpression()` and returns the compiled delegate's result — no separate in-memory implementation is required

#### Scenario: Interface exposes no composition members

- **Given** a variable typed as `ISpecification<T>`
- **When** the caller looks for `And` / `Or` / `Not`
- **Then** they are not available, because those members are declared on `Specification<T>` (the base class) and not on `ISpecification<T>`

### Requirement: In-memory evaluation compiles the expression once and caches it

The system SHALL, in `Specification<T>.IsSatisfiedBy`, lazily compile `ToExpression()` into a
`Func<T, bool>` on first call, store it in the private `_compiledExpression` field, and reuse that
delegate for every subsequent call on the same instance. The caching SHALL be performed with a plain
`??=` assignment and no synchronisation.

#### Scenario: Repeated evaluation reuses the compiled delegate

- **Given** a `Specification<T>` instance whose `ToExpression()` increments a counter each time it is called
- **When** `IsSatisfiedBy` is invoked three times on that instance
- **Then** `ToExpression()` has been called exactly once

#### Scenario: Concurrent first evaluation may compile more than once

- **Given** a freshly constructed `Specification<T>` with no compiled delegate yet
- **When** two threads call `IsSatisfiedBy` simultaneously
- **Then** both may observe `_compiledExpression` as null and compile independently, and the last assignment wins; the field is non-volatile and the `??=` write is neither atomic nor release-fenced, so the compiled delegate is published without any barrier

#### Scenario: A new instance does not inherit the cache

- **Given** two separate instances of the same specification class
- **When** `IsSatisfiedBy` is called on each
- **Then** each compiles its own delegate — the cache is per instance, not per type

### Requirement: Boolean composition of specifications

The system SHALL provide `Specification<T>.And(other)`, `.Or(other)` and `.Not()` returning
`ISpecification<T>` instances of `AndSpecification<T>`, `OrSpecification<T>` and `NotSpecification<T>`
respectively, and SHALL provide the operator overloads `&`, `|` and `!` on `Specification<T>` that
delegate to those three methods.

#### Scenario: Operator form equals method form

- **Given** two `Specification<Order>` instances `a` and `b`
- **When** the caller writes `a & b`
- **Then** the result is an `AndSpecification<Order>` identical in behaviour to `a.And(b)`

#### Scenario: Composites are themselves composable

- **Given** `var combined = (a & b) as Specification<Order>` is not possible because `&` returns `ISpecification<Order>`
- **When** the caller wants to compose a third specification onto the result
- **Then** they must hold the concrete composite type (`AndSpecification<T>` derives from `Specification<T>`) or re-wrap manually, because `And`/`Or`/`Not` and the operators are declared on `Specification<T>` while the operators' return type is `ISpecification<T>`

#### Scenario: Negation of a composite

- **Given** `var spec = new AndSpecification<Order>(a, b)`
- **When** `spec.Not()` is called
- **Then** a `NotSpecification<Order>` wrapping the AND composite is returned, and its `ToExpression()` negates the whole conjunction rather than either operand

### Requirement: AND/OR composition unifies lambda parameters instead of invoking

The system SHALL build the combined expression for `AndSpecification<T>` and `OrSpecification<T>` by
delegating to `ExpressionParameterReplacer.AndAlso` / `.OrElse`, which rewrite the right-hand lambda's
parameter to the left-hand lambda's parameter and emit a single `Expression.AndAlso` / `Expression.OrElse`
body — so the produced tree contains no `InvocationExpression` node.

#### Scenario: Combined expression has one parameter

- **Given** `left` returning `x => x.A > 1` and `right` returning `y => y.B < 2`
- **When** `new AndSpecification<T>(left, right).ToExpression()` is evaluated
- **Then** the result is a lambda with exactly one parameter whose body is `AndAlso(x.A > 1, x.B < 2)`, with `y` fully replaced by `x`

#### Scenario: Result is translatable by the SQL parser

- **Given** an `OrSpecification<T>` over two store-translatable operands
- **When** its expression is passed to a store's `Read(filter)`
- **Then** the store's condition parser sees only AND/OR/comparison nodes, because no `Expression.Invoke` wrapper was introduced

### Requirement: Negation reuses the inner lambda's parameter list

The system SHALL implement `NotSpecification<T>.ToExpression()` as `Expression.Not(innerExpr.Body)`
re-wrapped in a lambda over `innerExpr.Parameters` — i.e. the inner expression's own parameter instances
are reused verbatim, with no parameter rewriting.

#### Scenario: Single negation

- **Given** an inner specification whose expression is `x => x.IsDeleted`
- **When** `new NotSpecification<T>(inner).ToExpression()` is evaluated
- **Then** the result is `x => !x.IsDeleted` bound to the same `x` parameter instance as the inner lambda

#### Scenario: Double negation

- **Given** `new NotSpecification<T>(new NotSpecification<T>(inner))`
- **When** `ToExpression()` is evaluated
- **Then** the body is `Not(Not(inner.Body))` — the two negations are not collapsed

### Requirement: Composition operands are rejected when null

The system SHALL throw `ArgumentNullException` from the constructors of `AndSpecification<T>`
(parameters `left`, `right`), `OrSpecification<T>` (parameters `left`, `right`) and
`NotSpecification<T>` (parameter `inner`) when the corresponding argument is null.

#### Scenario: Null right operand of AND

- **Given** a non-null `left` specification
- **When** `new AndSpecification<T>(left, null)` is constructed
- **Then** `ArgumentNullException` with parameter name `right` is thrown at construction time, not at `ToExpression()` time

#### Scenario: Null inner of NOT

- **Given** no inner specification
- **When** `new NotSpecification<T>(null)` is constructed
- **Then** `ArgumentNullException` with parameter name `inner` is thrown

#### Scenario: RuleSpecification does not validate its rule

- **Given** `new RuleSpecification<T>((IRule)null)`
- **When** the constructor runs
- **Then** it completes successfully (the `_rule` field is assigned without a null check) and the failure is deferred to the first `ToExpression()` / `IsSatisfiedBy` call, which dereferences `rule.IsEnabled`

### Requirement: RuleSpecification evaluates in memory through the rule evaluator

The system SHALL have `RuleSpecification<T>` `override` (not shadow) `IsSatisfiedBy`, evaluating the rule
against an `ObjectRuleContext<T>` wrapper of the entity via the injected `IRuleEvaluator` and returning
`result.IsMatch`, so that in-memory evaluation never goes through the compiled LINQ expression. When no
evaluator is supplied the system SHALL default to a new `RuleEvaluator()`.

#### Scenario: Evaluation via a composed specification still reaches the rule evaluator

- **Given** a `RuleSpecification<Order>` combined into `new AndSpecification<Order>(ruleSpec, other)`
- **When** the composite's `IsSatisfiedBy` runs (which compiles the composite expression) versus when `ruleSpec.IsSatisfiedBy` is called directly through an `ISpecification<Order>` reference
- **Then** the direct call dispatches to the `RuleSpecification` override and uses `IRuleEvaluator`, because `IsSatisfiedBy` is an override rather than a `new` member

#### Scenario: Custom evaluator is honoured

- **Given** a stub `IRuleEvaluator` that always returns a matching `RuleResult`
- **When** `new RuleSpecification<Order>(rule, stub).IsSatisfiedBy(anyOrder)` is called
- **Then** the result is true regardless of the entity's field values

#### Scenario: Default evaluator is constructed per specification

- **Given** `new RuleSpecification<Order>(rule)` with the `evaluator` argument omitted
- **When** the constructor runs
- **Then** a new `RuleEvaluator` instance is created and stored for that specification

### Requirement: Rule field resolution is case-insensitive and unmatched fields are unsatisfiable

The system SHALL resolve a leaf rule's `Field` to a public instance property of `T` using
`BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase`, and SHALL return
`Expression.Constant(false)` for that leaf when no such property exists.

#### Scenario: Field name differs in casing

- **Given** `T` has a property `CustomerName` and the rule's `Field` is `"customername"`
- **When** `ToExpression()` is built
- **Then** the property is found and the comparison is emitted against `x.CustomerName`

#### Scenario: Field does not exist on the entity

- **Given** a rule with `Field = "NoSuchColumn"`
- **When** `ToExpression()` is built
- **Then** the leaf becomes `Expression.Constant(false)` — the specification matches nothing for that leaf instead of throwing

#### Scenario: Non-public or static member

- **Given** a rule whose `Field` names a private field or a static property
- **When** `ToExpression()` is built
- **Then** the lookup fails (the binding flags exclude non-public and static members) and the leaf becomes `Expression.Constant(false)`

### Requirement: Leaf operator translation table

The system SHALL translate a leaf rule's `ComparisonOperator` to an expression as follows: `IsNull` /
`IsNotNull` to a typed null equality/inequality; `Equal`, `NotEqual`, `GreaterThan`,
`GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual` to the corresponding `Expression` binary comparison;
`Between` to `AndAlso(member >= lower, member <= upper)`; `Contains`, `StartsWith`, `EndsWith` to the
corresponding guarded `string` method call; `NotContains` to the negation of the guarded `Contains` call;
`Like` to an anchored `StartsWith` / `EndsWith` / `Contains` / equality according to its `%` placement;
`In` to a chain of `OrElse` equalities; and `NotIn` to the negation of that chain.

An operator value with **no arm** SHALL throw `NotSupportedException` naming the operator and the field.
It SHALL NOT fall back to a constant: a fallback constant is indistinguishable from a real predicate, and
the previous `Expression.Constant(true)` fallback silently widened `In`, `NotIn` and `Like` — all three
declared operators — into match-everything on a filter that also drives bulk `Update` and `Delete`.

#### Scenario: Range rule

- **Given** a leaf rule with `Operator = Between`, `Value = 10`, `UpperValue = 20` on an `int` property `Qty`
- **When** `ToExpression()` is built
- **Then** the body is `x.Qty >= 10 && x.Qty <= 20`

#### Scenario: Collection membership filters instead of matching everything

- **Given** a leaf rule with `Operator = In` and `Value = new[] { 1, 2, 3 }` on an `int` property
- **When** `ToExpression()` is built
- **Then** the leaf is `x.Qty == 1 || x.Qty == 2 || x.Qty == 3`, and `IsSatisfiedBy` agrees with it for
  every entity

#### Scenario: Membership over an empty set is match-none, and its negation is match-all

- **Given** a leaf rule with `Operator = In` and an empty `Value` collection
- **When** `ToExpression()` is built
- **Then** the leaf is `Constant(false)` — and, unlike a degraded leaf, it is a **real predicate**, so
  `NotIn` over the same empty set correctly matches every row. "In the empty set" is false for every row
  and its negation is true for every row; this is the only all-rows outcome the translator produces on
  purpose

#### Scenario: SQL-style wildcard translates by its anchors

- **Given** a leaf rule with `Operator = Like` on a `string` property
- **When** the pattern is `"abc"`, `"abc%"`, `"%abc"` or `"%abc%"`
- **Then** the leaf is an equality, `StartsWith`, `EndsWith` or `Contains` respectively

#### Scenario: A wildcard with an interior `%` is unevaluable rather than approximated

- **Given** a leaf rule with `Operator = Like` and `Value = "a%b"`
- **When** `ToExpression()` is built
- **Then** the leaf is an unevaluable match-none, because no single string method expresses an interior
  wildcard — and it stays match-none under negation rather than matching every row

#### Scenario: An operator with no arm throws

- **Given** a leaf rule whose `Operator` is a value the translator has no arm for
- **When** `ToExpression()` is built
- **Then** `NotSupportedException` is thrown naming the operator and the field, so a newly added
  `ComparisonOperator` cannot silently widen a filter the way `In` / `NotIn` / `Like` did

### Requirement: Negation is applied only to a leaf the translator could actually evaluate

The system SHALL distinguish a leaf that expresses a **real predicate** from one that **could not be
evaluated**, and SHALL apply `Expression.Not` only to the former. A leaf that could not be evaluated
matches nothing, and SHALL continue to match nothing under negation.

This is the single invariant behind four separate findings. `Expression.Not` over a match-none constant is
match-**all**, so every site that degraded to `Constant(false)` — a non-string member handed a string
operator, a value that will not convert to the member's type, unconvertible `Between` bounds — became a
whole-table match as soon as the rule was negated, or, for `NotContains`, without any negation flag at all.
The distinction SHALL be carried on the leaf itself rather than re-derived per site, so that a degradation
site added later inherits the invariant instead of having to remember it.

An unresolved `Field` SHALL likewise be an unevaluable match-none. (It was already safe, but only because
its early return happened to precede the negation step; it is now safe by the same rule as every other
degradation.)

#### Scenario: Negated equality

- **Given** a leaf rule `Status Equal "Open"` with `IsNegated = true`
- **When** `ToExpression()` is built
- **Then** the body is `!(x.Status == "Open")`

#### Scenario: Negated unresolvable field matches nothing

- **Given** a leaf rule on a non-existent field with `IsNegated = true`
- **When** `ToExpression()` is built
- **Then** the leaf matches nothing — the negation is not applied to an unevaluable leaf

#### Scenario: A string operator on a non-string member never widens

- **Given** a leaf rule `Quantity Contains "1"` on an `int` property, with or without `IsNegated`, and the
  equivalent `Quantity NotContains "1"`
- **When** `ToExpression()` is built
- **Then** every one of those leaves matches nothing — previously `NotContains` produced `Not(false)` and
  matched every row with no negation flag set at all

#### Scenario: A negated comparison with a non-convertible value never widens

- **Given** a leaf rule `Quantity Equal "not-a-number"` with `IsNegated = true`, and a `Between` rule with
  unconvertible bounds and `IsNegated = true`
- **When** `ToExpression()` is built
- **Then** both match nothing rather than every row

### Requirement: Null-check translation builds a typed null constant with no nullability guard

The system SHALL translate `IsNull` / `IsNotNull` by constructing `Expression.Constant(null,
member.Type)` and comparing the member against it with `Expression.Equal` / `Expression.NotEqual`, with
no check that `member.Type` can hold null.

#### Scenario: Null check on a reference-typed property

- **Given** a rule `Description IsNull` on a `string` property
- **When** `ToExpression()` is built
- **Then** the body is `x.Description == (string)null`

#### Scenario: Null check on a nullable value type

- **Given** a rule `ClosedAt IsNotNull` on a `DateTime?` property
- **When** `ToExpression()` is built
- **Then** the body is `x.ClosedAt != (DateTime?)null`

#### Scenario: Null check on a non-nullable value type throws

- **Given** a rule `Qty IsNull` on a non-nullable `int` property
- **When** `ToExpression()` is built
- **Then** `Expression.Constant(null, typeof(int))` throws `ArgumentException` ("Argument types do not match") out of `ToExpression()` — unlike the comparison operators, the null-check path has no graceful degradation

### Requirement: Non-convertible rule values make a comparison leaf unsatisfiable

The system SHALL convert a leaf rule's `Value` (and `UpperValue` for `Between`) to the member's type via
`TryConvertConstant`, which: returns false when the value is null and the target is a non-nullable value
type; passes the value through unchanged when `targetType.IsInstanceOfType(value)` (covering enums and
exact type matches); otherwise calls `Convert.ChangeType` against the target's non-nullable underlying
type; and returns false when the conversion raises `InvalidCastException`, `FormatException`,
`OverflowException` or `ArgumentException`. When conversion fails the system SHALL emit an **unevaluable**
match-none leaf for that leaf rather than throwing — unevaluable rather than merely false, so that a
negated comparison with a non-convertible value cannot invert into match-all.

#### Scenario: Numeric string is converted

- **Given** a rule `Qty GreaterThan "10"` (value is the string `"10"`) on an `int` property
- **When** `ToExpression()` is built
- **Then** `Convert.ChangeType` yields `10` and the body is `x.Qty > 10`

#### Scenario: Null against a non-nullable member

- **Given** a rule `Qty Equal null` on a non-nullable `int` property
- **When** `ToExpression()` is built
- **Then** the leaf becomes `Expression.Constant(false)`

#### Scenario: Unparseable value

- **Given** a rule `Qty Equal "abc"` on an `int` property
- **When** `ToExpression()` is built
- **Then** `Convert.ChangeType` throws `FormatException`, the filter catches it and the leaf becomes `Expression.Constant(false)`

#### Scenario: Enum member with an enum value

- **Given** a rule `Status Equal OrderStatus.Open` on an `OrderStatus` property
- **When** `ToExpression()` is built
- **Then** the `IsInstanceOfType` branch passes the enum through unchanged (bypassing `Convert.ChangeType`, which cannot handle it) and the comparison is emitted

#### Scenario: Between with one bad bound

- **Given** a rule `Between` with a valid lower bound and a non-convertible upper bound
- **When** `ToExpression()` is built
- **Then** the whole `Between` leaf becomes `Expression.Constant(false)` — a partially valid range is not emitted

#### Scenario: An unlisted conversion exception propagates

- **Given** a value whose conversion raises an exception outside the four filtered types (for example an `IConvertible` implementation throwing `NotSupportedException`)
- **When** `ToExpression()` is built
- **Then** that exception escapes `TryConvertConstant` and `ToExpression()`, because the `catch` clause filters on exactly `InvalidCastException`, `FormatException`, `OverflowException` and `ArgumentException`

### Requirement: String-method translation is ordinal-case-insensitive and null-guarded

The system SHALL translate `Contains` / `StartsWith` / `EndsWith` to a call of the
`string(string, StringComparison)` overload with `StringComparison.OrdinalIgnoreCase`, converting the
rule value with `value?.ToString() ?? string.Empty`, and SHALL wrap the call as
`AndAlso(member != null, call)`. When the resolved member is not of type `string` the system SHALL emit an
**unevaluable** match-none leaf instead — unevaluable rather than merely false, so neither `NotContains`
nor an `IsNegated` flag can invert it into match-all. The null guard, by contrast, produces a **real**
predicate: a null field genuinely does not match, so negating it is meaningful and `NotContains` over a
null string is correctly true. The `StringComparison.OrdinalIgnoreCase` argument governs only
in-memory evaluation of the compiled delegate: when the same expression reaches a SQL store, that store's
condition parser deliberately discards the `StringComparison` argument and builds the `LIKE` pattern from
the first argument alone, so case sensitivity there follows the column's database collation.

#### Scenario: Substring match on a string property

- **Given** a rule `Name Contains "acme"` on a `string` property
- **When** `ToExpression()` is built and compiled
- **Then** an entity with `Name = "ACME Ltd"` satisfies it (case-insensitive ordinal comparison)

#### Scenario: Null string property does not throw

- **Given** a rule `Name StartsWith "a"` and an entity whose `Name` is null
- **When** the compiled delegate runs in memory
- **Then** the `member != null` guard short-circuits and the result is false — no `NullReferenceException`

#### Scenario: String operator on a non-string member

- **Given** a rule `Qty Contains "1"` on an `int` property
- **When** `ToExpression()` is built, and separately `IsSatisfiedBy` is called for the same rule
- **Then** both report no match, in both polarities. The in-memory engine previously answered **true** here
  by stringifying the member (`21.ToString().Contains("1")`); the two engines were reconciled onto
  match-none because the expression path cannot follow — no portable translation of
  `column.ToString().Contains(…)` exists across the SQL providers and ElasticSearch

#### Scenario: Null rule value becomes an empty needle

- **Given** a rule `Name Contains null`
- **When** `ToExpression()` is built and compiled
- **Then** the needle is `string.Empty`, so every entity with a non-null `Name` satisfies the leaf

#### Scenario: The same leaf is collation-dependent at a SQL store

- **Given** a rule `Name Contains "acme"` whose expression is passed to a `Birko.Data.SQL` store's `Read(filter)`
- **When** the store's condition parser translates the `Contains(string, StringComparison)` call
- **Then** only the first argument becomes the `LIKE` pattern and the `StringComparison.OrdinalIgnoreCase` argument is skipped as a non-operand argument, so whether `Name = "ACME Ltd"` is returned depends on the column's collation rather than on the ordinal-ignore-case semantics the expression requested

### Requirement: Rule-group translation drops disabled children and treats an empty group as unsatisfiable

The system SHALL translate a `RuleGroup` by first filtering its children to those with `IsEnabled`, and:
when no enabled child remains, emit `Expression.Constant(false)`; otherwise fold the children
left-to-right with `Expression.AndAlso` when `group.Logic == LogicOperator.And` and `Expression.OrElse`
otherwise. The group's own `IsNegated` flag SHALL NOT be applied.

#### Scenario: Mixed enabled and disabled children in an AND group

- **Given** an AND group with children `[enabled: A, disabled: B, enabled: C]`
- **When** `ToExpression()` is built
- **Then** the body is `A && C` — the disabled child contributes nothing rather than forcing a false

#### Scenario: Group whose children are all disabled

- **Given** an OR group with two children, both `IsEnabled = false`
- **When** `ToExpression()` is built
- **Then** the group becomes `Expression.Constant(false)` and the specification matches nothing

#### Scenario: Empty group

- **Given** a `RuleGroup(LogicOperator.And, new List<IRule>())`
- **When** `ToExpression()` is built
- **Then** the result is `Expression.Constant(false)`, matching the in-memory evaluator's `NoMatch` for an empty group

#### Scenario: Negated group is silently un-negated

- **Given** a `RuleGroup` with `IsNegated = true` containing one enabled child `A`
- **When** `ToExpression()` is built
- **Then** the body is `A` with no surrounding `Expression.Not` — group-level negation is ignored by the translator

#### Scenario: Nested groups

- **Given** an OR group whose second child is itself an AND group
- **When** `ToExpression()` is built
- **Then** the nested group is translated recursively through `BuildExpression` and folded into the outer `OrElse` chain

### Requirement: A disabled root rule matches nothing, agreeing with in-memory evaluation

The system SHALL return `Expression.Constant(false)` from `BuildExpression` when the rule being translated
has `IsEnabled == false`, matching `RuleEvaluator.Evaluate`, which short-circuits a disabled rule to
`NoMatch`. It SHALL also return `Expression.Constant(false)` when the rule is neither a `Rules.Rule` nor a
`RuleGroup`.

This applies only to a **root** rule: `BuildGroupExpression` filters disabled children before recursing, so
a disabled rule inside a group is dropped from the composition and never reaches this branch.

#### Scenario: Disabled root rule

- **Given** `new RuleSpecification<Order>(rule)` where `rule.IsEnabled == false`
- **When** `ToExpression()` is built and, separately, `IsSatisfiedBy(order)` is called
- **Then** both report no match for every entity — previously `ToExpression()` yielded `x => true`, so a
  disabled rule handed to `Delete(filter)` emptied the table while `IsSatisfiedBy` returned false for every
  entity

#### Scenario: A disabled root rule is not an instruction to delete everything

- **Given** a populated store and a specification whose root rule is disabled
- **When** `Delete(spec.ToExpression())` is called
- **Then** no rows are removed. This matters beyond the filter: `x => true` is precisely the shape the
  destructive-write guard recognises as a *deliberate* whole-table request, so the disabled rule was routed
  through the explicit all-rows door rather than being refused by it

#### Scenario: Unknown IRule implementation

- **Given** a custom `IRule` implementation that is neither `Rule` nor `RuleGroup`, with `IsEnabled == true`
- **When** `ToExpression()` is built
- **Then** the switch's `_` arm yields `Expression.Constant(false)`

### Requirement: RuleSet adaptation

The system SHALL accept a `RuleSet` in the `RuleSpecification<T>` constructor and wrap it via
`WrapRuleSet`: when `ruleSet.IsEnabled` is false it SHALL produce an **empty** `RuleGroup(LogicOperator
.And, new List<IRule>())` (leaving that group's own `IsEnabled` at its default `true`); otherwise it
SHALL produce `RuleGroup(LogicOperator.And, ruleSet.Rules)` carrying `IsEnabled = ruleSet.IsEnabled`.

#### Scenario: Enabled rule set becomes a conjunction

- **Given** an enabled `RuleSet` with rules `[A, B]`
- **When** `new RuleSpecification<Order>(ruleSet).ToExpression()` is built
- **Then** the body is `A && B`

#### Scenario: Disabled rule set matches nothing

- **Given** a `RuleSet` with `IsEnabled = false` and two rules
- **When** `ToExpression()` is built
- **Then** the empty wrapper group has no enabled children and yields `Expression.Constant(false)` — the rules are discarded and the specification matches nothing (the opposite outcome to a disabled *root rule*, which yields match-all)

### Requirement: Rule expression uses a single parameter named "x"

The system SHALL create one `ParameterExpression` of type `T` named `"x"` in
`RuleSpecification<T>.ToExpression()` and use that same parameter for every property access in the whole
rule tree, returning `Expression.Lambda<Func<T, bool>>(body, param)`.

#### Scenario: Multi-leaf rule tree shares one parameter

- **Given** an AND group with three leaf rules on three different properties
- **When** `ToExpression()` is built
- **Then** the lambda has exactly one parameter and all three member accesses bind to it, making the tree directly translatable by store condition parsers

### Requirement: Paged repository contracts

The system SHALL declare two separate paging interfaces — `IPagedRepository<T>` with
`PagedResult<T> ReadPaged(filter = null, orderBy = null, page = 1, pageSize = 20)` and
`IAsyncPagedRepository<T>` with `Task<PagedResult<T>> ReadPagedAsync(filter = null, orderBy = null,
page = 1, pageSize = 20, CancellationToken ct = default)` — both constrained to
`T : Data.Models.AbstractModel`.

#### Scenario: Default page window

- **Given** a caller invoking `ReadPaged()` with no arguments
- **When** the call executes
- **Then** it reads page 1 with a page size of 20 and no filter or ordering

#### Scenario: Sync and async are not related by inheritance

- **Given** a type implementing `IPagedRepository<T>`
- **When** a caller requires `IAsyncPagedRepository<T>`
- **Then** the cast fails unless the type implements both — the two interfaces are independent

### Requirement: Paging wrappers require a repository

The system SHALL throw `ArgumentNullException` (parameter name `repository`) from the
`PagedRepositoryWrapper<T>` and `AsyncPagedRepositoryWrapper<T>` constructors when the wrapped repository
is null, and SHALL otherwise store it for the lifetime of the wrapper.

#### Scenario: Null repository

- **Given** no repository instance
- **When** `new PagedRepositoryWrapper<Order>(null)` is constructed
- **Then** `ArgumentNullException` with parameter name `repository` is thrown

### Requirement: Page and page-size arguments are clamped, never rejected

The system SHALL clamp `page` to a minimum of 1 and `pageSize` to a minimum of 1 in both
`PagedRepositoryWrapper<T>.ReadPaged` and `AsyncPagedRepositoryWrapper<T>.ReadPagedAsync`, silently, with
no exception, and SHALL report the **clamped** values in the returned `PagedResult<T>`.

#### Scenario: Zero page number

- **Given** `page = 0` and `pageSize = 10`
- **When** `ReadPaged` executes
- **Then** page is treated as 1, offset is 0, and the returned `PagedResult.Page` is 1

#### Scenario: Negative page size

- **Given** `page = 3` and `pageSize = -5`
- **When** `ReadPaged` executes
- **Then** page size is treated as 1, the underlying read is `limit: 1, offset: 2`, and `PagedResult.PageSize` is 1

#### Scenario: Page beyond the end of the data

- **Given** 5 total matching rows, `page = 10`, `pageSize = 20`
- **When** `ReadPaged` executes
- **Then** the underlying read uses `offset: 180`, `Items` is empty, `TotalCount` is 5, `TotalPages` is 1 and `HasNextPage` is false while `HasPreviousPage` is true

### Requirement: Offset is derived from the clamped page and page size

The system SHALL compute the read offset as `(page - 1) * pageSize` after clamping, and pass `pageSize`
as the `limit` and that offset as the `offset` to the wrapped repository's
`Read(filter, orderBy, limit, offset)` / `ReadAsync(filter, orderBy, limit, offset, ct)`. The
multiplication SHALL be performed in unchecked `int` arithmetic.

#### Scenario: Third page of twenty

- **Given** `page = 3`, `pageSize = 20`
- **When** `ReadPaged` executes
- **Then** the repository is called with `limit: 20, offset: 40`

#### Scenario: Very large page number overflows

- **Given** `page = int.MaxValue` and `pageSize = 20`
- **When** `ReadPaged` executes
- **Then** `(page - 1) * pageSize` wraps around in unchecked `int` arithmetic and a negative offset is handed to the repository — the wrapper performs no overflow check

### Requirement: Items are read first, then counted, sequentially and non-atomically

The system SHALL, in both wrappers, materialise the page with `.ToList()` from the repository read and
only afterwards call `Count(filter)` / `CountAsync(filter, ct)` with the same filter, deliberately
awaiting sequentially rather than concurrently so that a non-thread-safe underlying store never sees two
in-flight calls on one instance.

#### Scenario: Order of underlying calls

- **Given** a recording fake `IAsyncBulkRepository<Order>`
- **When** `ReadPagedAsync` executes
- **Then** `ReadAsync` is observed to complete before `CountAsync` is started

#### Scenario: Concurrent insert between the two calls

- **Given** page 1 of 20 is read and another writer inserts a matching row before the count runs
- **When** `ReadPagedAsync` returns
- **Then** `TotalCount` includes the new row while `Items` does not — the page and the total are not a consistent snapshot and no transaction spans them

#### Scenario: Count uses the same filter as the read

- **Given** a filter `o => o.Status == "Open"`
- **When** `ReadPaged` executes
- **Then** the same filter expression instance is passed to both `Read` and `Count`, so `TotalCount` is the filtered total and not the table total

#### Scenario: Items are materialised before the count query runs

- **Given** a repository returning a lazily-enumerated `IEnumerable<Order>`
- **When** `ReadPagedAsync` executes
- **Then** `.ToList()` fully drains that enumerable before `CountAsync` is issued, so a forward-only reader is not left open across the second call

### Requirement: Cancellation is propagated to both async calls

The system SHALL pass the caller's `CancellationToken` to both `ReadAsync` and `CountAsync` in
`AsyncPagedRepositoryWrapper<T>.ReadPagedAsync`.

#### Scenario: Token cancelled before the count

- **Given** a token that is cancelled while `ReadAsync` is in flight
- **When** `ReadPagedAsync` continues to `CountAsync`
- **Then** the same token is supplied to `CountAsync`, so the underlying store can observe the cancellation (the wrapper itself performs no `ThrowIfCancellationRequested`)

### Requirement: PagedResult carries the page and derives navigation metadata

The system SHALL expose on `PagedResult<T>` the read-only properties `Items` (`IReadOnlyList<T>`),
`TotalCount` (`long`), `Page` (`int`) and `PageSize` (`int`) exactly as supplied to the constructor, and
SHALL compute `TotalPages` as `PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0`,
`HasNextPage` as `Page < TotalPages` and `HasPreviousPage` as `Page > 1`. The type SHALL be `sealed` and
SHALL perform no validation or defensive copying of its arguments.

#### Scenario: Partial last page

- **Given** `new PagedResult<Order>(items, totalCount: 45, page: 3, pageSize: 20)`
- **When** the metadata is read
- **Then** `TotalPages` is 3 (ceiling of 2.25), `HasNextPage` is false and `HasPreviousPage` is true

#### Scenario: No matching rows

- **Given** `new PagedResult<Order>([], totalCount: 0, page: 1, pageSize: 20)`
- **When** the metadata is read
- **Then** `TotalPages` is 0, `HasNextPage` is false and `HasPreviousPage` is false

#### Scenario: Zero page size constructed directly

- **Given** `new PagedResult<Order>(items, totalCount: 100, page: 1, pageSize: 0)` (reachable only by direct construction, since the wrappers clamp)
- **When** the metadata is read
- **Then** `TotalPages` is 0 (the guard prevents a divide-by-zero) and `HasNextPage` is false, even though 100 items exist

#### Scenario: Null items are accepted

- **Given** `new PagedResult<Order>(null, 0, 1, 20)`
- **When** the constructor runs
- **Then** it succeeds and `Items` is null — the constructor has no null check, so the failure surfaces only when a consumer enumerates `Items`

#### Scenario: Caller retains a reference to the item list

- **Given** a mutable `List<Order>` passed as `items`
- **When** the caller mutates that list after construction
- **Then** `PagedResult.Items` reflects the mutation, because the list is stored by reference rather than copied

### Requirement: Empty paged result factory

The system SHALL provide the static factory `PagedResult<T>.Empty(page = 1, pageSize = 20)` returning an
instance with an empty item collection and `TotalCount` of 0, using the supplied page and page size
verbatim without clamping.

#### Scenario: Default empty result

- **Given** a caller invoking `PagedResult<Order>.Empty()`
- **When** the metadata is read
- **Then** `Items` is empty, `TotalCount` is 0, `Page` is 1, `PageSize` is 20, `TotalPages` is 0 and both navigation flags are false

#### Scenario: Empty result for an out-of-range request

- **Given** `PagedResult<Order>.Empty(page: 0, pageSize: 0)`
- **When** the metadata is read
- **Then** `Page` is 0 and `PageSize` is 0 as supplied — `Empty` applies none of the wrappers' clamping — and `TotalPages` is 0
