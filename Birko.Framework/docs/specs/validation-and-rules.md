---
area: validation-and-rules
generated-at: 804f0b7619d3e502e6a0a9d33119a3e62097c562
generated-on: 2026-08-07
sources:
  - ../Birko.Rules/Context/DictionaryRuleContext.cs
  - ../Birko.Rules/Context/IRuleContext.cs
  - ../Birko.Rules/Context/ObjectRuleContext.cs
  - ../Birko.Rules/Core/ComparisonOperator.cs
  - ../Birko.Rules/Core/IRule.cs
  - ../Birko.Rules/Core/LogicOperator.cs
  - ../Birko.Rules/Core/Rule.cs
  - ../Birko.Rules/Core/RuleGroup.cs
  - ../Birko.Rules/Core/RuleResult.cs
  - ../Birko.Rules/Core/RuleSet.cs
  - ../Birko.Rules/Core/RuleSeverity.cs
  - ../Birko.Rules/Evaluation/ComparisonHelper.cs
  - ../Birko.Rules/Evaluation/IRuleEvaluator.cs
  - ../Birko.Rules/Evaluation/RuleEvaluator.cs
  - ../Birko.Rules/Expressions/RuleExpressionConverter.cs
  - ../Birko.Validation/Core/IValidationRule.cs
  - ../Birko.Validation/Core/IValidator.cs
  - ../Birko.Validation/Core/ValidationContext.cs
  - ../Birko.Validation/Core/ValidationException.cs
  - ../Birko.Validation/Core/ValidationResult.cs
  - ../Birko.Validation/Fluent/AbstractValidator.cs
  - ../Birko.Validation/Fluent/PropertyRule.cs
  - ../Birko.Validation/Fluent/RuleBuilder.cs
  - ../Birko.Validation/Integration/AsyncValidatingBulkStoreWrapper.cs
  - ../Birko.Validation/Integration/AsyncValidatingStoreWrapper.cs
  - ../Birko.Validation/Integration/RuleBasedValidator.cs
  - ../Birko.Validation/Integration/ValidatingStoreWrapper.cs
  - ../Birko.Validation/Rules/CustomRule.cs
  - ../Birko.Validation/Rules/EmailRule.cs
  - ../Birko.Validation/Rules/LengthRule.cs
  - ../Birko.Validation/Rules/RangeRule.cs
  - ../Birko.Validation/Rules/RegexRule.cs
  - ../Birko.Validation/Rules/RequiredRule.cs
source-commits:   # sibling HEADs when this spec was last written (2026-08-07 13:16:35,
                  # commit 55e37bd). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Rules: 839d712
  ../Birko.Validation: 0455677
shaped-by: [FEATURE-014]
# false, and NOT because nobody tried: the evidence pass cannot run from this aggregator — every source
# glob points into a sibling repo, so no task's `pr:` sha resolves under `git show` here. FEATURE-014 comes
# from the regenerating task's own `feature:` field, not from evidence.
shaped-by-derived: false
---

# Fluent validation, rule engine and validating store wrappers

## Purpose

This capability covers two cooperating libraries. `Birko.Rules` is a data-driven rule engine: a rule
is a serializable `field operator value` triple (or an AND/OR group of such triples) that can be
**evaluated in memory** against an arbitrary field source (`IRuleContext`) *or* **converted into a
LINQ `Expression<Func<T,bool>>`** so the same rule tree can be pushed down to any Birko store as a
query filter. `Birko.Validation` is a code-first fluent validator (`AbstractValidator<T>` with
`RuleFor(...)` chains and a small catalogue of built-in property rules) plus store decorators that
reject invalid entities before they reach persistence, and a bridge (`RuleBasedValidator<T>`) that
turns a `Birko.Rules` rule set into an `IValidator<T>`.

Consumers are: application code that hand-writes validators; systems that load rules from a database
or configuration so business users can change them without a deploy (IoT alarm thresholds, stock
rules); and any store composition that wants validation enforced at the store boundary rather than
at every call site.

## Requirements

### Requirement: Rule model shape and defaults

The system SHALL represent a rule as either a leaf `Rule` (a single `Field` / `Operator` / `Value`
condition, with `UpperValue` for `Between` and an `IsNegated` flag) or a composite `RuleGroup`
(a `LogicOperator` plus a `List<IRule>` of children, itself an `IRule`, nestable), both exposing the
`IRule` metadata `Name`, `Description`, `Severity` and `IsEnabled`, and SHALL default `Severity` to
`RuleSeverity.Info` and `IsEnabled` to `true`.

#### Scenario: Leaf rule constructed with the three-argument constructor

- **Given** `new Rule("Temperature", ComparisonOperator.GreaterThan, 80)`
- **When** the instance is inspected
- **Then** `Field` is `"Temperature"`, `Operator` is `GreaterThan`, `Value` is `80`, `UpperValue` is `null`, `IsNegated` is `false`, `Severity` is `RuleSeverity.Info` and `IsEnabled` is `true`

#### Scenario: Between factory stores the bounds in two distinct properties

- **Given** `Rule.Between("Temperature", 10, 30)`
- **When** the instance is inspected
- **Then** `Operator` is `ComparisonOperator.Between`, `Value` is `10` (the lower bound) and `UpperValue` is `30` (the upper bound)

#### Scenario: Group factories fix the logic operator

- **Given** two leaf rules `a` and `b`
- **When** `RuleGroup.And(a, b)` and `RuleGroup.Or(a, b)` are created
- **Then** the first has `Logic == LogicOperator.And`, the second `Logic == LogicOperator.Or`, both hold `Rules.Count == 2`, and both default to `Severity.Info`, `IsEnabled == true`, `IsNegated == false`

#### Scenario: RuleSet starts enabled with an empty rule list

- **Given** `new RuleSet("IoT Temperature Alarms")`
- **When** the instance is inspected
- **Then** `IsEnabled` is `true`, `Description` is `null` and `Rules` is an empty list

### Requirement: Dictionary-backed rule context

The system SHALL provide `DictionaryRuleContext`, which resolves fields by delegating directly to the
`Dictionary<string, object?>` handed to its constructor — retaining that instance by reference
(no copy) and therefore inheriting **that dictionary's key comparer**, so field lookup is
case-sensitive for a dictionary created with the default comparer — and SHALL throw
`ArgumentNullException` if the dictionary is `null`.

