---
area: validation-and-rules
generated-at: 4b5bf7e6a33b66aa46da5e6adc40ff88e547da43
generated-on: 2026-10-16
sources:
  - ../Birko.Rules/Context/DictionaryRuleContext.cs
  - ../Birko.Rules/Context/IRuleContext.cs
  - ../Birko.Rules/Context/ObjectRuleContext.cs
  - ../Birko.Rules/Core/ComparisonOperator.cs
  - ../Birko.Rules/Core/IRule.cs
  - ../Birko.Rules/Core/LogicOperator.cs
  - ../Birko.Rules/Core/Rule.cs
  - ../Birko.Rules/Core/RuleGroup.cs
  - ../Birko.Rules/Core/RuleList.cs
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
# source-commits: omitted — since the monorepo migration (TASK-457) every source above is inside this
# repo, so `generated-at` covers them (regen step 5c). The old per-sibling shas no longer resolve here.
shaped-by: [FEATURE-014]
# Evidence pass ran 2026-10-12 over 325 feature-linked tasks: 59 resolved, 266 did not —
# polyrepo-era `pr:` shas the history import rewrote, or no reachable commit whose subject leads with the
# task id; 9 of those are refused by the state gate (todo/cancelled). Earlier entries kept append-only.
shaped-by-derived: true
shaped-by-unresolved: 266
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
(a `LogicOperator` plus a `RuleList` of children, itself an `IRule`, nestable), both exposing the
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

### Requirement: Rule lists are copy-on-write and safe to edit during evaluation

The system SHALL hold the rules of a `RuleSet` and of a `RuleGroup` in `RuleList`, an `IList<IRule>` /
`IReadOnlyList<IRule>` whose every edit (`Add`, `AddRange`, `Insert`, `Remove`, `RemoveAt`, `Clear`, the
indexer setter) copies the current array under a lock and publishes the new array, and whose reads
(`Count`, the indexer, `Contains`, `IndexOf`, `CopyTo`, enumeration, `Snapshot()`) take no lock and see the
array current when they started. `RuleEvaluator` and `RuleExpressionConverter` SHALL evaluate a set's or a
group's rules from such a snapshot. The constructors copy the supplied sequence (`RuleList(null)` throws
`ArgumentNullException`), and assigning `RuleSet.Rules = null` SHALL throw `ArgumentNullException`
(SH-M370, TASK-156).

#### Scenario: A reload edits the rule set while it is evaluated

- **Given** a `RuleSet` being evaluated by `RuleEvaluator.Evaluate(ruleSet, context)` on one thread
- **When** another thread calls `ruleSet.Rules.Add(…)` or `Clear()` mid-evaluation
- **Then** no "Collection was modified" exception is thrown; the evaluation runs over the rules as they were when it took its snapshot, and the edit is visible to the next evaluation

#### Scenario: A snapshot is not changed by a later edit

- **Given** `var snapshot = ruleSet.Rules.Snapshot()` on a set of two rules
- **When** a third rule is added
- **Then** `snapshot` still holds two rules and `ruleSet.Rules.Count` is `3`

#### Scenario: Constructors copy their input

- **Given** a `List<IRule>` passed to `new RuleGroup(LogicOperator.And, list)`
- **When** the caller later adds to `list`
- **Then** the group's `Rules` is unchanged

### Requirement: Dictionary-backed rule context

The system SHALL provide `DictionaryRuleContext`, which resolves fields against the
`Dictionary<string, object?>` handed to its constructor — retaining that instance by reference
(no copy) — first by an exact lookup with **that dictionary's own key comparer** and, on a miss, by a
scan for the first key equal to the field under `OrdinalIgnoreCase`, so field lookup is
case-insensitive whatever comparer the caller's dictionary uses (SH-M369, TASK-156); `From(...)` builds
its dictionary with `StringComparer.OrdinalIgnoreCase`. It SHALL throw `ArgumentNullException` if the
dictionary is `null`, and it is an open context (`HasFixedFields` is `false`).

#### Scenario: Field present in the dictionary

- **Given** `DictionaryRuleContext.From(("Temp", 91.5))`
- **When** `TryGetValue("Temp", out var v)` is called
- **Then** it returns `true` and `v` is `91.5`; `HasField("Temp")` returns `true`

#### Scenario: Case mismatch is resolved

- **Given** `new DictionaryRuleContext(new Dictionary<string, object?> { ["Temp"] = 91.5 })` (default ordinal, case-sensitive comparer)
- **When** `TryGetValue("temp", out var v)` is called
- **Then** the exact lookup misses, the case-insensitive fallback finds `"Temp"`, and it returns `true` with `v == 91.5` — as `ObjectRuleContext` and the translator resolve fields

#### Scenario: Absent field

- **Given** `DictionaryRuleContext.From(("Temp", 91.5))`
- **When** `TryGetValue("Humidity", out var v)` is called
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

