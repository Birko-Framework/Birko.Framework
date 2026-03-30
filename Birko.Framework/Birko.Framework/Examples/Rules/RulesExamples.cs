using Birko.Rules;
using Birko.Data.SQL.Conditions;
using Birko.Data.Patterns.Specification;
using Birko.Validation.Integration;

namespace Birko.Framework.Examples.Rules;

public static class RulesExamples
{
    // ── Example model for demos ──
    private class SensorReading
    {
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Location { get; set; }
    }

    private class Product
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public string? Category { get; set; }
    }

    // ────────────────────────────────────────────────────────
    //  Basic Rule Evaluation
    // ────────────────────────────────────────────────────────

    public static void RunBasicRulesExample()
    {
        ExampleOutput.WriteHeader("Basic Rule Evaluation");

        var evaluator = new RuleEvaluator();

        // Simple leaf rule
        var highTemp = new Rule("Temperature", ComparisonOperator.GreaterThan, 100.0)
        {
            Name = "High Temperature Alert",
            Severity = RuleSeverity.Critical
        };

        var context = DictionaryRuleContext.From(
            ("Temperature", (object?)105.0),
            ("Humidity", (object?)60.0),
            ("Status", (object?)"Active")
        );

        var result = evaluator.Evaluate(highTemp, context);
        ExampleOutput.WriteInfo("Rule", highTemp.Name!);
        ExampleOutput.WriteInfo("Match", result.IsMatch.ToString());
        ExampleOutput.WriteInfo("Severity", result.Severity.ToString());
        ExampleOutput.WriteInfo("Actual Value", result.ActualValue?.ToString() ?? "null");

        ExampleOutput.WriteLine();

        // Between rule
        var normalRange = Rule.Between("Temperature", 20.0, 40.0);
        normalRange.Name = "Normal Range Check";
        var normalResult = evaluator.Evaluate(normalRange, context);
        ExampleOutput.WriteInfo("Rule", normalRange.Name);
        ExampleOutput.WriteInfo("Match", normalResult.IsMatch.ToString());
        ExampleOutput.WriteDim("105°C is outside 20-40 range");

        ExampleOutput.WriteLine();

        // String operations
        var containsRule = new Rule("Status", ComparisonOperator.Contains, "act")
        {
            Name = "Status Contains 'act'"
        };
        var stringResult = evaluator.Evaluate(containsRule, context);
        ExampleOutput.WriteInfo("Rule", containsRule.Name!);
        ExampleOutput.WriteInfo("Match", stringResult.IsMatch.ToString());
        ExampleOutput.WriteDim("'Active' contains 'act' (case-insensitive)");
    }

    // ────────────────────────────────────────────────────────
    //  Groups and Nesting
    // ────────────────────────────────────────────────────────

    public static void RunGroupsExample()
    {
        ExampleOutput.WriteHeader("Rule Groups (AND/OR/Nested)");

        var evaluator = new RuleEvaluator();
        var context = DictionaryRuleContext.From(
            ("Temperature", (object?)95.0),
            ("Humidity", (object?)85.0),
            ("Status", (object?)"Active")
        );

        // AND group
        var andGroup = RuleGroup.And(
            new Rule("Temperature", ComparisonOperator.GreaterThan, 80.0),
            new Rule("Humidity", ComparisonOperator.GreaterThan, 70.0)
        );
        andGroup.Name = "Hot & Humid";

        var andResult = evaluator.Evaluate(andGroup, context);
        ExampleOutput.WriteInfo("AND Group", $"{andGroup.Name} → {andResult.IsMatch}");
        ExampleOutput.WriteDim("Both Temperature > 80 AND Humidity > 70 must match");

        ExampleOutput.WriteLine();

        // OR group
        var orGroup = RuleGroup.Or(
            new Rule("Temperature", ComparisonOperator.GreaterThan, 100.0),
            new Rule("Status", ComparisonOperator.Equal, "Active")
        );
        orGroup.Name = "Critical or Active";

        var orResult = evaluator.Evaluate(orGroup, context);
        ExampleOutput.WriteInfo("OR Group", $"{orGroup.Name} → {orResult.IsMatch}");
        ExampleOutput.WriteDim("Temperature > 100 is false, but Status == Active is true");

        ExampleOutput.WriteLine();

        // Nested: (A AND B) OR C
        var nested = RuleGroup.Or(
            RuleGroup.And(
                new Rule("Temperature", ComparisonOperator.GreaterThan, 100.0),
                new Rule("Humidity", ComparisonOperator.GreaterThan, 90.0)
            ),
            new Rule("Status", ComparisonOperator.Equal, "Active")
        );
        nested.Name = "Nested: (Hot AND Very Humid) OR Active";

        var nestedResult = evaluator.Evaluate(nested, context);
        ExampleOutput.WriteInfo("Nested", $"{nested.Name} → {nestedResult.IsMatch}");
        ExampleOutput.WriteDim("Inner AND fails, but OR with Active succeeds");
    }

    // ────────────────────────────────────────────────────────
    //  Object Context (Reflection)
    // ────────────────────────────────────────────────────────

    public static void RunObjectContextExample()
    {
        ExampleOutput.WriteHeader("Object Context (Reflection-Based)");

        var evaluator = new RuleEvaluator();

        var reading = new SensorReading
        {
            Temperature = 75.5,
            Humidity = 45.0,
            Status = "Online",
            Location = "Warehouse-A"
        };

        var context = new ObjectRuleContext<SensorReading>(reading);

        var rules = new IRule[]
        {
            new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0)
            {
                Name = "Warm",
                Severity = RuleSeverity.Low
            },
            new Rule("Humidity", ComparisonOperator.LessThan, 30.0)
            {
                Name = "Low Humidity",
                Severity = RuleSeverity.Medium
            },
            new Rule("Location", ComparisonOperator.StartsWith, "Warehouse")
            {
                Name = "Warehouse Sensor",
                Severity = RuleSeverity.Info
            }
        };

        ExampleOutput.WriteInfo("Object", $"Temp={reading.Temperature}, Humidity={reading.Humidity}, Status={reading.Status}, Location={reading.Location}");
        ExampleOutput.WriteLine();

        var matches = evaluator.EvaluateMatches(rules, context);
        ExampleOutput.WriteInfo("Matches", $"{matches.Count} of {rules.Length} rules");
        foreach (var match in matches)
        {
            ExampleOutput.WriteSuccess($"{match.Rule.Name} (severity: {match.Severity}, value: {match.ActualValue})");
        }
    }

    // ────────────────────────────────────────────────────────
    //  SQL Condition Converter
    // ────────────────────────────────────────────────────────

    public static void RunSqlConverterExample()
    {
        ExampleOutput.WriteHeader("Rule → SQL Condition Converter");

        // Single rule
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m)
        {
            Name = "Expensive Products"
        };

        var conditions = RuleConditionConverter.ToConditions(rule).ToList();
        ExampleOutput.WriteInfo("Rule", $"{rule.Field} {rule.Operator} {rule.Value}");
        ExampleOutput.WriteInfo("SQL Condition", $"Type={conditions[0].Type}, IsNot={conditions[0].IsNot}");
        ExampleOutput.WriteDim("→ Price > @param");

        ExampleOutput.WriteLine();

        // RuleSet → multiple conditions
        var ruleSet = new RuleSet("Product Filters",
            new Rule("Status", ComparisonOperator.NotEqual, "Deleted"),
            new Rule("Stock", ComparisonOperator.GreaterThan, 0),
            new Rule("Category", ComparisonOperator.In, new[] { "Electronics", "Books" })
        );

        var allConditions = RuleConditionConverter.ToConditions(ruleSet).ToList();
        ExampleOutput.WriteInfo("RuleSet", $"'{ruleSet.Name}' → {allConditions.Count} SQL conditions");
        foreach (var c in allConditions)
        {
            ExampleOutput.WriteSuccess($"{c.Name}: Type={c.Type}, IsNot={c.IsNot}");
        }
        ExampleOutput.WriteDim("→ Status <> 'Deleted' AND Stock > 0 AND Category IN ('Electronics', 'Books')");

        ExampleOutput.WriteLine();

        // OR group
        var orGroup = RuleGroup.Or(
            new Rule("Price", ComparisonOperator.LessThan, 10m),
            new Rule("Category", ComparisonOperator.Equal, "Clearance")
        );
        var orConditions = RuleConditionConverter.ToConditions(orGroup).ToList();
        ExampleOutput.WriteInfo("OR Group", $"{orConditions.Count} condition with SubConditions");
        ExampleOutput.WriteDim("→ (Price < 10 OR Category = 'Clearance')");
    }

    // ────────────────────────────────────────────────────────
    //  Specification Pattern Integration
    // ────────────────────────────────────────────────────────

    public static void RunSpecificationExample()
    {
        ExampleOutput.WriteHeader("Rule → Specification Pattern");

        // Create a rule-based specification
        var tempRule = new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0);
        var spec = new RuleSpecification<SensorReading>(tempRule);

        var readings = new[]
        {
            new SensorReading { Temperature = 80, Humidity = 60, Status = "Active" },
            new SensorReading { Temperature = 30, Humidity = 80, Status = "Active" },
            new SensorReading { Temperature = 95, Humidity = 40, Status = "Inactive" },
            new SensorReading { Temperature = 20, Humidity = 70, Status = "Active" },
        };

        // In-memory filtering via IsSatisfiedBy
        ExampleOutput.WriteInfo("Spec", "Temperature > 50 (via RuleEvaluator)");
        ExampleOutput.WriteLine();

        foreach (var r in readings)
        {
            var satisfied = spec.IsSatisfiedBy(r);
            var label = satisfied ? "MATCH" : "skip";
            if (satisfied)
                ExampleOutput.WriteSuccess($"{label}: Temp={r.Temperature}, Humidity={r.Humidity}");
            else
                ExampleOutput.WriteDim($"{label}: Temp={r.Temperature}, Humidity={r.Humidity}");
        }

        ExampleOutput.WriteLine();

        // LINQ expression (for store queries)
        var expression = spec.ToExpression();
        var filtered = readings.AsQueryable().Where(expression).ToList();
        ExampleOutput.WriteInfo("LINQ Filtered", $"{filtered.Count} of {readings.Length} readings match");

        ExampleOutput.WriteLine();

        // Composing specifications
        var humidSpec = new RuleSpecification<SensorReading>(
            new Rule("Humidity", ComparisonOperator.GreaterThan, 50.0));
        var combined = spec.And(humidSpec);

        var combinedCount = readings.Count(r => combined.IsSatisfiedBy(r));
        ExampleOutput.WriteInfo("Combined", $"Temp > 50 AND Humidity > 50 → {combinedCount} matches");
    }

    // ────────────────────────────────────────────────────────
    //  Validation Integration
    // ────────────────────────────────────────────────────────

    public static void RunValidationExample()
    {
        ExampleOutput.WriteHeader("Rule-Based Validation");

        // Define violation rules (matches = errors)
        var ruleSet = new RuleSet("Product Violations",
            new Rule("Stock", ComparisonOperator.LessThan, 0)
            {
                Name = "Negative Stock",
                Description = "Stock quantity cannot be negative",
                Severity = RuleSeverity.Critical
            },
            new Rule("Price", ComparisonOperator.LessThanOrEqual, 0m)
            {
                Name = "Invalid Price",
                Description = "Price must be greater than zero",
                Severity = RuleSeverity.High
            },
            new Rule("Name", ComparisonOperator.Equal, "")
            {
                Name = "Missing Name",
                Description = "Product name is required",
                Severity = RuleSeverity.High
            }
        );

        var validator = new RuleBasedValidator<Product>(ruleSet);

        // Valid product
        var good = new Product { Name = "Widget", Price = 29.99m, Stock = 100 };
        var goodResult = validator.Validate(good);
        ExampleOutput.WriteInfo("Product", $"Name={good.Name}, Price={good.Price}, Stock={good.Stock}");
        ExampleOutput.WriteSuccess($"Valid: {goodResult.IsValid} ({goodResult.Errors.Count} violations)");

        ExampleOutput.WriteLine();

        // Invalid product
        var bad = new Product { Name = "", Price = -5m, Stock = -10 };
        var badResult = validator.Validate(bad);
        ExampleOutput.WriteInfo("Product", $"Name='{bad.Name}', Price={bad.Price}, Stock={bad.Stock}");
        ExampleOutput.WriteError($"Valid: {badResult.IsValid} ({badResult.Errors.Count} violations)");
        foreach (var error in badResult.Errors)
        {
            ExampleOutput.WriteError($"  [{error.ErrorCode}] {error.PropertyName}: {error.Message}");
        }

        ExampleOutput.WriteLine();
        ExampleOutput.WriteDim("RuleBasedValidator evaluates rules via ObjectRuleContext<T> reflection");
        ExampleOutput.WriteDim("Matches = violations. Rules can be loaded from DB/config at runtime.");
    }

    // ────────────────────────────────────────────────────────
    //  RuleSet Management
    // ────────────────────────────────────────────────────────

    public static void RunRuleSetExample()
    {
        ExampleOutput.WriteHeader("RuleSet Management");

        var evaluator = new RuleEvaluator();

        var alarms = new RuleSet("IoT Alarms",
            new Rule("Temperature", ComparisonOperator.GreaterThan, 100.0)
            {
                Name = "Critical Heat",
                Severity = RuleSeverity.Critical
            },
            new Rule("Temperature", ComparisonOperator.Between, 80.0)
            {
                UpperValue = 100.0,
                Name = "Warning Heat",
                Severity = RuleSeverity.Medium
            },
            new Rule("Humidity", ComparisonOperator.GreaterThan, 90.0)
            {
                Name = "High Humidity",
                Severity = RuleSeverity.Low
            }
        );

        var ctx = DictionaryRuleContext.From(
            ("Temperature", (object?)92.0),
            ("Humidity", (object?)95.0)
        );

        ExampleOutput.WriteInfo("RuleSet", $"'{alarms.Name}' ({alarms.Rules.Count} rules)");
        ExampleOutput.WriteInfo("Data", "Temperature=92, Humidity=95");
        ExampleOutput.WriteLine();

        var matches = evaluator.Evaluate(alarms, ctx);
        ExampleOutput.WriteInfo("Matches", $"{matches.Count} rules triggered");
        foreach (var match in matches)
        {
            var icon = match.Severity switch
            {
                RuleSeverity.Critical => "CRIT",
                RuleSeverity.High => "HIGH",
                RuleSeverity.Medium => "MED ",
                RuleSeverity.Low => "LOW ",
                _ => "INFO"
            };
            ExampleOutput.WriteWarning($"[{icon}] {match.Rule.Name} (actual: {match.ActualValue})");
        }

        ExampleOutput.WriteLine();

        // Disable the set
        alarms.IsEnabled = false;
        var disabled = evaluator.Evaluate(alarms, ctx);
        ExampleOutput.WriteInfo("After Disable", $"{disabled.Count} matches (set disabled)");
    }
}
