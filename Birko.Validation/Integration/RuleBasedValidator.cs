using System.Threading;
using System.Threading.Tasks;
using Birko.Rules;

namespace Birko.Validation.Integration;

/// <summary>
/// Validates model instances using data-driven Birko.Rules.
/// Enables business users to define validation rules without code changes (e.g., loaded from DB/config).
/// </summary>
/// <typeparam name="T">The model type to validate.</typeparam>
public class RuleBasedValidator<T> : IValidator<T> where T : class
{
    private readonly RuleSet _ruleSet;
    private readonly IRuleEvaluator _evaluator;

    public RuleBasedValidator(RuleSet ruleSet, IRuleEvaluator? evaluator = null)
    {
        _ruleSet = ruleSet;
        _evaluator = evaluator ?? new RuleEvaluator();
    }

    public RuleBasedValidator(string name, IRuleEvaluator? evaluator = null, params IRule[] rules)
    {
        _ruleSet = new RuleSet(name, rules);
        _evaluator = evaluator ?? new RuleEvaluator();
    }

    public ValidationResult Validate(T instance)
    {
        var context = new ObjectRuleContext<T>(instance);
        var matches = _evaluator.Evaluate(_ruleSet, context);
        var result = new ValidationResult();

        foreach (var match in matches)
        {
            var (propertyName, errorCode, message) = ExtractError(match);
            result.AddError(propertyName, errorCode, message);
        }

        return result;
    }

    public Task<ValidationResult> ValidateAsync(T instance, CancellationToken ct = default)
    {
        return Task.FromResult(Validate(instance));
    }

    private static (string propertyName, string errorCode, string message) ExtractError(RuleResult match)
    {
        var propertyName = match.Rule switch
        {
            Rule leaf => leaf.Field,
            _ => match.Rule.Name ?? "Unknown"
        };

        var errorCode = $"RULE_{match.Severity.ToString().ToUpperInvariant()}";

        var message = match.Rule.Description
            ?? match.Rule.Name
            ?? $"Rule violation on '{propertyName}' (severity: {match.Severity})";

        return (propertyName, errorCode, message);
    }
}