The system SHALL provide `ObjectRuleContext<T>`, which resolves a field name — a property name or a
dotted path (`Address.City`), split on `'.'` — to a chain of public instance properties starting at `T`
using `BindingFlags.IgnoreCase`, caches the resolved path (including a negative result) in a
`ConcurrentDictionary` keyed case-insensitively and scoped to the closed generic type, returns `true`
with the leaf property's value when every segment exists and no intermediate value is `null` (even if
the leaf value is `null`), returns `false` with a `null` value when a segment does not exist or an
intermediate is `null`, reports `HasField` from the path resolution alone, reports `HasFixedFields`
as `true`, and SHALL throw `ArgumentNullException` when constructed with a `null` instance (TASK-144,
SH-M297).

#### Scenario: Property found regardless of casing

- **Given** a class with a `public string Name { get; set; }` set to `"abc"` and `new ObjectRuleContext<T>(instance)`
- **When** `TryGetValue("name", out var v)` is called
- **Then** it returns `true` and `v` is `"abc"`

#### Scenario: Existing property holding null is reported as present

- **Given** an instance whose `Name` property is `null`
- **When** `TryGetValue("Name", out var v)` is called
- **Then** it returns `true` and `v` is `null` — distinguishing "present but null" from "absent"

#### Scenario: Dotted path resolves a nested property

- **Given** an instance whose `Address.City` is `"Prague"`
- **When** `TryGetValue("address.city", out var v)` is called
- **Then** it returns `true` and `v` is `"Prague"`, walking the same path `RuleExpressionConverter` builds

#### Scenario: A null intermediate has no value

- **Given** an instance whose `Address` is `null`
- **When** `TryGetValue("Address.City", out var v)` is called
- **Then** it returns `false` and `v` is `null`, while `HasField("Address.City")` still returns `true`

#### Scenario: Non-existent field

- **Given** any instance
- **When** `HasField("NoSuchProperty")` is called
- **Then** it returns `false`, and the negative lookup is cached for subsequent calls

### Requirement: Leaf rule evaluation

The system SHALL evaluate a leaf `Rule` by resolving `Field` from the context and applying
`Operator` through `ComparisonHelper.Compare`, SHALL return `NoMatch` immediately for a disabled rule
or for an unrecognised `IRule` implementation, SHALL return `NoMatch` — for every operator, negated or
not — when the context has fixed fields (`IRuleContext.HasFixedFields`, default `false`) and cannot
reach the field, SHALL otherwise evaluate `IsNull` / `IsNotNull` **without requiring the field to
exist**, SHALL return `NoMatch` for every other operator when the field is absent, and SHALL carry the
resolved field value on the result as `ActualValue`.

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

#### Scenario: A typed context's unreachable field matches nothing

- **Given** an `ObjectRuleContext<T>` and `new Rule("Typo", ComparisonOperator.IsNull, null)` — or `Address.City IsNull` on an entity whose `Address` is `null`
- **When** `Evaluate` is called, with or without `IsNegated`
- **Then** `IsMatch` is `false`: the context has fixed fields, so a field it cannot reach has no value at all, as the store filter answers (TASK-144, SH-M297). Before, `Typo IsNull` matched every entity in memory

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

#### Scenario: Negation is skipped for what the translator degrades

- **Given** a negated `Contains null`, a negated `In null`, or a negated `Like "a%b"` (interior `%`) on a string field
- **When** the rule is evaluated
- **Then** `IsMatch` is `false`: `CanEvaluate` reports a null needle, a null set and an interior-`%` pattern as unanswerable, so the negation is not applied — the store filter matches nothing for the same rules (TASK-156)

### Requirement: Group rule evaluation

The system SHALL evaluate a `RuleGroup` by short-circuiting over its **enabled** children — `And`
returning `NoMatch` on the first non-matching child and `Or` returning `Match` on the first matching
child — SHALL skip disabled children entirely, and SHALL return `NoMatch` for a group whose `Rules`
list is empty or whose every child is disabled. It SHALL honour `RuleGroup.IsNegated` by pushing the negation
to the leaves (De Morgan) — a negated AND evaluates as an OR of its children's negations, a negated OR as an
AND — so a leaf whose answer could not be given is still never inverted, and a negated empty or all-disabled
group is still `NoMatch` (SH-H045, [[TASK-320]]). It agrees with `RuleSpecification.ToExpression()` on group negation.

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

#### Scenario: Group-level negation is applied in memory

- **Given** `RuleGroup.And(new Rule("A", Equal, 1)) { IsNegated = true }` and a context where `A == 1`
- **When** `Evaluate` is called
- **Then** the result is `NoMatch` — until SH-H045 `EvaluateGroup` never read `IsNegated` and answered `Match`

#### Scenario: A negated group does not invert an inapplicable leaf

