# Birko.Rules

Data-driven rule engine for the Birko Framework. Define business rules as composable, serializable data structures and evaluate them against any data source.

## Features

- **Leaf rules** — single field conditions (`field operator value`)
- **Composite groups** — AND/OR groups with unlimited nesting
- **Rule sets** — named, reusable collections with enable/disable toggle
- **Multiple contexts** — dictionary-based, reflection-based (any object), or custom
- **Rich operators** — Equal, NotEqual, GreaterThan, LessThan, Between, Contains, StartsWith, EndsWith, Like, In, IsNull, and more
- **Severity levels** — Info, Low, Medium, High, Critical
- **Negation support** — any rule can be negated
- **Type-safe comparisons** — numeric promotion, string fallback, null handling
- **Stateless evaluator** — singleton-safe, no side effects
- **LINQ expression converter** — convert rules to `Expression<Func<T, bool>>` for any store (SQL, Elasticsearch, MongoDB, JSON)

## Dependencies

None (standalone project).

## Usage

### Define and evaluate rules

```csharp
using Birko.Rules;

// Create a leaf rule
var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100)
{
    Name = "High Temperature",
    Severity = RuleSeverity.High
};

// Create a context from a dictionary
var context = DictionaryRuleContext.From(
    ("Temperature", 105),
    ("Humidity", 60)
);

// Evaluate
var evaluator = new RuleEvaluator();
var result = evaluator.Evaluate(rule, context);
// result.IsMatch == true, result.Severity == High
```

### Composite rules (AND/OR)

```csharp
var group = RuleGroup.And(
    new Rule("Temperature", ComparisonOperator.GreaterThan, 80),
    new Rule("Humidity", ComparisonOperator.GreaterThan, 70)
);

var result = evaluator.Evaluate(group, context);
```

### Object-based context (reflection)

```csharp
var sensor = new SensorReading { Temperature = 95, Humidity = 80 };
var context = new ObjectRuleContext<SensorReading>(sensor);

var result = evaluator.Evaluate(rule, context);
```

### Rule sets

```csharp
var ruleSet = new RuleSet("Temperature Alarms",
    new Rule("Temperature", ComparisonOperator.GreaterThan, 100)
    {
        Severity = RuleSeverity.Critical
    },
    new Rule("Temperature", ComparisonOperator.Between, 80)
    {
        UpperValue = 100,
        Severity = RuleSeverity.Medium
    }
);

var matches = evaluator.Evaluate(ruleSet, context);
```

### Between and In operators

```csharp
var between = Rule.Between("Temperature", 20, 40);
var inRule = new Rule("Status", ComparisonOperator.In, new[] { "Active", "Pending" });
```

## API Reference

### Core Types
- **IRule** — base interface (Name, Description, Severity, IsEnabled)
- **Rule** — leaf condition (Field, Operator, Value, UpperValue, IsNegated)
- **RuleGroup** — AND/OR composite (Logic, Rules), with `And()` / `Or()` factory methods
- **RuleSet** — named rule collection
- **RuleResult** — evaluation outcome (IsMatch, Rule, Severity, ActualValue, Metadata)

### Enums
- **ComparisonOperator** — Equal, NotEqual, GreaterThan, LessThan, Between, Contains, Like, In, IsNull, etc.
- **LogicOperator** — And, Or
- **RuleSeverity** — Info, Low, Medium, High, Critical

### Contexts
- **IRuleContext** — TryGetValue / HasField interface
- **DictionaryRuleContext** — dictionary-backed context with `From()` builder
- **ObjectRuleContext\<T\>** — reflection-based context with property caching

### Evaluation
- **IRuleEvaluator** — Evaluate, EvaluateAll, EvaluateMatches
- **RuleEvaluator** — default stateless implementation

### Expressions
- **RuleExpressionConverter** — static converter: rules → `Expression<Func<T, bool>>` for LINQ-based stores

## LINQ Expression Conversion

Convert data-driven rules into LINQ expressions that any Birko store accepts:

```csharp
using Birko.Rules;

// Single rule → expression
var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m);
var expr = RuleExpressionConverter.ToExpression<Product>(rule);
var results = store.ReadList(expr); // Works with SQL, ES, MongoDB, JSON...

// RuleSet → combined AND expression
var ruleSet = new RuleSet("Active expensive products",
    new Rule("IsActive", ComparisonOperator.Equal, true),
    new Rule("Price", ComparisonOperator.GreaterThan, 50m));
var expr = RuleExpressionConverter.ToExpression<Product>(ruleSet);

// Nested properties (null-safe)
var rule = new Rule("Address.City", ComparisonOperator.Equal, "Prague");
// Generates: x => x.Address != null && x.Address.City == "Prague"

// Automatic value conversion
var rule = new Rule("CreatedAt", ComparisonOperator.GreaterThan, "2025-01-01"); // string → DateTime
var rule = new Rule("Id", ComparisonOperator.Equal, "abc-123-..."); // string → Guid
```

Supports all 16 comparison operators, nested AND/OR groups, negation, disabled rule filtering, case-insensitive property resolution, and type coercion.

## License

Part of the Birko Framework.
