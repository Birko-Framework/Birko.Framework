using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Pipeline;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class RuleFilterBehaviorTests
{
    private record TestEvent(string Source, string Category, int Priority) : IEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; } = DateTime.UtcNow;
    }

    [Fact]
    public async Task MatchingEvent_CallsNext()
    {
        var ruleSet = new RuleSet("Filter",
            new Rule("Source", ComparisonOperator.Equal, "OrderService")
        );
        var behavior = new RuleFilterBehavior(ruleSet);

        var called = false;
        var evt = new TestEvent("OrderService", "Orders", 1);
        var ctx = EventContext.From(evt);

        await behavior.HandleAsync(evt, ctx, () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
    }

    [Fact]
    public async Task NonMatchingEvent_SkipsNext()
    {
        var ruleSet = new RuleSet("Filter",
            new Rule("Source", ComparisonOperator.Equal, "PaymentService")
        );
        var behavior = new RuleFilterBehavior(ruleSet);

        var called = false;
        var evt = new TestEvent("OrderService", "Orders", 1);
        var ctx = EventContext.From(evt);

        await behavior.HandleAsync(evt, ctx, () => { called = true; return Task.CompletedTask; });

        called.Should().BeFalse();
    }

    [Fact]
    public async Task DisabledRuleSet_AlwaysCallsNext()
    {
        var ruleSet = new RuleSet("Filter",
            new Rule("Source", ComparisonOperator.Equal, "NeverMatch")
        ) { IsEnabled = false };
        var behavior = new RuleFilterBehavior(ruleSet);

        var called = false;
        var evt = new TestEvent("OrderService", "Orders", 1);
        var ctx = EventContext.From(evt);

        await behavior.HandleAsync(evt, ctx, () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
    }

    [Fact]
    public async Task CustomEventProperties_AreAccessible()
    {
        var ruleSet = new RuleSet("Priority Filter",
            new Rule("Priority", ComparisonOperator.GreaterThanOrEqual, 5)
        );
        var behavior = new RuleFilterBehavior(ruleSet);

        var highPriority = new TestEvent("Svc", "Cat", 10);
        var lowPriority = new TestEvent("Svc", "Cat", 1);

        var highCalled = false;
        var lowCalled = false;

        await behavior.HandleAsync(highPriority, EventContext.From(highPriority),
            () => { highCalled = true; return Task.CompletedTask; });
        await behavior.HandleAsync(lowPriority, EventContext.From(lowPriority),
            () => { lowCalled = true; return Task.CompletedTask; });

        highCalled.Should().BeTrue();
        lowCalled.Should().BeFalse();
    }

    [Fact]
    public async Task CustomContextFactory_IsUsed()
    {
        var ruleSet = new RuleSet("Custom Filter",
            new Rule("CustomField", ComparisonOperator.Equal, "special")
        );
        var behavior = new RuleFilterBehavior(ruleSet,
            (evt, ctx) => DictionaryRuleContext.From(("CustomField", (object?)"special")));

        var called = false;
        var evt = new TestEvent("Svc", "Cat", 1);
        var ctx = EventContext.From(evt);

        await behavior.HandleAsync(evt, ctx, () => { called = true; return Task.CompletedTask; });

        called.Should().BeTrue();
    }

    [Fact]
    public async Task GroupRule_WorksInFilter()
    {
        var ruleSet = new RuleSet("Complex Filter",
            RuleGroup.And(
                new Rule("Source", ComparisonOperator.Equal, "OrderService"),
                new Rule("Priority", ComparisonOperator.GreaterThanOrEqual, 5)
            )
        );
        var behavior = new RuleFilterBehavior(ruleSet);

        var matchBoth = new TestEvent("OrderService", "Cat", 10);
        var matchOne = new TestEvent("OrderService", "Cat", 1);

        var bothCalled = false;
        var oneCalled = false;

        await behavior.HandleAsync(matchBoth, EventContext.From(matchBoth),
            () => { bothCalled = true; return Task.CompletedTask; });
        await behavior.HandleAsync(matchOne, EventContext.From(matchOne),
            () => { oneCalled = true; return Task.CompletedTask; });

        bothCalled.Should().BeTrue();
        oneCalled.Should().BeFalse();
    }
}