#### Scenario: Field present in the dictionary

- **Given** `DictionaryRuleContext.From(("Temp", 91.5))`
- **When** `TryGetValue("Temp", out var v)` is called
- **Then** it returns `true` and `v` is `91.5`; `HasField("Temp")` returns `true`

#### Scenario: Case mismatch is not resolved

- **Given** `DictionaryRuleContext.From(("Temp", 91.5))` (default ordinal, case-sensitive comparer)
- **When** `TryGetValue("temp", out var v)` is called
- **Then** it returns `false` and `v` is `null`

#### Scenario: Null dictionary rejected

- **Given** a `null` dictionary reference
- **When** `new DictionaryRuleContext(null)` is invoked
- **Then** an `ArgumentNullException` naming `values` is thrown

#### Scenario: From() keeps the last value for a repeated field

- **Given** `DictionaryRuleContext.From(("Temp", 1), ("Temp", 2))`
- **When** `TryGetValue("Temp", out var v)` is called
- **Then** `v` is `2`

### Requirement: Object-backed rule context resolves public instance properties case-insensitively

The system SHALL provide `ObjectRuleContext<T>`, which resolves a field name to a public instance
property of `T` using `BindingFlags.IgnoreCase`, caches the resolved `PropertyInfo` (including a
negative result) in a `ConcurrentDictionary` keyed case-insensitively and scoped to the closed
generic type, returns `true` with the property's value when the property exists (even if that value
is `null`), returns `false` with a `null` value when it does not, and SHALL throw
`ArgumentNullException` when constructed with a `null` instance.

#### Scenario: Property found regardless of casing

- **Given** a class with a `public string Name { get; set; }` set to `"abc"` and `new ObjectRuleContext<T>(instance)`
- **When** `TryGetValue("name", out var v)` is called
- **Then** it returns `true` and `v` is `"abc"`

#### Scenario: Existing property holding null is reported as present

- **Given** an instance whose `Name` property is `null`
- **When** `TryGetValue("Name", out var v)` is called
- **Then** it returns `true` and `v` is `null` — distinguishing "present but null" from "absent"

#### Scenario: Dotted path is not a supported field name

- **Given** an instance with `Address.City`
- **When** `TryGetValue("Address.City", out var v)` is called
- **Then** it returns `false`, because the lookup is a single `GetProperty("Address.City")` with no splitting on `'.'` — unlike `RuleExpressionConverter`, which does split nested paths

#### Scenario: Non-existent field

- **Given** any instance
- **When** `HasField("NoSuchProperty")` is called
- **Then** it returns `false`, and the negative lookup is cached for subsequent calls

### Requirement: Leaf rule evaluation

The system SHALL evaluate a leaf `Rule` by resolving `Field` from the context and applying
`Operator` through `ComparisonHelper.Compare`, SHALL return `NoMatch` immediately for a disabled rule
or for an unrecognised `IRule` implementation, SHALL evaluate `IsNull` / `IsNotNull` **without
requiring the field to exist**, SHALL return `NoMatch` for every other operator when the field is
absent, and SHALL carry the resolved field value on the result as `ActualValue`.

#### Scenario: Matching comparison

- **Given** `new Rule("Temp", ComparisonOperator.GreaterThan, 80)` and a context where `Temp` is `91.5`
- **When** `RuleEvaluator.Evaluate(rule, context)` is called
- **Then** the result has `IsMatch == true`, `ActualValue == 91.5` and `Severity == RuleSeverity.Info`

#### Scenario: Disabled rule never matches

- **Given** the same rule with `IsEnabled = false` and a context where `Temp` is `91.5`
- **When** `Evaluate` is called
- **Then** the result has `IsMatch == false` and `ActualValue == null` — the context is not consulted at all

#### Scenario: IsNull matches an absent field

- **Given** `new Rule("Missing", ComparisonOperator.IsNull, null)` and a context with no `Missing` field
- **When** `Evaluate` is called
- **Then** `IsMatch` is `true`, because the evaluator ignores the `TryGetValue` return value for null operators and compares the `out` default (`null`)

#### Scenario: Negation is applied on a present field but skipped on an absent one

- **Given** `new Rule("Temp", ComparisonOperator.GreaterThan, 80) { IsNegated = true }`
- **When** the context supplies `Temp = 10`
- **Then** the underlying comparison is `false`, negation flips it and `IsMatch` is `true`
- **When** the context has no `Temp` field at all
- **Then** the evaluator returns `NoMatch` *before* applying `IsNegated`, so `IsMatch` is `false` rather than `true`

#### Scenario: Negation is skipped when the operator could not be evaluated at all

- **Given** `new Rule("Code", ComparisonOperator.Contains, "234") { IsNegated = true }` on an `int` field
- **When** the rule is evaluated
- **Then** `IsMatch` is `false`. `ComparisonHelper.CanEvaluate` reports that a string operator has no answer
  for a non-string member, and negation is applied only to an answer the comparison could actually give —
  a `false` meaning *"does not apply"* must not invert into a match, which is the same widening that
  affected the expression translator

### Requirement: Group rule evaluation

The system SHALL evaluate a `RuleGroup` by short-circuiting over its **enabled** children — `And`
returning `NoMatch` on the first non-matching child and `Or` returning `Match` on the first matching
child — SHALL skip disabled children entirely, and SHALL return `NoMatch` for a group whose `Rules`
list is empty or whose every child is disabled.

#### Scenario: AND group with one failing child

- **Given** `RuleGroup.And(new Rule("A", Equal, 1), new Rule("B", Equal, 2))` and a context where `A == 1` and `B == 99`
- **When** `Evaluate` is called
- **Then** the result is `NoMatch` for the group

#### Scenario: Disabled child does not fail an AND group

- **Given** `RuleGroup.And(enabledMatchingRule, disabledFailingRule)`
- **When** `Evaluate` is called
- **Then** the disabled child is skipped and the group result is `Match`

#### Scenario: OR group reports the matching child's value

- **Given** `RuleGroup.Or(new Rule("A", Equal, 1), new Rule("B", Equal, 2))` and a context where `A == 99` and `B == 2`
- **When** `Evaluate` is called
- **Then** the result is `Match` and `ActualValue` is the `B` value (`2`), propagated from the child result