- **Given** a negated AND group of `Quantity GreaterThan 0` and `Quantity Contains "1"`, and a context where `Quantity == 5`
- **When** `Evaluate` is called
- **Then** the result is `NoMatch`: `Contains` cannot be evaluated on an `int`, so it stays a non-match under the
  negation, leaving `NOT(Quantity > 0)` — false. Inverting the whole group's result would have answered `Match`

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

The system SHALL compare two **exact** numbers (any integer type or `decimal`) exactly, as `decimal`
(SH-M360, TASK-156). Otherwise it SHALL compare values numerically, as `double`, whenever both operands
are numbers (`double`, `float`, `decimal` or any integer type), or one is a number and the other a
`string` that parses with `NumberStyles.Float` in the invariant culture to a finite value — a string is
read as a number **only against a numeric side**, never against another string (SH-M361, TASK-156). In
the `double` comparison it SHALL treat a zero difference as equal, SHALL require exact equality when
both promoted values are integral, and SHALL otherwise accept a relative tolerance of
`1e-6 * max(|a|,|b|)`.

#### Scenario: Cross-type integral equality

- **Given** an actual value of `int 5` and an expected value of `double 5.0`
- **When** `ComparisonOperator.Equal` is evaluated
- **Then** the difference is `0` and the values compare equal

#### Scenario: Distinct large integers never falsely match

- **Given** an actual value of `1_000_000_000L` and an expected value of `1_000_000_001L`
- **When** `Equal` is evaluated
- **Then** both are exact numbers, compared as `decimal`, and the result is `false`

#### Scenario: Integers beyond double precision and long decimals compare exactly

- **Given** an actual value of `9007199254740993L` and an expected value of `9007199254740992L`, or `1.0000000000000000001m` and `1.0m`
- **When** `Equal` is evaluated
- **Then** the result is `false` — promoting both to `double` used to equate them

#### Scenario: float→double promotion error is absorbed

- **Given** an actual value of `0.1f` and an expected value of `0.1d`
- **When** `Equal` is evaluated
- **Then** the values are fractional and the difference is within `1e-6 * 0.1`, so the result is `true`

#### Scenario: Numeric-looking string is promoted

- **Given** an actual value of the string `"5"` and an expected value of `int 5`
- **When** `Equal` is evaluated
- **Then** the other side is a number, so the string is parsed and the result is `true`

#### Scenario: Two numeric-looking strings compare as text

- **Given** an actual value of the string `"007"` and an expected value of the string `"7"` (or `"(5)"` and `"-5"`, or `"1,000"` and `"1000"`)
- **When** `Equal` is evaluated
- **Then** neither side is a number, so no parse is attempted, and the result is `false` — as the store compares them

#### Scenario: Non-numeric fallback compares ToString case-insensitively

- **Given** an actual value of `bool true` and an expected value of the string `"true"`
- **When** `Equal` is evaluated
- **Then** numeric promotion fails, `a.Equals(b)` is `false`, and the `ToString()` ordinal-ignore-case comparison (`"True"` vs `"true"`) makes the result `true`

### Requirement: Comparison semantics — ordering, null and Between

The system SHALL order values exactly (as `decimal`) when both are exact numbers, then by numeric
promotion (under the same string-against-a-number rule as equality), then `DateTime`, then `IComparable`
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

#### Scenario: Two numeric-looking strings order as strings

- **Given** an actual value of the string `"10"` and an expected value of the string `"9"`
- **When** `GreaterThan` is evaluated
- **Then** neither side is a number, so `string.CompareTo` (the `IComparable` step, culture-sensitive) orders `"10"` before `"9"` and the rule does not match. The translator has no store form for ordering strings and degrades such a leaf to match-nothing, so string ordering is evaluator-only

### Requirement: Comparison semantics — string operators and LIKE

The system SHALL apply `Contains`, `NotContains`, `StartsWith`, `EndsWith` and `Like` **only to a `string`
actual value** (a `null` actual counts as string-compatible, since this engine sees values rather than
declared types). Against a non-string actual all five SHALL evaluate to `false` in **both** polarities —
`NotContains` is deliberately not `!Contains` here, because negating "this operator does not apply"
produces match-all.

For a string actual the system SHALL call the corresponding `string` method on `actual.ToString()` with
`StringComparison.OrdinalIgnoreCase` (returning `false` whenever either side is `null`; `NotContains`
returns `true` for a `null` actual and `false` for a `null` needle, which is no predicate — SH-M296,
TASK-171), and SHALL implement `Like` as SQL-style `%` wildcard matching honoured **only at the ends**,
as the translator reads it (TASK-156): `%text%` → contains, `%text` → ends-with, `text%` → starts-with,
no wildcard → case-insensitive equality, a lone `%` → any non-null value; a `null` or empty pattern, or
one with an interior `%`, matches nothing (SH-M362), and `_` is a literal character.

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

#### Scenario: Bare "%" pattern matches any value

