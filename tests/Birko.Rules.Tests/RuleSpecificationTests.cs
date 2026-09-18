using Birko.Data.Patterns.Specification;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class RuleSpecificationTests
{
    private class SensorReading
    {
        public double Temperature { get; set; }
        public double Humidity { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    [Fact]
    public void IsSatisfiedBy_MatchingEntity_ReturnsTrue()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0);
        var spec = new RuleSpecification<SensorReading>(rule);

        var reading = new SensorReading { Temperature = 75 };

        spec.IsSatisfiedBy(reading).Should().BeTrue();
    }

    [Fact]
    public void IsSatisfiedBy_NonMatchingEntity_ReturnsFalse()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 100.0);
        var spec = new RuleSpecification<SensorReading>(rule);

        var reading = new SensorReading { Temperature = 50 };

        spec.IsSatisfiedBy(reading).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_Equal_CompilesAndWorks()
    {
        var rule = new Rule("Status", ComparisonOperator.Equal, "Active");
        var spec = new RuleSpecification<SensorReading>(rule);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Status = "Active" }).Should().BeTrue();
        expr(new SensorReading { Status = "Inactive" }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_GreaterThan_Works()
    {
        var rule = new Rule("Temperature", ComparisonOperator.GreaterThan, 80.0);
        var spec = new RuleSpecification<SensorReading>(rule);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Temperature = 90 }).Should().BeTrue();
        expr(new SensorReading { Temperature = 70 }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_Between_Works()
    {
        var rule = Rule.Between("Temperature", 20.0, 40.0);
        var spec = new RuleSpecification<SensorReading>(rule);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Temperature = 30 }).Should().BeTrue();
        expr(new SensorReading { Temperature = 50 }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_Contains_Works()
    {
        var rule = new Rule("Status", ComparisonOperator.Contains, "act");
        var spec = new RuleSpecification<SensorReading>(rule);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Status = "Active" }).Should().BeTrue();
        expr(new SensorReading { Status = "Deleted" }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_StartsWith_Works()
    {
        var rule = new Rule("Status", ComparisonOperator.StartsWith, "Act");
        var spec = new RuleSpecification<SensorReading>(rule);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Status = "Active" }).Should().BeTrue();
        expr(new SensorReading { Status = "Inactive" }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_AndGroup_Works()
    {
        var group = RuleGroup.And(
            new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0),
            new Rule("Humidity", ComparisonOperator.GreaterThan, 60.0)
        );
        var spec = new RuleSpecification<SensorReading>(group);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Temperature = 80, Humidity = 70 }).Should().BeTrue();
        expr(new SensorReading { Temperature = 80, Humidity = 50 }).Should().BeFalse();
    }

    [Fact]
    public void ToExpression_OrGroup_Works()
    {
        var group = RuleGroup.Or(
            new Rule("Temperature", ComparisonOperator.GreaterThan, 100.0),
            new Rule("Humidity", ComparisonOperator.GreaterThan, 90.0)
        );
        var spec = new RuleSpecification<SensorReading>(group);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading { Temperature = 110, Humidity = 50 }).Should().BeTrue();
        expr(new SensorReading { Temperature = 50, Humidity = 95 }).Should().BeTrue();
        expr(new SensorReading { Temperature = 50, Humidity = 50 }).Should().BeFalse();
    }

    [Fact]
    public void RuleSet_WrapsInAndGroup()
    {
        var ruleSet = new RuleSet("Sensor Alarms",
            new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0),
            new Rule("Humidity", ComparisonOperator.GreaterThan, 60.0)
        );
        var spec = new RuleSpecification<SensorReading>(ruleSet);

        spec.IsSatisfiedBy(new SensorReading { Temperature = 80, Humidity = 70 }).Should().BeTrue();
        spec.IsSatisfiedBy(new SensorReading { Temperature = 40, Humidity = 70 }).Should().BeFalse();
    }

    [Fact]
    public void Composable_WithOtherSpecifications()
    {
        var ruleSpec = new RuleSpecification<SensorReading>(
            new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0));

        var ruleSpec2 = new RuleSpecification<SensorReading>(
            new Rule("Humidity", ComparisonOperator.LessThan, 30.0));

        var combined = ruleSpec.And(ruleSpec2);

        combined.IsSatisfiedBy(new SensorReading { Temperature = 80, Humidity = 20 }).Should().BeTrue();
        combined.IsSatisfiedBy(new SensorReading { Temperature = 80, Humidity = 50 }).Should().BeFalse();
    }

    [Fact]
    public void DisabledRule_TreatedAsTrue()
    {
        var group = RuleGroup.And(
            new Rule("Temperature", ComparisonOperator.GreaterThan, 50.0),
            new Rule("Humidity", ComparisonOperator.GreaterThan, 99.0) { IsEnabled = false }
        );
        var spec = new RuleSpecification<SensorReading>(group);

        var expr = spec.ToExpression().Compile();

        // Disabled rule evaluates to true, so only Temperature check matters
        expr(new SensorReading { Temperature = 80, Humidity = 10 }).Should().BeTrue();
    }

    [Fact]
    public void MissingProperty_ReturnsFalse()
    {
        var rule = new Rule("NonExistent", ComparisonOperator.Equal, "value");
        var spec = new RuleSpecification<SensorReading>(rule);

        var expr = spec.ToExpression().Compile();

        expr(new SensorReading()).Should().BeFalse();
    }
}