#### Scenario: Empty and all-disabled groups

- **Given** `new RuleGroup(LogicOperator.And)` with no children, or a group whose every child has `IsEnabled = false`
- **When** `Evaluate` is called
- **Then** the result is `NoMatch` in both cases (an empty AND group is *not* treated as vacuously true)

#### Scenario: Group-level negation is ignored in memory

- **Given** `RuleGroup.And(new Rule("A", Equal, 1)) { IsNegated = true }` and a context where `A == 1`
- **When** `Evaluate` is called
- **Then** the result is `Match` — `RuleEvaluator.EvaluateGroup` never reads `RuleGroup.IsNegated`, so the negation has no effect on in-memory evaluation (whereas `RuleExpressionConverter.BuildGroupExpression` *does* apply it)

### Requirement: Rule set and batch evaluation

The system SHALL expose `EvaluateAll` (a result per input rule, matches and non-matches alike),
`EvaluateMatches` (matches only) and `Evaluate(RuleSet, …)`, where the rule-set overload returns an
**empty** list when the set's `IsEnabled` is `false` and otherwise returns only the matches.

#### Scenario: EvaluateAll preserves input order and arity

- **Given** three rules of which one matches
- **When** `EvaluateAll(rules, context)` is called
- **Then** three `RuleResult`s are returned in input order, one with `IsMatch == true`

#### Scenario: Disabled rule set yields nothing

- **Given** a `RuleSet` containing two matching rules but with `IsEnabled = false`
- **When** `Evaluate(ruleSet, context)` is called
- **Then** an empty list is returned; no rule is evaluated

#### Scenario: Enabled rule set returns matches only

- **Given** an enabled `RuleSet` with one matching and one non-matching rule
- **When** `Evaluate(ruleSet, context)` is called
- **Then** exactly one result is returned and its `IsMatch` is `true`

### Requirement: Comparison semantics — numeric promotion with fractional tolerance

The system SHALL compare values numerically whenever **both** operands can be converted to `double`
(`double`, `int`, `long`, `float`, `decimal`, `short`, `byte`, or any value whose `ToString()`
parses as an invariant-culture number), SHALL treat a zero difference as equal, SHALL require exact
equality when both promoted values are integral, and SHALL otherwise accept a relative tolerance of
`1e-6 * max(|a|,|b|)`.

#### Scenario: Cross-type integral equality

- **Given** an actual value of `int 5` and an expected value of `double 5.0`
- **When** `ComparisonOperator.Equal` is evaluated
- **Then** the difference is `0` and the values compare equal

#### Scenario: Distinct large integers never falsely match

- **Given** an actual value of `1_000_000_000L` and an expected value of `1_000_000_001L`
- **When** `Equal` is evaluated
- **Then** both promoted values are integral, so the relative tolerance is not applied and the result is `false`

#### Scenario: float→double promotion error is absorbed

- **Given** an actual value of `0.1f` and an expected value of `0.1d`
- **When** `Equal` is evaluated
- **Then** the values are fractional and the difference is within `1e-6 * 0.1`, so the result is `true`

#### Scenario: Numeric-looking string is promoted

- **Given** an actual value of the string `"5"` and an expected value of `int 5`
- **When** `Equal` is evaluated
- **Then** `double.TryParse` succeeds for both and the result is `true`

#### Scenario: Non-numeric fallback compares ToString case-insensitively

- **Given** an actual value of `bool true` and an expected value of the string `"true"`
- **When** `Equal` is evaluated
- **Then** numeric promotion fails, `a.Equals(b)` is `false`, and the `ToString()` ordinal-ignore-case comparison (`"True"` vs `"true"`) makes the result `true`

### Requirement: Comparison semantics — ordering, null and Between

The system SHALL order values by numeric promotion first, then `DateTime`, then `IComparable`
(swallowing a thrown comparison exception and falling through), then an ordinal-ignore-case
`ToString()` comparison; SHALL rank a `null` actual value **below** any non-`null` expected value and
a `null` expected value **below** any non-`null` actual value; and SHALL treat `Between` as the
inclusive conjunction `actual >= Value && actual <= UpperValue`.

#### Scenario: Inclusive Between bounds

- **Given** `Rule.Between("Temp", 10, 30)`
- **When** the actual value is `10`, `20` or `30`
- **Then** the rule matches in all three cases
- **When** the actual value is `9` or `31`
- **Then** the rule does not match

#### Scenario: A null field satisfies LessThan but not GreaterThan

- **Given** a context where `Temp` resolves to `null`
- **When** `LessThan 5` is evaluated
- **Then** `CompareValues(null, 5)` returns `-1` and the rule matches
- **When** `GreaterThan 5` is evaluated
- **Then** the same `-1` makes the rule not match

#### Scenario: Between with a missing upper bound never matches

- **Given** `new Rule("Temp", ComparisonOperator.Between, 10)` with `UpperValue` left `null`
- **When** the actual value is `20`
- **Then** `CompareValues(20, null)` returns `1`, the `<= 0` half fails and the rule does not match

#### Scenario: IComparable type mismatch degrades to string comparison instead of throwing

- **Given** an actual value of `DateTime` and an expected value of an unrelated non-numeric type
- **When** an ordering operator is evaluated
- **Then** the `CompareTo` `ArgumentException` is caught and the comparison falls through to the ordinal-ignore-case `ToString()` comparison

### Requirement: Comparison semantics — string operators and LIKE

The system SHALL apply `Contains`, `NotContains`, `StartsWith`, `EndsWith` and `Like` **only to a `string`
actual value** (a `null` actual counts as string-compatible, since this engine sees values rather than
declared types). Against a non-string actual all five SHALL evaluate to `false` in **both** polarities —
`NotContains` is deliberately not `!Contains` here, because negating "this operator does not apply"
produces match-all.

For a string actual the system SHALL call the corresponding `string` method on `actual.ToString()` with
`StringComparison.OrdinalIgnoreCase` (returning `false` — and therefore `true` for `NotContains` —
whenever either side is `null`), and SHALL implement `Like` as SQL-style `%` wildcard matching: `%text%` →
contains, `%text` → ends-with, `text%` → starts-with, and no wildcard → case-insensitive equality.