- **Given** `new Rule("Name", ComparisonOperator.Like, "%")`
- **When** the rule is evaluated against any non-null string value
- **Then** it matches: the trimmed core is empty and every string contains it. It used to slice `pattern[1..^1]` on a one-character string and throw `ArgumentOutOfRangeException` (SH-M359)

#### Scenario: Interior wildcard matches nothing

- **Given** `new Rule("Name", ComparisonOperator.Like, "Ware%house")` and an actual value of `"Warehouse"` or of the literal `"Ware%house"`
- **When** the rule is evaluated, negated or not
- **Then** it does not match — neither engine guesses at an interior `%` (SH-M362)

#### Scenario: Null operand short-circuits

- **Given** an actual value of `null`
- **When** `Contains "x"` is evaluated
- **Then** the result is `false`; the corresponding `NotContains "x"` evaluates to `true`

#### Scenario: A null needle matches nothing under NotContains too

- **Given** an actual value of `"abc"` and `NotContains null`
- **When** the rule is evaluated
- **Then** the result is `false`, as `Contains null` is and as the store filter answers — not the inversion of "contains nothing"

### Requirement: Comparison semantics — collection membership

The system SHALL treat `In` as membership in `Value` when `Value` is a non-`string` `IEnumerable`
(comparing each element with the same equality semantics as `Equal`), SHALL treat a `string` `Value`
as a single candidate rather than a character sequence, SHALL return `false` when either the actual
value or `Value` is `null`, and SHALL define `NotIn` as the exact negation of that result except when
`Value` is `null`, where `NotIn` is `false` too — a null set is no predicate (TASK-156).

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

#### Scenario: A null set matches nothing in both polarities

- **Given** `new Rule("Status", ComparisonOperator.NotIn, null)` and an actual value of `2`
- **When** the rule is evaluated
- **Then** it does not match, and neither does the corresponding `In` — before TASK-156 `NotIn null` matched every value in memory while the store matched none

### Requirement: Rule tree to LINQ predicate conversion

The system SHALL convert a rule tree into `Expression<Func<T,bool>>` via
`RuleExpressionConverter.ToExpression<T>` — the one rule-to-LINQ translator, which
`RuleSpecification.ToExpression()` calls (TASK-144) — and SHALL select the same rows as `RuleSpecification`
and `RuleEvaluator`, with one recorded divergence: string `Equal` / `NotEqual` (and a `Like` with no
wildcard) is case-sensitive here and case-insensitive in the evaluator. It SHALL never return `null`. It
SHALL answer **match-nothing** (`x => false`) for a disabled rule, an empty or all-disabled group, a
disabled `RuleSet`, an empty rule list, a field that resolves to no property, and a leaf whose value or
member type it cannot express. That answer is never inverted by an enclosing negation: `A AND <match-nothing>`
matches nothing, and `A OR <match-nothing>` is `A`. It SHALL AND-combine the enabled rules of a `RuleSet` or
`IEnumerable<IRule>` and skip their disabled members. It SHALL apply `RuleGroup.IsNegated` by De Morgan down
to the leaves: a negated group flips its connective and inverts each leaf. It never wraps the combined group
in `Expression.Not`. (FIELD-025 and `SH-M358` were fixed by TASK-558. Before that fix the converter returned
`null`, which every store reads as "no filter", and it dropped an untranslatable child, which widened an AND.)

#### Scenario: Rule set becomes a conjunction

- **Given** a `RuleSet` with `Name == "x"` and two enabled leaf rules
- **When** `ToExpression<T>(ruleSet)` is called
- **Then** a lambda over parameter `x` is returned whose body is an `AndAlso` of the two leaf predicates

#### Scenario: Disabled input matches nothing

- **Given** a rule with `IsEnabled = false`, or a `RuleSet` with `IsEnabled = false`, or a `RuleSet` with zero rules,
  or an empty or all-disabled `RuleGroup`
- **When** `ToExpression<T>` is called
- **Then** a non-null predicate is returned that selects no rows — never `null`, which a store reads as "no filter"

#### Scenario: Group negation agrees with the other engines

- **Given** `RuleGroup.And(new Rule("A", Equal, 1)) { IsNegated = true }`
- **When** `ToExpression<T>` is called and the predicate is compiled and applied to an instance whose `A == 1`
- **Then** the predicate returns `false`, as `RuleEvaluator.Evaluate` and `RuleSpecification` answer for the same group

#### Scenario: The converter agrees with RuleSpecification and RuleEvaluator

- **Given** any of: an unresolved leaf (negated or not), `A AND unresolved`, `A OR unresolved`,
  `NOT (A AND unresolved)`, `NOT (A OR unresolved)`, `NOT ((A OR unresolved) AND B)`, a disabled leaf, an empty
  group, `A AND <empty>`, a negated empty group, `NOT (A OR B)`, `NOT (A AND B)`
