# Rules Engine Guide

## Overview

Birko.Rules is a data-driven rule engine for the Birko Framework. It allows you to define business rules as composable, serializable data structures and evaluate them against any data source.

Unlike Birko.Validation (which validates model properties with fluent builder syntax), Birko.Rules evaluates arbitrary field-based conditions against dynamic data contexts — useful for IoT alerts, business logic, filtering, access control, and more.

## Core Concepts

### Rules (IRule)

Every evaluation unit implements `IRule`:

```csharp
public interface IRule
{
    string? Name { get; }
    string? Description { get; }
    RuleSeverity Severity { get; }
    bool IsEnabled { get; }
}
```

There are two implementations:
- **Rule** — a leaf condition: `field operator value`
- **RuleGroup** — a composite: AND/OR group of child rules (nestable)

### Leaf Rules

A `Rule` represents a single field condition:

```csharp
// Temperature > 100
var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100)
{
    Name = "High Temperature",
    Severity = RuleSeverity.Critical
};

// Price between 10 and 50
var between = Rule.Between("Price", 10, 50);

// Status in [Active, Pending]
var inRule = new Rule("Status", ComparisonOperator.In, new[] { "Active", "Pending" });

// Negation: NOT (Temperature > 100)
var negated = new Rule("Temperature", ComparisonOperator.GreaterThan, 100)
{
    IsNegated = true
};
```

### Comparison Operators

| Operator | Description |
|----------|-------------|
| Equal | Exact match (numeric promotion, case-insensitive strings) |
| NotEqual | Not equal |
| GreaterThan | Greater than (numeric, DateTime, IComparable) |
| GreaterThanOrEqual | Greater than or equal |
| LessThan | Less than |
| LessThanOrEqual | Less than or equal |
| Between | Inclusive range (Value <= field <= UpperValue) |
| IsNull | Field is null or missing |
| IsNotNull | Field exists and is not null |
| Contains | String contains substring |
| NotContains | String does not contain substring |
| StartsWith | String starts with prefix |
| EndsWith | String ends with suffix |
| Like | SQL-style LIKE with % wildcards |
| In | Value is in a collection |
| NotIn | Value is not in a collection |

### Rule Groups

Combine rules with AND/OR logic:

```csharp
// All must match
var andGroup = RuleGroup.And(
    new Rule("Temperature", ComparisonOperator.GreaterThan, 80),
    new Rule("Humidity", ComparisonOperator.GreaterThan, 70)
);

// Any must match
var orGroup = RuleGroup.Or(
    new Rule("Status", ComparisonOperator.Equal, "Critical"),
    new Rule("Temperature", ComparisonOperator.GreaterThan, 150)
);

// Nested: (A AND B) OR C
var nested = RuleGroup.Or(
    RuleGroup.And(rule1, rule2),
    rule3
);
```

### Rule Sets

Named, reusable collections with enable/disable toggle:

```csharp
var alarms = new RuleSet("Temperature Alarms",
    new Rule("Temperature", ComparisonOperator.GreaterThan, 100)
    {
        Severity = RuleSeverity.Critical
    },
    Rule.Between("Temperature", 80, 100)
);
```

### Severity Levels

Rules carry a severity level: `Info`, `Low`, `Medium`, `High`, `Critical`. Consumers interpret the meaning.

## Contexts

A context provides field values for rule evaluation via `IRuleContext`:

```csharp
public interface IRuleContext
{
    bool TryGetValue(string field, out object? value);
    bool HasField(string field);
}
```

### DictionaryRuleContext

Backed by `Dictionary<string, object?>`:

```csharp
var ctx = DictionaryRuleContext.From(
    ("Temperature", 95),
    ("Humidity", 80),
    ("Status", "Active")
);
```

### ObjectRuleContext<T>

Reads property values from any object via reflection (cached per type):

```csharp
var sensor = new SensorReading { Temperature = 95, Humidity = 80 };
var ctx = new ObjectRuleContext<SensorReading>(sensor);
```

Property lookup is case-insensitive.