This engine previously stringified any actual value, so `Code Contains "234"` matched an `int 12345`. That
answer could not be reconciled with the expression path, which runs against a database where no portable
translation of `column.ToString().Contains(…)` exists — so both engines were moved onto match-none. No
existing test depended on the stringified behaviour.

#### Scenario: A string operator against a non-string field matches nothing

- **Given** a context where `Code` resolves to `int 12345`
- **When** `Contains "234"` is evaluated, and separately `NotContains "234"`, and separately
  `Contains "234"` with `IsNegated = true`
- **Then** all three are `NoMatch` — and the same rule translated through `RuleSpecification.ToExpression()`
  agrees, which is the property the two engines are now tested on together

#### Scenario: A null actual is still treated as string-compatible

- **Given** a context where `Name` resolves to `null`
- **When** `NotContains "abc"` is evaluated
- **Then** the result is `Match` — a null field does not contain the needle. Only the typed expression path
  can tell a null `string` from a null `int?`, so this engine deliberately admits null rather than
  rejecting it and diverging on the case the two currently agree on

#### Scenario: LIKE wildcard forms

- **Given** an actual value of `"Warehouse"`
- **When** the pattern is `"%house"`, `"ware%"` or `"%reho%"`
- **Then** the rule matches in all three cases
- **When** the pattern is `"Warehouse"` with no wildcard
- **Then** it matches only that exact value, ignoring case

#### Scenario: Bare "%" pattern throws

- **Given** `new Rule("Name", ComparisonOperator.Like, "%")`
- **When** the rule is evaluated against any non-null string value
- **Then** `LikeString` takes the both-ends branch (a single `'%'` satisfies both `StartsWith('%')` and `EndsWith('%')`) and evaluates `pattern[1..^1]` on a one-character string, throwing `ArgumentOutOfRangeException`

#### Scenario: Null operand short-circuits

- **Given** an actual value of `null`
- **When** `Contains "x"` is evaluated
- **Then** the result is `false`; the corresponding `NotContains "x"` evaluates to `true`

### Requirement: Comparison semantics — collection membership

The system SHALL treat `In` as membership in `Value` when `Value` is a non-`string` `IEnumerable`
(comparing each element with the same equality semantics as `Equal`), SHALL treat a `string` `Value`
as a single candidate rather than a character sequence, SHALL return `false` when either the actual
value or `Value` is `null`, and SHALL define `NotIn` as the exact negation of that result.

#### Scenario: Membership in a typed array

- **Given** `new Rule("Status", ComparisonOperator.In, new[] { 1, 2, 3 })` and an actual value of `2`
- **When** the rule is evaluated
- **Then** it matches

#### Scenario: String value is one candidate, not a char set

- **Given** `new Rule("Code", ComparisonOperator.In, "AB")` and an actual value of `"A"`
- **When** the rule is evaluated
- **Then** it does not match, because `"AB"` is compared as a single value

#### Scenario: Empty collection and null actual

- **Given** `Value` is an empty array
- **When** `In` is evaluated
- **Then** it does not match, and the corresponding `NotIn` matches
- **When** the actual value is `null` and `Value` is a populated array
- **Then** `In` does not match and `NotIn` matches

### Requirement: Rule tree to LINQ predicate conversion

The system SHALL convert a rule tree into `Expression<Func<T,bool>>` via
`RuleExpressionConverter.ToExpression<T>`, SHALL return `null` for a disabled rule, for a disabled or
empty `RuleSet`, and whenever no child produced an expression, SHALL AND-combine the enabled rules of
a `RuleSet` or `IEnumerable<IRule>` overload, SHALL combine group children with `AndAlso` / `OrElse`
according to `RuleGroup.Logic`, and SHALL apply `RuleGroup.IsNegated` by wrapping the combined group
expression in `Expression.Not`.

#### Scenario: Rule set becomes a conjunction

- **Given** a `RuleSet` with `Name == "x"` and two enabled leaf rules
- **When** `ToExpression<T>(ruleSet)` is called
- **Then** a lambda over parameter `x` is returned whose body is an `AndAlso` of the two leaf predicates

#### Scenario: Disabled input yields no predicate

- **Given** a rule with `IsEnabled = false`, or a `RuleSet` with `IsEnabled = false`, or a `RuleSet` with zero rules
- **When** `ToExpression<T>` is called
- **Then** `null` is returned in every case, and the caller cannot distinguish these from a conversion failure

#### Scenario: Group negation is honoured in the expression path

- **Given** `RuleGroup.And(new Rule("A", Equal, 1)) { IsNegated = true }`
- **When** `ToExpression<T>` is called and the predicate is compiled and applied to an instance whose `A == 1`
- **Then** the predicate returns `false`, the opposite of what `RuleEvaluator.Evaluate` returns for the same group

#### Scenario: Property caching is concurrency-safe

- **Given** `ToExpression<T>` called concurrently from multiple threads for the same `(Type, field)` pair
- **When** property resolution runs
- **Then** the shared `ConcurrentDictionary<(Type, string), PropertyInfo?>` is used, so no dictionary corruption occurs

### Requirement: Expression conversion — property resolution and nested paths

The system SHALL resolve a rule's `Field` against `T` by splitting on `'.'` and walking public
instance properties case-insensitively, SHALL return `null` (silently dropping the rule) when any
segment cannot be resolved, and SHALL guard a nested access by prefixing `AndAlso` null checks for
every intermediate segment whose type is nullable.

#### Scenario: Nested field builds a guarded member access

- **Given** `new Rule("Address.City", ComparisonOperator.Equal, "Prague")` on an entity with a nullable `Address`
- **When** the predicate is built and applied to an entity whose `Address` is `null`
- **Then** the guard `x.Address != null` short-circuits and the predicate returns `false` without a `NullReferenceException`

#### Scenario: Unresolvable field is dropped, widening the query

- **Given** a `RuleSet` with one rule whose `Field` is a typo not present on `T`
- **When** `ToExpression<T>(ruleSet)` is called
- **Then** the leaf yields `null`, nothing is combined, and `ToExpression` returns `null` — a store handed a `null` filter reads every row rather than reporting the bad field

#### Scenario: Dropping a child changes group meaning asymmetrically

- **Given** a group with one resolvable and one unresolvable child
- **When** the group is `And`
- **Then** the surviving child alone forms the predicate, matching *more* rows than intended
- **When** the group is `Or`
- **Then** the surviving child alone forms the predicate, matching *fewer* rows than intended

