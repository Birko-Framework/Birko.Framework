using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Rules;

namespace Birko.EventBus.Pipeline;

/// <summary>
/// Event pipeline behavior that filters events using Birko.Rules.
/// Events that don't match the rule set are short-circuited (handler not called).
/// Useful for conditional event handling, routing, and access control.
/// </summary>
public class RuleFilterBehavior : IEventPipelineBehavior
{
    private readonly RuleSet _ruleSet;
    private readonly IRuleEvaluator _evaluator;
    private readonly Func<IEvent, EventContext, IRuleContext>? _contextFactory;

    /// <summary>
    /// Creates a filter behavior using reflection-based context (reads event properties).
    /// </summary>
    public RuleFilterBehavior(RuleSet ruleSet, IRuleEvaluator? evaluator = null)
    {
        _ruleSet = ruleSet;
        _evaluator = evaluator ?? new RuleEvaluator();
    }

    /// <summary>
    /// Creates a filter behavior with a custom context factory.
    /// Use this to build context from both event properties and EventContext metadata.
    /// </summary>
    public RuleFilterBehavior(
        RuleSet ruleSet,
        Func<IEvent, EventContext, IRuleContext> contextFactory,
        IRuleEvaluator? evaluator = null)
    {
        _ruleSet = ruleSet;
        _evaluator = evaluator ?? new RuleEvaluator();
        _contextFactory = contextFactory;
    }

    public Task HandleAsync(IEvent @event, EventContext context, Func<Task> next, CancellationToken cancellationToken = default)
    {
        if (!_ruleSet.IsEnabled)
            return next();

        var ruleContext = _contextFactory is not null
            ? _contextFactory(@event, context)
            : BuildDefaultContext(@event, context);

        var matches = _evaluator.Evaluate(_ruleSet, ruleContext);

        // If any rule matched, the event passes the filter → continue pipeline
        if (matches.Count > 0)
            return next();

        // No match → short-circuit, don't call handler
        return Task.CompletedTask;
    }

    private static IRuleContext BuildDefaultContext(IEvent @event, EventContext context)
    {
        var dict = new System.Collections.Generic.Dictionary<string, object?>
        {
            ["EventId"] = @event.EventId,
            ["OccurredAt"] = @event.OccurredAt,
            ["Source"] = @event.Source,
            ["DeliveryCount"] = context.DeliveryCount
        };

        if (context.TenantGuid.HasValue)
            dict["TenantGuid"] = context.TenantGuid.Value;

        if (context.CorrelationId.HasValue)
            dict["CorrelationId"] = context.CorrelationId.Value;

        // Add event-specific properties via reflection
        foreach (var prop in @event.GetType().GetProperties(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            dict.TryAdd(prop.Name, prop.GetValue(@event));
        }

        // Add metadata
        foreach (var kvp in context.Metadata)
        {
            dict.TryAdd(kvp.Key, kvp.Value);
        }

        return new DictionaryRuleContext(dict);
    }
}