### Custom Context

Implement `IRuleContext` for custom data sources (database rows, JSON documents, etc.).

## Evaluation

### RuleEvaluator

The default evaluator is stateless and singleton-safe:

```csharp
var evaluator = new RuleEvaluator();

// Single rule
RuleResult result = evaluator.Evaluate(rule, context);

// All rules (including non-matches)
IReadOnlyList<RuleResult> all = evaluator.EvaluateAll(rules, context);

// Only matches
IReadOnlyList<RuleResult> matches = evaluator.EvaluateMatches(rules, context);

// RuleSet (respects IsEnabled)
IReadOnlyList<RuleResult> setResults = evaluator.Evaluate(ruleSet, context);
```

### RuleResult

```csharp
result.IsMatch      // true if condition was satisfied
result.Rule         // the rule that was evaluated
result.Severity     // severity from the rule
result.ActualValue  // the field value that was evaluated
result.Metadata     // optional metadata
```

## Type Handling

ComparisonHelper handles type-safe comparisons:
- **Numeric promotion**: int, long, float, double, decimal, short, byte all compare correctly across types
- **DateTime**: direct comparison
- **IComparable**: fallback for custom types
- **String**: case-insensitive comparison as last resort
- **Null safety**: null checks for all operators

## LINQ Expression Conversion

`RuleExpressionConverter` bridges rules and stores — converts any `IRule`, `RuleGroup`, `RuleSet`, or `IEnumerable<IRule>` into an `Expression<Func<T, bool>>` that all Birko stores accept (SQL, Elasticsearch, MongoDB, JSON, etc.).

### Basic Usage

```csharp
using Birko.Rules;

// Single rule → LINQ expression
var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m);
var expr = RuleExpressionConverter.ToExpression<Product>(rule);
var results = store.ReadList(expr); // Works with any store

// RuleSet → AND-combined expression
var ruleSet = new RuleSet("Active expensive",
    new Rule("IsActive", ComparisonOperator.Equal, true),
    new Rule("Price", ComparisonOperator.GreaterThan, 50m));
var expr = RuleExpressionConverter.ToExpression<Product>(ruleSet);
```

### Nested Properties

Nested property paths are null-safe:

```csharp
var rule = new Rule("Address.City", ComparisonOperator.Equal, "Prague");
// Generates: x => x.Address != null && x.Address.City == "Prague"
```

### Value Conversion

Values are automatically converted to match the target property type:

```csharp
new Rule("CreatedAt", ComparisonOperator.GreaterThan, "2025-01-01") // string → DateTime
new Rule("Id", ComparisonOperator.Equal, "abc-def-...")              // string → Guid
new Rule("Price", ComparisonOperator.GreaterThan, 10)                // int → decimal
new Rule("Status", ComparisonOperator.Equal, "Active")               // string → enum
```

### Features

- All 16 comparison operators supported
- AND/OR groups with arbitrary nesting
- Negation (IsNegated) on rules and groups
- Disabled rules/groups skipped (returns null)
- Case-insensitive property resolution and string comparisons
- SQL LIKE-style wildcards (%, StartsWith, EndsWith, Contains)
- In/NotIn with collections

### Rules vs SQL Conditions

There are two conversion paths from rules:

| Converter | Output | Use Case |
|-----------|--------|----------|
| `RuleExpressionConverter.ToExpression<T>()` | `Expression<Func<T, bool>>` | Any store (SQL, ES, MongoDB, JSON) |
| `RuleConditionConverter.ToConditions()` | SQL `Condition` tree | SQL-specific (direct WHERE clause) |

The expression converter is the universal path — use it unless you need SQL-specific optimizations.

## Use Cases

- **IoT alerts**: evaluate sensor readings against threshold rules
- **Business rules**: apply pricing, discount, or eligibility rules to data
- **Dynamic filtering**: build query filters from user-defined rules (now with LINQ expression output for any store)
- **Access control**: evaluate permissions based on context attributes
- **Monitoring**: check system metrics against alert thresholds