#### Scenario: Non-nullable intermediate segment adds no guard

- **Given** a nested field whose intermediate segment is a non-nullable struct property
- **When** the predicate is built
- **Then** no null check is emitted for that segment

### Requirement: Expression conversion — per-operator predicate construction

The system SHALL build each operator as follows: `IsNull` as equality with a typed `null` constant,
or the constant `false` for a non-nullable value type (and `IsNotNull` as its `Not`); the ordering
operators as `Expression.MakeBinary`; `Equal` / `NotEqual` on a `string` property as
`string.Equals(member, constant, StringComparison.OrdinalIgnoreCase)`; `Between` as
`member >= lower && member <= upper`; `Contains` / `StartsWith` / `EndsWith` as a null-guarded
`string` method call with `OrdinalIgnoreCase`; `Like` by rewriting the `%` pattern onto those string
methods; `In` as an `OrElse` chain of equality tests; and any unrecognised operator as the constant
`false`. Leaf `IsNegated` SHALL wrap the operator body in `Expression.Not` before the nested null
guards are prepended.

#### Scenario: String equality is case-insensitive

- **Given** `new Rule("Name", ComparisonOperator.Equal, "abc")` on a `string Name`
- **When** the compiled predicate is applied to an entity with `Name == "ABC"`
- **Then** it returns `true`

#### Scenario: IsNull on a non-nullable value type is constant-folded

- **Given** `new Rule("Age", ComparisonOperator.IsNull, null)` where `Age` is `int`
- **When** the predicate is compiled and applied
- **Then** it always returns `false`, and the matching `IsNotNull` rule always returns `true`

#### Scenario: String operator on a non-string property throws

- **Given** `new Rule("Age", ComparisonOperator.Contains, "1")` where `Age` is `int`
- **When** `ToExpression<T>` is called
- **Then** an `InvalidOperationException` is thrown — `String operator 'Contains' cannot be applied to property of type 'Int32'.` — whereas the in-memory `ComparisonHelper` would have evaluated the same rule successfully via `ToString()`

#### Scenario: Empty In and NotIn sets

- **Given** `new Rule("Status", ComparisonOperator.In, Array.Empty<int>())`
- **When** the predicate is built
- **Then** the body is the constant `false` (matching nothing)
- **When** the operator is `NotIn` with the same empty set
- **Then** the body is `Not(false)`, matching every row

#### Scenario: Bare "%" LIKE pattern matches everything instead of throwing

- **Given** `new Rule("Name", ComparisonOperator.Like, "%")`
- **When** the predicate is built (the both-ends branch is skipped by its `pattern.Length > 1` guard) and applied
- **Then** it reduces to `Name != null && Name.EndsWith("")`, i.e. every non-null value matches — while the in-memory path throws `ArgumentOutOfRangeException` for the same rule

### Requirement: Expression conversion — rule value coercion

The system SHALL coerce a rule's `Value` to the target property's underlying type before embedding it
as a constant: pass through an already-assignable value, parse a `string` into `Guid`, `DateTime` or
`DateTimeOffset` using the invariant culture, parse or convert an enum (`Enum.Parse` ignoring case
for a string, `Enum.ToObject` otherwise), and otherwise call `Convert.ChangeType` with the invariant
culture; a coercion that the framework rejects SHALL surface as the underlying exception rather than
being suppressed.

#### Scenario: String value coerced to Guid

- **Given** `new Rule("Guid", ComparisonOperator.Equal, "3f2504e0-4f89-11d3-9a0c-0305e82c3301")` on a `Guid` property
- **When** the predicate is built
- **Then** the constant is the parsed `Guid` and the predicate compares correctly

#### Scenario: String value coerced to an enum member

- **Given** an enum-typed property and a rule `Value` of the string `"active"`
- **When** the predicate is built
- **Then** `Enum.Parse(underlying, "active", ignoreCase: true)` supplies the constant

#### Scenario: Null value against a non-nullable property throws

- **Given** `new Rule("Age", ComparisonOperator.Equal, null)` where `Age` is `int`
- **When** `ToExpression<T>` is called
- **Then** `ConvertValue` returns `null` and `Expression.Constant(null, typeof(int))` throws `ArgumentException` — whereas the in-memory path evaluates the same rule to a plain non-match

#### Scenario: Uncoercible value propagates the conversion exception

- **Given** a rule whose `Value` is `"not-a-number"` against an `int` property
- **When** `ToExpression<T>` is called
- **Then** the `Convert.ChangeType` `FormatException` propagates to the caller

### Requirement: Validation result accumulation

The system SHALL model a validation outcome as `ValidationResult`, valid exactly when its error list
is empty, accumulating `ValidationError(PropertyName, ErrorCode, Message)` records in the order added,
supporting `Merge` of another result's errors and `ToDictionary()` grouping messages by property name.

#### Scenario: Success and failure factories

- **Given** no input
- **When** `ValidationResult.Success()` is called
- **Then** `IsValid` is `true` and `Errors` is empty
- **When** `ValidationResult.Failure("Name", "REQUIRED", "'Name' is required.")` is called
- **Then** `IsValid` is `false` and `Errors` holds exactly that one record

#### Scenario: Merge appends

- **Given** result `a` with one error and result `b` with two
- **When** `a.Merge(b)` is called
- **Then** `a.Errors` has three entries, `b`'s appended after `a`'s

#### Scenario: ToDictionary groups by property

- **Given** two errors on `"Name"` and one on `"Email"`
- **When** `ToDictionary()` is called
- **Then** the dictionary has key `"Name"` with a two-element message array and key `"Email"` with one

### Requirement: Validation exception carries the full result

The system SHALL define `ValidationException` as an `Exception` exposing the failing
`ValidationResult` and whose `Message` is `"Validation failed: {N} error(s). "` followed by the
`ValidationError` records joined with `"; "`.

#### Scenario: Message reflects the error count

- **Given** a `ValidationResult` with two errors
- **When** `new ValidationException(result)` is constructed
- **Then** `Message` starts with `"Validation failed: 2 error(s). "` and `ValidationResult` returns the same instance

### Requirement: Fluent validator definition and execution

