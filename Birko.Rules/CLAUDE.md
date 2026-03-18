# Birko.Rules

## Overview
Data-driven rule engine. Define business rules as composable, serializable data structures and evaluate them against any data source (dictionaries, objects via reflection, custom contexts).

## Structure
```
Birko.Rules/
├── Core/
│   ├── ComparisonOperator.cs    - Enum: Equal, NotEqual, GT, GTE, LT, LTE, Between, IsNull, IsNotNull, Contains, NotContains, StartsWith, EndsWith, Like, In, NotIn
│   ├── LogicOperator.cs         - Enum: And, Or
│   ├── RuleSeverity.cs          - Enum: Info, Low, Medium, High, Critical
│   ├── IRule.cs                 - Interface: Name, Description, Severity, IsEnabled
│   ├── Rule.cs                  - Leaf rule: Field + Operator + Value (+ UpperValue for Between, IsNegated)
│   ├── RuleGroup.cs             - Composite: LogicOperator + List<IRule>, static And()/Or() factories
│   ├── RuleSet.cs               - Named collection with enable/disable toggle
│   └── RuleResult.cs            - Evaluation outcome: IsMatch, Rule, Severity, ActualValue, Metadata
├── Context/
│   ├── IRuleContext.cs           - Interface: TryGetValue(field, out value), HasField(field)
│   ├── DictionaryRuleContext.cs  - Dictionary<string, object?> context with From() builder
│   └── ObjectRuleContext.cs      - Generic reflection-based context with ConcurrentDictionary property cache
├── Evaluation/
│   ├── IRuleEvaluator.cs         - Interface: Evaluate, EvaluateAll, EvaluateMatches, Evaluate(RuleSet)
│   ├── ComparisonHelper.cs       - Internal: type-safe comparison with numeric promotion, string fallback, LIKE patterns
│   └── RuleEvaluator.cs          - Default stateless evaluator (leaf + group evaluation, respects IsEnabled/IsNegated)
└── Expressions/
    └── RuleExpressionConverter.cs - Static converter: IRule/RuleSet/IEnumerable<IRule> → Expression<Func<T, bool>> for any LINQ-based store
```

## Dependencies
- None (standalone project)

## Key Design Decisions
- **Namespace:** All types in `Birko.Rules` (flat namespace, no sub-namespaces)
- **ComparisonOperator** superset of `Birko.Data.SQL.ConditionType` — SQL layer can map from these
- **Rule** aligned with `Birko.Data.SQL.Condition` structure but DB-independent
- **RuleGroup** aligned with `Birko.Data.SQL.Condition.SubConditions + IsOr` pattern
- **ComparisonHelper** is `internal static` — type-safe comparisons with numeric promotion (int/long/float/double/decimal), DateTime, IComparable, string fallback
- **ObjectRuleContext\<T\>** caches PropertyInfo per type via ConcurrentDictionary (case-insensitive)
- **RuleEvaluator** is stateless, singleton-safe
- **RuleExpressionConverter** is `static` — converts rules to `Expression<Func<T, bool>>` for use with any LINQ-based store (SQL, Elasticsearch, MongoDB, JSON). Supports nested properties with null guards, automatic value conversion (int→decimal, string→DateTime/Guid/enum), case-insensitive property resolution and string comparisons

## Conventions
- Namespace: `Birko.Rules`
- All rule types implement `IRule`
- Factory methods preferred: `RuleGroup.And()`, `RuleGroup.Or()`, `Rule.Between()`, `DictionaryRuleContext.From()`
- `RuleResult` uses static factory methods: `Match()`, `NoMatch()`

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly.

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect new or renamed files, changed architecture, dependencies, or conventions.

### Test Requirements
Every new public functionality must have corresponding unit tests in Birko.Rules.Tests.