- **When** each is applied to one shared table through all three engines
- **Then** all three select the same rows (`RuleExpressionConverterAgreementTests`)

#### Scenario: Property caching is concurrency-safe

- **Given** `ToExpression<T>` called concurrently from multiple threads for the same `(Type, field)` pair
- **When** property resolution runs
- **Then** the shared `ConcurrentDictionary<(Type, string), PropertyInfo[]?>` path cache is used, so no dictionary corruption occurs

### Requirement: Expression conversion — property resolution and nested paths

The system SHALL resolve a rule's `Field` against `T` by splitting on `'.'` and walking public instance
properties case-insensitively. When any segment cannot be resolved it SHALL treat the rule as
match-nothing. The rule is never dropped, as it was before TASK-558. The system SHALL guard a nested access
by prefixing `AndAlso` null checks for every intermediate segment whose type is nullable. The checks go
outside any negation, whether it comes from the rule's own `IsNegated` or from an enclosing negated group,
so a null intermediate never matches — which `ObjectRuleContext` and the evaluator now answer the same
way (TASK-144; `SH-M367` resolved by that decision).

#### Scenario: Nested field builds a guarded member access

- **Given** `new Rule("Address.City", ComparisonOperator.Equal, "Prague")` on an entity with a nullable `Address`
- **When** the predicate is built and applied to an entity whose `Address` is `null`
- **Then** the guard `x.Address != null` short-circuits and the predicate returns `false` without a `NullReferenceException`

#### Scenario: An unresolvable field matches nothing

- **Given** a `RuleSet` with one rule whose `Field` is a typo not present on `T`
- **When** `ToExpression<T>(ruleSet)` is called
- **Then** the predicate selects no rows. Before TASK-558 it was `null`, and a store handed `null` read every row.

#### Scenario: An unresolvable child narrows or vanishes like the other engines

- **Given** a group with one resolvable child `A` and one unresolvable child
- **When** the group is `And`
- **Then** it matches nothing
- **When** the group is `Or`
- **Then** it is `A`

#### Scenario: Both negation routes agree on a null parent

- **Given** `Address.City == "Prague"` negated by its own `IsNegated`, and the same rule inside a negated group
- **When** both predicates are applied to an entity whose `Address` is `null`
- **Then** neither matches it, because the negation lands inside the null guard in both cases

#### Scenario: Non-nullable intermediate segment adds no guard

- **Given** a nested field whose intermediate segment is a non-nullable struct property
- **When** the predicate is built
- **Then** no null check is emitted for that segment

### Requirement: Expression conversion — per-operator predicate construction

The system SHALL build each operator as follows: `IsNull` as equality with a typed `null` constant,
or the constant `false` for a non-nullable value type (and `IsNotNull` as the matching inequality, or
the constant `true`); the comparison operators (`Equal`, `NotEqual` and the ordering operators) as the
corresponding `Expression` binary node — on a `string` property plain `==` / `!=`, the
store-translatable form, so string equality is **case-sensitive** at the store (SH-M295, TASK-144) — with
the member converted to `double` when the value is fractional and the member integral (SH-M294); a
comparison for which the type defines no operator (`string > string`, `bool >= bool`) as an unevaluable
leaf (SH-M291); `Between` as `member >= lower && member <= upper`, each bound built by that same comparison
(so a fractional bound against an integral member is compared in `double`, TASK-645), unevaluable when a bound
cannot be converted or the type has no ordering; `Contains` / `StartsWith` / `EndsWith` as a null-guarded
`string` method call with `OrdinalIgnoreCase`, unevaluable on a non-`string` member or for a `null`
needle (SH-M296); `NotContains` / `NotIn` as the `Not` of their positive form unless that form is
unevaluable; `Like` by rewriting a `%` at the start and/or end onto those string methods (no wildcard →
case-sensitive `==`), unevaluable on a non-`string` member, for a `null` or empty pattern, or for an
interior `%`; and `In` as an `OrElse` chain of the same equality comparison over the candidates (a
non-collection value is one candidate; a fractional candidate against an integral member compared in `double`), unevaluable for a `null` set or any candidate that cannot be converted. An unevaluable
leaf is the constant `false` and is **never negated**. An operator with no arm SHALL throw
`NotSupportedException` naming the operator and the field, rather than becoming a constant (SH-H041).
Leaf `IsNegated` (combined with an enclosing negated group) SHALL wrap a non-degraded operator body in
`Expression.Not` before the nested null guards are prepended.

#### Scenario: String equality is case-sensitive

- **Given** `new Rule("Name", ComparisonOperator.Equal, "abc")` on a `string Name`
- **When** the compiled predicate is applied to an entity with `Name == "ABC"`
- **Then** it returns `false` — the body is plain `x.Name == "abc"`, which every store translates; `RuleEvaluator` answers `Match` for the same rule, the one recorded divergence between the engines