The system SHALL let a validator be declared by subclassing `AbstractValidator<T>` and calling the
protected `RuleFor(expression)` in the constructor, which wraps the property expression in a
`Convert` to `object?`, registers a `PropertyRule<T>` and returns a `RuleBuilder<T, TProp>`; `Validate`
SHALL run every registered property rule in registration order against a single shared
`ValidationContext<T>`, accumulate all failures rather than stopping at the first, throw
`ArgumentNullException` for a `null` instance, and `ValidateAsync` SHALL return the synchronous
result via `Task.FromResult` (never awaiting anything, and ignoring the cancellation token).

#### Scenario: All violations are reported

- **Given** a validator declaring `RuleFor(x => x.Name).Required().MaxLength(3)` and an instance whose `Name` is `"abcdef"`
- **When** `Validate(instance)` is called
- **Then** the result is invalid with a single `INVALID_LENGTH` error (`Required` passed), and the errors appear in rule-declaration order

#### Scenario: Two chains on the same property both run

- **Given** `RuleFor(x => x.Name).Required()` declared twice
- **When** `Validate` is called on an instance with `Name == null`
- **Then** two separate `REQUIRED` errors for `"Name"` are produced, one per registered `PropertyRule`

#### Scenario: Null instance rejected

- **Given** any validator
- **When** `Validate(null)` is called
- **Then** an `ArgumentNullException` naming `instance` is thrown

#### Scenario: Async path is synchronous

- **Given** any validator and an already-cancelled `CancellationToken`
- **When** `ValidateAsync(instance, ct)` is awaited
- **Then** the same result as `Validate(instance)` is returned; the token is not observed and no `OperationCanceledException` is raised

### Requirement: Property name extraction from the rule expression

The system SHALL derive a `PropertyRule<T>`'s `PropertyName` from the expression body by unwrapping a
`Convert` unary node and taking `MemberExpression.Member.Name`, SHALL throw `ArgumentException` for
any other expression shape, and SHALL read values through the compiled delegate at validation time.

#### Scenario: Simple property access

- **Given** `RuleFor(x => x.Name)`
- **When** the rule is registered
- **Then** `PropertyName` is `"Name"`

#### Scenario: Boxed value type is unwrapped

- **Given** `RuleFor(x => x.Temperature)` where `Temperature` is `double`
- **When** the rule is registered
- **Then** the inserted `Convert` node is unwrapped and `PropertyName` is `"Temperature"`

#### Scenario: Nested path loses the parent segment

- **Given** `RuleFor(x => x.Address.City)`
- **When** the rule is registered
- **Then** `PropertyName` is `"City"`, not `"Address.City"` — errors are reported against the leaf name only

#### Scenario: Non-member expression rejected

- **Given** `RuleFor(x => x.FirstName + x.LastName)`
- **When** the rule is registered
- **Then** an `ArgumentException` is thrown: `Expression must be a simple property access, got: …`

#### Scenario: Null intermediate on a nested path faults at validation time

- **Given** `RuleFor(x => x.Address.City)` and an instance whose `Address` is `null`
- **When** `Validate(instance)` is called
- **Then** the compiled accessor dereferences `null` and a `NullReferenceException` propagates out of `Validate` — the fluent path has no equivalent of `RuleExpressionConverter`'s null guards

### Requirement: Built-in rule catalogue and the null-is-valid convention

The system SHALL treat a `null` value as **valid** in `EmailRule`, `LengthRule`, `RangeRule` and
`RegexRule` — delegating null rejection to `RequiredRule` — and SHALL report a value of the wrong
runtime type as **invalid** in those same rules (`EmailRule`, `LengthRule` and `RegexRule` require a
`string`; `RangeRule` requires an `IComparable`). Each rule SHALL expose a fixed `ErrorCode`:
`REQUIRED`, `INVALID_EMAIL`, `INVALID_LENGTH`, `OUT_OF_RANGE`, `INVALID_FORMAT`, `CUSTOM_VALIDATION`.

#### Scenario: RequiredRule per value shape

- **Given** a `RequiredRule`
- **When** the value is `null`, `""`, `"   "`, an empty `ICollection`, an empty non-collection `IEnumerable`, or `Guid.Empty`
- **Then** it is invalid in every case
- **When** the value is `"x"`, a one-element collection, a non-empty `Guid`, or any other non-null object
- **Then** it is valid

#### Scenario: RequiredRule disposes the probed enumerator

- **Given** a lazy `IEnumerable` that is not an `ICollection` and whose enumerator implements `IDisposable`
- **When** `RequiredRule.IsValid` probes it
- **Then** a single `MoveNext()` is taken and the enumerator is disposed in a `finally` block

#### Scenario: EmailRule whitespace vs RegexRule whitespace

- **Given** a whitespace-only string `"   "`
- **When** `EmailRule.IsValid` is called
- **Then** it returns `true` (it short-circuits on `IsNullOrWhiteSpace`)
- **When** `RegexRule.IsValid` is called with a pattern that rejects whitespace
- **Then** it returns `false` — `RegexRule` short-circuits only on `IsNullOrEmpty`, so the two rules diverge for whitespace-only input

#### Scenario: Email pattern

- **Given** an `EmailRule`
- **When** the value is `"a@b.co"`
- **Then** it is valid per `^[^@\s]+@[^@\s]+\.[^@\s]+$`
- **When** the value is `"a@b"` or `"a b@c.co"`
- **Then** it is invalid

#### Scenario: Length rule bounds and non-string input

- **Given** `new LengthRule("Name", minLength: 2, maxLength: 4)`
- **When** the value is `"ab"` or `"abcd"`
- **Then** it is valid (bounds inclusive)
- **When** the value is `"a"` or `"abcde"`
- **Then** it is invalid
- **When** the value is `int 123`
- **Then** it is invalid, because the rule requires a `string`

#### Scenario: Range rule bounds are inclusive and one-sided bounds are supported

- **Given** `new RangeRule("Qty", min: 1, max: 10)`
- **When** the value is `1` or `10`
- **Then** it is valid
- **When** only `min` is supplied and the value is far above it
- **Then** it is valid, because the `max` half of the check is skipped

### Requirement: Range rule comparison is unguarded against operand type mismatch

The system SHALL evaluate `RangeRule` by calling `IComparable.CompareTo` on the property value with
the configured bound as a boxed `object`, without a type-compatibility check or exception guard, so a
bound whose runtime type differs from the property's type SHALL propagate the framework's
`ArgumentException` out of `Validate`.

