# Birko.Rules.Tests

## Overview
Unit tests for the Birko.Rules project — data-driven rule engine.

## Project Location
- `C:\Source\Birko.Rules.Tests\`

## Dependencies
- **Birko.Rules** (shared project via .projitems)
- xUnit 2.9.3
- FluentAssertions 7.0.0

## Test Classes
- **RuleTests** — Core types: Rule, RuleGroup, RuleSet, RuleResult construction and defaults
- **ContextTests** — DictionaryRuleContext and ObjectRuleContext (TryGetValue, HasField, null handling, case-insensitivity)
- **RuleEvaluatorTests** — Full evaluator coverage: all comparison operators, negation, disabled rules, AND/OR groups, nested groups, RuleSet evaluation, EvaluateAll/EvaluateMatches

## Running Tests
```bash
dotnet test Birko.Rules.Tests/Birko.Rules.Tests.csproj
```

## Maintenance
When adding new functionality to Birko.Rules, add corresponding tests here.