#### Scenario: IsNull on a non-nullable value type is constant-folded

- **Given** `new Rule("Age", ComparisonOperator.IsNull, null)` where `Age` is `int`
- **When** the predicate is compiled and applied
- **Then** it always returns `false`, and the matching `IsNotNull` rule always returns `true`

#### Scenario: String operator on a non-string property matches nothing

- **Given** `new Rule("Age", ComparisonOperator.Contains, "1")` where `Age` is `int`, negated or not, or the same field under `NotContains "1"`
- **When** `ToExpression<T>` is called
- **Then** no exception is thrown and the predicate selects no rows, as `ComparisonHelper` answers for a non-string actual

#### Scenario: A fractional value against an integral member is not rounded

- **Given** `new Rule("Qty", ComparisonOperator.Equal, 10.7)` where `Qty` is `int`
- **When** the predicate is applied to an entity with `Qty == 11`
- **Then** it returns `false`: the member is compared as `double`, not the value rounded to `11` by `Convert.ChangeType`

#### Scenario: Ordering a string degrades instead of throwing

- **Given** `new Rule("Name", ComparisonOperator.GreaterThan, "m")` on a `string Name`
- **When** `ToExpression<T>` is called
- **Then** no exception is thrown and the predicate selects no rows, negated or not — no store orders strings as the evaluator does

#### Scenario: A null needle matches nothing

- **Given** `new Rule("Name", ComparisonOperator.Contains, null)` or `NotContains null`
- **When** the predicate is applied
- **Then** it selects no rows — the needle used to become `""`, which every string contains

#### Scenario: Empty In and NotIn sets

- **Given** `new Rule("Status", ComparisonOperator.In, Array.Empty<int>())`
- **When** the predicate is built
- **Then** the body is the constant `false` (matching nothing)
- **When** the operator is `NotIn` with the same empty set
- **Then** the body is `Not(false)`, matching every row

#### Scenario: A null In set and a scalar In value

- **Given** `new Rule("Status", ComparisonOperator.In, null)` or `NotIn null`
- **When** the predicate is applied
- **Then** it selects no rows in both polarities
- **When** the `Value` is the scalar `2` instead of a collection
- **Then** the body is the single equality `x.Status == 2`, as the evaluator reads one candidate

#### Scenario: An unrecognised operator throws

- **Given** a leaf whose `Operator` is a value the converter has no arm for, negated or not
- **When** `ToExpression<T>` is called
- **Then** a `NotSupportedException` is thrown naming `ComparisonOperator.{value}` and the field — a gap in the code is not turned into a constant that changes the filter silently

#### Scenario: Bare "%" LIKE pattern matches every non-null value

- **Given** `new Rule("Name", ComparisonOperator.Like, "%")`
- **When** the predicate is built and applied
- **Then** the trimmed core is empty and it reduces to `Name != null && Name.Contains("", OrdinalIgnoreCase)`, i.e. every non-null value matches — as the in-memory path now answers for the same rule

#### Scenario: Interior LIKE wildcard matches nothing

- **Given** `new Rule("Name", ComparisonOperator.Like, "a%b")`, negated or not
- **When** the predicate is applied
- **Then** it selects no rows; the pattern is not matched as a literal

### Requirement: Expression conversion — rule value coercion

The system SHALL coerce a rule's `Value` to the target property's underlying type before embedding it
as a constant: pass through an already-assignable value, parse a `string` into `Guid`, `DateTime` or
`DateTimeOffset` using the invariant culture, parse or convert an enum (`Enum.Parse` ignoring case
for a string, `Enum.ToObject` otherwise), and otherwise call `Convert.ChangeType` with the invariant
culture; a `null` value against a non-nullable value type, or a coercion that the framework rejects
(`InvalidCastException`, `FormatException`, `OverflowException`, `ArgumentException`), SHALL make the
leaf unevaluable — match-nothing, never negated — rather than throwing (TASK-171, TASK-144).

#### Scenario: String value coerced to Guid

- **Given** `new Rule("Guid", ComparisonOperator.Equal, "3f2504e0-4f89-11d3-9a0c-0305e82c3301")` on a `Guid` property
- **When** the predicate is built
- **Then** the constant is the parsed `Guid` and the predicate compares correctly

#### Scenario: String value coerced to an enum member

- **Given** an enum-typed property and a rule `Value` of the string `"active"`
- **When** the predicate is built
- **Then** `Enum.Parse(underlying, "active", ignoreCase: true)` supplies the constant

#### Scenario: Null value against a non-nullable property matches nothing

- **Given** `new Rule("Age", ComparisonOperator.Equal, null)` where `Age` is `int`, negated or not
- **When** `ToExpression<T>` is called
- **Then** no exception is thrown and the predicate selects no rows — the in-memory path evaluates the same rule to a plain non-match

#### Scenario: Uncoercible value matches nothing