#### Scenario: int literal bounds on a double property

- **Given** `RuleFor(x => x.Temperature).Range(-50, 150)` where `Temperature` is `double`
- **When** `Validate(instance)` is called
- **Then** `((IComparable)temperature).CompareTo((object)(int)-50)` throws `ArgumentException` (`Object must be of type Double.`) and the exception escapes `Validate` instead of producing an `OUT_OF_RANGE` error

#### Scenario: Matching bound types compare normally

- **Given** `RuleFor(x => x.Temperature).Range(-50d, 150d)` where `Temperature` is `double`
- **When** `Validate` is called for `Temperature == 200d`
- **Then** a single `OUT_OF_RANGE` error is reported

### Requirement: Fluent builder composition helpers

The system SHALL expose `Required`, `Email`, `MaxLength`, `MinLength`, `Length`, `Range`,
`GreaterThanOrEqual`, `LessThanOrEqual`, `Matches` (pattern or compiled `Regex`), `Must`,
`MustSatisfy`, `In` and `NotEqual` on `RuleBuilder<T, TProp>`, each appending a rule to the same
`PropertyRule<T>` and returning the builder for chaining, and each accepting an optional message that
overrides the rule's generated default.

#### Scenario: Chaining accumulates rules on one property

- **Given** `RuleFor(x => x.SerialNumber).Required().Matches("^[A-Z0-9-]+$")`
- **When** the chain is built
- **Then** the single `PropertyRule` for `"SerialNumber"` holds a `RequiredRule` followed by a `RegexRule`

#### Scenario: Custom message replaces the default

- **Given** `RuleFor(x => x.Name).Required("Name musí byť zadané")`
- **When** validation fails
- **Then** the error's `Message` is the supplied text and its `ErrorCode` is still `REQUIRED`

#### Scenario: GreaterThanOrEqual / LessThanOrEqual build one-sided ranges

- **Given** `RuleFor(x => x.Qty).GreaterThanOrEqual(1)`
- **When** the rule is inspected
- **Then** it is a `RangeRule` with only `min` set, so no upper bound is enforced

### Requirement: Predicate-based builder rules and their null handling

The system SHALL implement `Must(Func<TProp,bool>)` so that the predicate runs only when the value
matches `TProp`, and a value that does not match is valid exactly when the value is `null` and
`TProp` is a nullable type; SHALL implement `MustSatisfy(Func<T,bool>)` as a `CustomRule<T>` that
receives the whole instance from `ValidationContext.Instance` and returns valid when the instance is
not of type `T`; SHALL implement `In` by rejecting an empty allowed-value set up front and otherwise
requiring the value to match `TProp` and be present in the set; and SHALL implement `NotEqual`
through `object.Equals`.

#### Scenario: Must does not reject null for a reference-typed property

- **Given** `RuleFor(x => x.Name).Must(n => n.Length > 3)` where `Name` is `string`
- **When** `Validate` runs on an instance whose `Name` is `null`
- **Then** the predicate is not invoked, no `NullReferenceException` occurs, and the rule is valid — null rejection requires an explicit `Required()`

#### Scenario: Must rejects null for a non-nullable value-typed property

- **Given** `RuleFor(x => x.Age).Must(a => a > 0)` where the accessor yields `null` for a non-nullable `TProp` such as `int`
- **When** the rule is evaluated
- **Then** `default(int) is null` is `false` and the rule reports `CUSTOM_VALIDATION`

#### Scenario: In rejects an empty allowed set at declaration time

- **Given** a validator constructor calling `RuleFor(x => x.Status).In()`
- **When** the validator is constructed
- **Then** an `ArgumentException` — `At least one allowed value must be provided.` — is thrown, rather than producing a property that no value can satisfy

#### Scenario: In treats null as a failure, unlike the other rules

- **Given** `RuleFor(x => x.Status).In("A", "B")` and an instance whose `Status` is `null`
- **When** `Validate` runs
- **Then** `value is TProp typed` fails and a `NOT_IN_SET` error is reported — `In` does not follow the null-is-valid convention of `EmailRule` / `LengthRule` / `RangeRule` / `RegexRule`, and its message cannot be overridden

#### Scenario: MustSatisfy sees the whole model

- **Given** `RuleFor(x => x.EndDate).MustSatisfy(m => m.EndDate >= m.StartDate, "End must not precede start")`
- **When** `Validate` runs on an instance with `EndDate < StartDate`
- **Then** one error is reported against `"EndDate"` with the supplied message and error code `CUSTOM_VALIDATION`

#### Scenario: CustomRule<T> passes when the instance type does not match

- **Given** a `CustomRule<TOther>` evaluated against a `ValidationContext` whose `Instance` is not a `TOther`
- **When** `IsValid` is called
- **Then** it returns `true`, silently skipping the predicate

### Requirement: Validation context

The system SHALL provide `ValidationContext` exposing the model `Instance`, its runtime
`InstanceType`, a mutable `PropertyName` set by the executing `PropertyRule` before its rules run, an
optional `DisplayName`, and a free-form `Items` dictionary for rule-visible data; and SHALL provide
the strongly-typed `ValidationContext<T>` (obtainable via `ValidationContext.For<T>`) which exposes
`Instance` as `T`.

#### Scenario: PropertyName tracks the executing rule

- **Given** a validator with chains on `"Name"` then `"Email"`
- **When** the `"Email"` chain's rules run
- **Then** `context.PropertyName` is `"Email"` — one context instance is shared and mutated across all property rules of a single `Validate` call

#### Scenario: Null instance rejected

- **Given** a `null` reference
- **When** `new ValidationContext(null)` is constructed
- **Then** an `ArgumentNullException` naming `instance` is thrown

### Requirement: Rule-set-backed validator treats matched rules as violations

The system SHALL provide `RuleBasedValidator<T>`, which wraps a `RuleSet` (defaulting to a new
`RuleEvaluator` when none is supplied), evaluates it against an `ObjectRuleContext<T>` built from the
instance, and converts **every matched rule** into a `ValidationError` whose `PropertyName` is the
leaf rule's `Field` (or the rule's `Name`, or `"Unknown"`, for a non-leaf), whose `ErrorCode` is
`RULE_{SEVERITY}` upper-cased, and whose `Message` is the rule's `Description`, else its `Name`, else
a generated `Rule violation on '{property}' (severity: {severity})`.

