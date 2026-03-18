# Birko.Rules.Tests

Unit tests for the Birko.Rules data-driven rule engine.

## Test Framework

- xUnit 2.9.3
- FluentAssertions 7.0.0

## Running Tests

```bash
dotnet test
```

## Test Coverage

- Core types (Rule, RuleGroup, RuleSet, RuleResult)
- Contexts (DictionaryRuleContext, ObjectRuleContext)
- Evaluator (all comparison operators, groups, nesting, negation, severity, disabled rules)
- Expression converter (all operators, value coercion, nested properties, null guards, groups, RuleSet, disabled rules)

## License

Part of the Birko Framework.