- **Given** a rule whose `Value` is `"not-a-number"` against an `int` property, or a malformed Guid text against a `Guid` property
- **When** `ToExpression<T>` is called
- **Then** the conversion exception is caught and the leaf selects no rows, negated or not

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
`Convert` unary node and joining with `'.'` the names of the member chain from the lambda parameter to
the accessed member (SH-M372, TASK-156), SHALL throw `ArgumentException` for any other expression shape
(including a chain that does not start at the parameter), and SHALL read values at validation time
through the compiled delegate for a single member, or for a nested path by walking the chain with
reflection and answering `null` when an intermediate is `null`.

#### Scenario: Simple property access

- **Given** `RuleFor(x => x.Name)`
- **When** the rule is registered
- **Then** `PropertyName` is `"Name"`

#### Scenario: Boxed value type is unwrapped

- **Given** `RuleFor(x => x.Temperature)` where `Temperature` is `double`
- **When** the rule is registered
- **Then** the inserted `Convert` node is unwrapped and `PropertyName` is `"Temperature"`

#### Scenario: Nested path keeps the whole path

- **Given** `RuleFor(x => x.Address.City)`
- **When** the rule is registered
- **Then** `PropertyName` is `"Address.City"` — so `RuleFor(x => x.Wyrm.MaxParallel)` and `RuleFor(x => x.Wyvern.MaxParallel)` report under different keys; both were `"MaxParallel"` before

#### Scenario: Non-member expression rejected

- **Given** `RuleFor(x => x.FirstName + x.LastName)`
- **When** the rule is registered
- **Then** an `ArgumentException` is thrown: `Expression must be a simple property access, got: …`

#### Scenario: Null intermediate on a nested path reads as a null value

- **Given** `RuleFor(x => x.Address.City).Required().MaxLength(3)` and an instance whose `Address` is `null`
- **When** `Validate(instance)` is called
- **Then** no exception is thrown; the rules see `null`, so `Required` reports `REQUIRED` against `"Address.City"` and `MaxLength` passes. The compiled accessor used to throw `NullReferenceException` out of `Validate`

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

### Requirement: Regex rule runs with a match timeout and without implicit compilation

The system SHALL construct a pattern-based `RegexRule` with `RegexOptions.None` by default (not
`Compiled`) and a match timeout of `RegexRule.DefaultMatchTimeout` (one second) unless the caller passes
`matchTimeout`, SHALL use a caller-supplied `Regex` instance as given, and SHALL report a value whose
match times out (`RegexMatchTimeoutException`) as **invalid** rather than propagating the exception
(SH-M376, TASK-156).

#### Scenario: A catastrophically backtracking pattern does not spin the request

- **Given** `new RegexRule("Code", "^(a+)+$", matchTimeout: TimeSpan.FromMilliseconds(50))` and a long value of `a`s ending in `!`
- **When** `IsValid` is called
- **Then** it returns `false` once the timeout elapses, and no exception escapes `Validate`

#### Scenario: Default options

- **Given** `RuleFor(x => x.SerialNumber).Matches("^[A-Z0-9-]+$")`
- **When** the rule is built
- **Then** its regex has `RegexOptions.None` and a one-second match timeout; `RegexOptions.Compiled` is opt-in through the options parameter

### Requirement: Range rule compares numeric bounds by value

The system SHALL evaluate `RangeRule` by calling `IComparable.CompareTo` directly when the property
value and the bound have the same runtime type; when both are numbers of different types it SHALL
compare them by value — as `double` if either is a `double` or `float`, otherwise exactly as `decimal`
(SH-M371, TASK-156); and for any other type mismatch it SHALL call `CompareTo` and rethrow its
`ArgumentException` as an `InvalidOperationException` naming the property and both types — a
misconfigured rule, not an `OUT_OF_RANGE` result.

#### Scenario: int literal bounds on a double property

- **Given** `RuleFor(x => x.Temperature).Range(-50, 150)` where `Temperature` is `double`
- **When** `Validate(instance)` is called for `Temperature == 200d`, and again for `Temperature == 20d`
- **Then** the first reports a single `OUT_OF_RANGE` error and the second is valid; no exception escapes `Validate` (it used to throw `Object must be of type Double.`)

#### Scenario: A non-numeric bound of another type is a configuration error

- **Given** `RuleFor(x => x.Qty).Range("a", "z")` where `Qty` is `int`
- **When** `Validate` is called
- **Then** an `InvalidOperationException` is thrown — `The range of 'Qty' has a String bound, which cannot be compared with its Int32 value.` — wrapping the `CompareTo` `ArgumentException`

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
not of type `T`; SHALL implement `In` (a `params TProp[]` overload, and an `IEnumerable<TProp>` overload
taking an optional message) by rejecting a `null` or empty allowed-value set up front and otherwise
accepting a `null` value or a value that matches `TProp` and is present in the set (SH-M375, TASK-156);
and SHALL implement `NotEqual` through `object.Equals`.

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

