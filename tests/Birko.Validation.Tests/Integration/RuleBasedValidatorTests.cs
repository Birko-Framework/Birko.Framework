using Birko.Rules;
using Birko.Validation.Integration;
using FluentAssertions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Validation.Tests.Integration;

/// <summary>
/// CR-L390: RuleBasedValidator&lt;T&gt; (both constructors, the "matched rule == violation" Validate path,
/// and the ExtractError property/code/message derivation) had no tests. These pin down the non-obvious
/// semantics: a rule that MATCHES the instance is reported as a violation, and the error code is
/// RULE_{Severity}.
/// </summary>
public class RuleBasedValidatorTests
{
    private sealed class Product
    {
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }

    [Fact]
    public void Validate_MatchingRule_ProducesErrorWithFieldAndSeverityCode()
    {
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m) { Severity = RuleSeverity.High };
        var validator = new RuleBasedValidator<Product>("prices", null, rule);

        var result = validator.Validate(new Product { Price = 150m });

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Single();
        error.PropertyName.Should().Be("Price");   // leaf Rule.Field
        error.ErrorCode.Should().Be("RULE_HIGH");  // RULE_{Severity}
    }

    [Fact]
    public void Validate_NonMatchingRule_ProducesNoError()
    {
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m) { Severity = RuleSeverity.High };
        var validator = new RuleBasedValidator<Product>("prices", null, rule);

        var result = validator.Validate(new Product { Price = 50m });

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ExtractError_PrefersDescriptionForMessage()
    {
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m)
        {
            Severity = RuleSeverity.Medium,
            Name = "PriceCap",
            Description = "Price is over the allowed cap."
        };
        var validator = new RuleBasedValidator<Product>("prices", null, rule);

        var result = validator.Validate(new Product { Price = 150m });

        result.Errors.Single().Message.Should().Be("Price is over the allowed cap.");
    }

    [Fact]
    public void Validate_ExtractError_FallsBackToNameWhenNoDescription()
    {
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m)
        {
            Severity = RuleSeverity.Medium,
            Name = "PriceCap"
        };
        var validator = new RuleBasedValidator<Product>("prices", null, rule);

        var result = validator.Validate(new Product { Price = 150m });

        result.Errors.Single().Message.Should().Be("PriceCap");
    }

    [Fact]
    public void Validate_ViaRuleSetConstructor_Works()
    {
        var ruleSet = new RuleSet("prices", new Rule("Stock", ComparisonOperator.LessThan, 0));
        var validator = new RuleBasedValidator<Product>(ruleSet);

        var negative = validator.Validate(new Product { Stock = -5 });
        var ok = validator.Validate(new Product { Stock = 3 });

        negative.IsValid.Should().BeFalse();
        negative.Errors.Single().PropertyName.Should().Be("Stock");
        ok.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_MatchesSyncResult()
    {
        var rule = new Rule("Price", ComparisonOperator.GreaterThan, 100m) { Severity = RuleSeverity.High };
        var validator = new RuleBasedValidator<Product>("prices", null, rule);

        var result = await validator.ValidateAsync(new Product { Price = 150m });

        result.IsValid.Should().BeFalse();
        result.Errors.Single().ErrorCode.Should().Be("RULE_HIGH");
    }
}
