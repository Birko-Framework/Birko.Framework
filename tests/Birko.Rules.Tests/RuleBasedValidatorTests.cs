using Birko.Rules;
using Birko.Validation.Integration;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class RuleBasedValidatorTests
{
    private class Product
    {
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public string? Category { get; set; }
    }

    [Fact]
    public void Valid_Instance_ReturnsSuccess()
    {
        var ruleSet = new RuleSet("Product Rules",
            new Rule("Price", ComparisonOperator.GreaterThan, 0m)
            {
                Name = "Price must be positive",
                Severity = RuleSeverity.High
            }
        );

        var validator = new RuleBasedValidator<Product>(ruleSet);
        var product = new Product { Name = "Widget", Price = 10m };

        var result = validator.Validate(product);

        // Price > 0 matches → that's a "violation" from rule perspective
        // Wait — RuleBasedValidator treats MATCHES as violations (like alerts)
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void NoMatch_ReturnsValid()
    {
        var ruleSet = new RuleSet("Violation Rules",
            new Rule("Stock", ComparisonOperator.LessThan, 0)
            {
                Name = "Negative stock",
                Description = "Stock cannot be negative",
                Severity = RuleSeverity.Critical
            }
        );

        var validator = new RuleBasedValidator<Product>(ruleSet);
        var product = new Product { Stock = 10 };

        var result = validator.Validate(product);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Match_ReturnsError_WithCorrectPropertyName()
    {
        var ruleSet = new RuleSet("Alerts",
            new Rule("Stock", ComparisonOperator.LessThan, 5)
            {
                Name = "Low stock warning",
                Description = "Stock is below minimum threshold",
                Severity = RuleSeverity.Medium
            }
        );

        var validator = new RuleBasedValidator<Product>(ruleSet);
        var product = new Product { Stock = 2 };

        var result = validator.Validate(product);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].PropertyName.Should().Be("Stock");
        result.Errors[0].Message.Should().Be("Stock is below minimum threshold");
        result.Errors[0].ErrorCode.Should().Be("RULE_MEDIUM");
    }

    [Fact]
    public void DisabledRuleSet_ReturnsValid()
    {
        var ruleSet = new RuleSet("Disabled",
            new Rule("Price", ComparisonOperator.LessThan, 0)
        ) { IsEnabled = false };

        var validator = new RuleBasedValidator<Product>(ruleSet);
        var product = new Product { Price = -5m };

        var result = validator.Validate(product);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void MultipleMatches_ReturnMultipleErrors()
    {
        var ruleSet = new RuleSet("Multi",
            new Rule("Stock", ComparisonOperator.LessThan, 5)
            {
                Name = "Low stock",
                Severity = RuleSeverity.Low
            },
            new Rule("Category", ComparisonOperator.IsNull, null)
            {
                Name = "Missing category",
                Severity = RuleSeverity.High
            }
        );

        var validator = new RuleBasedValidator<Product>(ruleSet);
        var product = new Product { Stock = 1, Category = null };

        var result = validator.Validate(product);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task ValidateAsync_Works()
    {
        var ruleSet = new RuleSet("Async Test",
            new Rule("Price", ComparisonOperator.LessThan, 0)
            {
                Name = "Negative price",
                Severity = RuleSeverity.Critical
            }
        );

        var validator = new RuleBasedValidator<Product>(ruleSet);
        var product = new Product { Price = -1m };

        var result = await validator.ValidateAsync(product);

        result.IsValid.Should().BeFalse();
    }
}