#### Scenario: In passes null, like the other rules

- **Given** `RuleFor(x => x.Status).In("A", "B")` and an instance whose `Status` is `null`
- **When** `Validate` runs
- **Then** no error is reported — `In` follows the null-is-valid convention of `EmailRule` / `LengthRule` / `RangeRule` / `RegexRule`; a missing value is `Required`'s to report
- **When** `Status` is `"C"`
- **Then** a `NOT_IN_SET` error is reported with the default message `'Status' must be one of: A, B.`

#### Scenario: In takes a custom message

- **Given** `RuleFor(x => x.Status).In(new[] { "A", "B" }, "Unknown status")` and an instance whose `Status` is `"C"`
- **When** `Validate` runs
- **Then** the `NOT_IN_SET` error's `Message` is `"Unknown status"`

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
and, for the enumerable `CreateAsync` / `UpdateAsync` overloads, materialises the batch once,
validates **every** item, merges all failures into one aggregated `ValidationResult` and throws a single
`ValidationException` only after the whole batch has been checked, passing the materialised list to the
inner store (SH-M374, TASK-156). It SHALL validate both filtered updates (SH-M373, TASK-156): each first
rejects a `null` filter with `ArgumentNullException` and an all-rows filter through
`BoundedFilterGuard` (naming `UpdateAllAsync(updateAction)` as the door); `UpdateAsync(filter, Action<T>)`
reads the matches, applies the action, validates them and writes them as one collection
`UpdateAsync` (writing nothing when nothing matched); `UpdateAsync(filter, PropertyUpdate<T>)` reads the
matches, applies the update to those copies only to validate them, and then runs the inner store's
native filtered update, so a row changed between the preview and the write is validated as it was read.

#### Scenario: Batch failures are aggregated, not reported one at a time

- **Given** a batch of three entities of which two are invalid with one error each
- **When** `CreateAsync(batch)` is awaited
- **Then** a single `ValidationException` is thrown whose `ValidationResult.Errors` contains both errors, and the inner store's bulk `CreateAsync` is not invoked

#### Scenario: Valid batch passes through

- **Given** a batch where every entity validates
- **When** `UpdateAsync(batch)` is awaited
- **Then** the aggregated result is valid and the batch is forwarded to the inner store

#### Scenario: Filter-based updates are validated

- **Given** a bulk wrapper whose validator would reject the mutation's outcome
- **When** `UpdateAsync(filter, updateAction)` or `UpdateAsync(filter, propertyUpdate)` is awaited
- **Then** a `ValidationException` aggregating the affected entities' errors is thrown and nothing is written; before TASK-156 both forwarded straight to the inner store unvalidated

#### Scenario: A valid filtered PropertyUpdate runs natively

- **Given** a bulk wrapper and `UpdateAsync(filter, new PropertyUpdate<T>().Increment(x => x.Count, 1))` whose outcome validates
- **When** it is awaited
- **Then** the preview copies are validated and the inner store's own `UpdateAsync(filter, updates)` performs the write, keeping the increment atomic

#### Scenario: Filtered updates refuse a missing or all-rows filter

- **Given** a bulk wrapper
- **When** `UpdateAsync(null, updateAction)` is awaited
- **Then** an `ArgumentNullException` naming `filter` is thrown
- **When** the filter reduces to every row
- **Then** `BoundedFilterGuard` refuses it with a `WholeTableWriteException` before anything is read

#### Scenario: Deletes and reads are unvalidated

- **Given** a bulk wrapper
- **When** `DeleteAsync(data)`, `DeleteAsync(filter)`, `ReadAsync(...)` or `CountAsync(...)` is awaited
- **Then** each is forwarded to the inner store unchanged

#### Scenario: Batch input is enumerated once

- **Given** a lazily-generated `IEnumerable<T>` passed to `CreateAsync`
- **When** the call is awaited
- **Then** the sequence is materialised once; the entities validated are the entities the inner store writes. A single-pass generator used to be validated and then written empty

### Requirement: No synchronous bulk validating wrapper exists

The system SHALL provide validating decorators for the synchronous single-entity store, the
asynchronous single-entity store and the asynchronous bulk store only; there SHALL be no synchronous
bulk validating wrapper, so a synchronous `IBulkStore<T>` cannot be validation-decorated by this
library.

#### Scenario: Composing validation onto a synchronous bulk store

- **Given** a synchronous `IBulkStore<T>` implementation
- **When** a caller looks for a `ValidatingBulkStoreWrapper<TStore, T>` in `Birko.Validation.Integration`
- **Then** no such type exists; only `ValidatingStoreWrapper`, `AsyncValidatingStoreWrapper` and `AsyncValidatingBulkStoreWrapper` are available, and wrapping the bulk store with `ValidatingStoreWrapper` validates only the single-entity `Create` / `Update` / `Save` surface