#### Scenario: A matching rule becomes an error

- **Given** `new RuleBasedValidator<Device>(new RuleSet("Alarms", new Rule("Temperature", GreaterThan, 100) { Severity = RuleSeverity.Critical, Description = "Overheating" }))`
- **When** `Validate` runs on a device whose `Temperature` is `120`
- **Then** the result is invalid with one error: `PropertyName == "Temperature"`, `ErrorCode == "RULE_CRITICAL"`, `Message == "Overheating"`

#### Scenario: Rules describe violations, not requirements

- **Given** the same validator and a device whose `Temperature` is `20`
- **When** `Validate` runs
- **Then** no rule matches and the result is valid — a rule set authored to describe *acceptable* records would invert the outcome

#### Scenario: Info severity still fails validation

- **Given** a matching rule left at the default `RuleSeverity.Info`
- **When** `Validate` runs
- **Then** an error with `ErrorCode == "RULE_INFO"` is added and `IsValid` is `false`; severity does not gate whether a match becomes an error

#### Scenario: Disabled rule set validates everything

- **Given** a `RuleSet` whose `IsEnabled` is `false`
- **When** `Validate` runs on any instance
- **Then** the evaluator returns an empty match list and the result is valid

#### Scenario: Group match without a name

- **Given** a matched `RuleGroup` with `Name` and `Description` both `null`
- **When** the error is extracted
- **Then** `PropertyName` is `"Unknown"` and `Message` is `Rule violation on 'Unknown' (severity: …)`

#### Scenario: Async path is synchronous

- **Given** a `RuleBasedValidator<T>`
- **When** `ValidateAsync(instance, ct)` is awaited
- **Then** the synchronous result is returned via `Task.FromResult` and the token is ignored

### Requirement: Validating store wrapper (sync)

The system SHALL provide `ValidatingStoreWrapper<TStore, T>` implementing `IStore<T>` and
`IStoreWrapper<T>` for `T : AbstractModel`, which validates the entity and throws
`ValidationException` before delegating `Create`, `Update` and `Save`, delegates `Read`, `Count`,
`Delete`, `Init`, `Destroy` and `CreateInstance` unvalidated, rejects a `null` inner store or `null`
validator with `ArgumentNullException`, and exposes the wrapped store through `GetInnerStore()` /
`GetInnerStoreAs<TInner>()`.

#### Scenario: Invalid entity never reaches the inner store

- **Given** a wrapper over an in-memory store with a validator that rejects the entity
- **When** `Create(entity)` is called
- **Then** a `ValidationException` carrying the failing `ValidationResult` is thrown and the inner store's `Create` is not invoked

#### Scenario: Delete is not validated

- **Given** the same wrapper and an entity that fails validation
- **When** `Delete(entity)` is called
- **Then** the call is forwarded to the inner store without validation

#### Scenario: Inner store is reachable

- **Given** a wrapper over a concrete `InMemoryStore<T>`
- **When** `GetInnerStoreAs<InMemoryStore<T>>()` is called
- **Then** the concrete inner store instance is returned

#### Scenario: Null dependencies rejected

- **Given** a `null` inner store or a `null` validator
- **When** the wrapper is constructed
- **Then** an `ArgumentNullException` naming `innerStore` or `validator` is thrown

### Requirement: Validating store wrapper (async) and bulk aggregation

The system SHALL provide `AsyncValidatingStoreWrapper<TStore, T>` mirroring the sync wrapper over
`IAsyncStore<T>` (awaiting `IValidator<T>.ValidateAsync` before `CreateAsync`, `UpdateAsync` and
`SaveAsync`), and `AsyncValidatingBulkStoreWrapper<TStore, T>` which extends it with `IAsyncBulkStore<T>`
and, for the enumerable `CreateAsync` / `UpdateAsync` overloads, validates **every** item, merges all
failures into one aggregated `ValidationResult` and throws a single `ValidationException` only after
the whole batch has been checked.

#### Scenario: Batch failures are aggregated, not reported one at a time

- **Given** a batch of three entities of which two are invalid with one error each
- **When** `CreateAsync(batch)` is awaited
- **Then** a single `ValidationException` is thrown whose `ValidationResult.Errors` contains both errors, and the inner store's bulk `CreateAsync` is not invoked

#### Scenario: Valid batch passes through

- **Given** a batch where every entity validates
- **When** `UpdateAsync(batch)` is awaited
- **Then** the aggregated result is valid and the batch is forwarded to the inner store

#### Scenario: Filter-based bulk operations bypass validation

- **Given** a bulk wrapper whose validator would reject the mutation's outcome
- **When** `UpdateAsync(filter, updateAction)` or `UpdateAsync(filter, propertyUpdate)` is awaited
- **Then** the call is forwarded straight to the inner store with no validation performed on the affected entities

#### Scenario: Deletes and reads are unvalidated

- **Given** a bulk wrapper
- **When** `DeleteAsync(data)`, `DeleteAsync(filter)`, `ReadAsync(...)` or `CountAsync(...)` is awaited
- **Then** each is forwarded to the inner store unchanged

#### Scenario: Batch input is enumerated twice

- **Given** a lazily-generated `IEnumerable<T>` passed to `CreateAsync`
- **When** the call is awaited
- **Then** the sequence is enumerated once for validation and again by the inner store, so a single-pass or side-effecting generator is re-run

### Requirement: No synchronous bulk validating wrapper exists

The system SHALL provide validating decorators for the synchronous single-entity store, the
asynchronous single-entity store and the asynchronous bulk store only; there SHALL be no synchronous
bulk validating wrapper, so a synchronous `IBulkStore<T>` cannot be validation-decorated by this
library.

#### Scenario: Composing validation onto a synchronous bulk store

- **Given** a synchronous `IBulkStore<T>` implementation
- **When** a caller looks for a `ValidatingBulkStoreWrapper<TStore, T>` in `Birko.Validation.Integration`
- **Then** no such type exists; only `ValidatingStoreWrapper`, `AsyncValidatingStoreWrapper` and `AsyncValidatingBulkStoreWrapper` are available, and wrapping the bulk store with `ValidatingStoreWrapper` validates only the single-entity `Create` / `Update` / `Save` surface
