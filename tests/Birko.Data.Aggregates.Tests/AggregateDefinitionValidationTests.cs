using System;
using Birko.Data.Aggregates.Core;
using Birko.Data.Aggregates.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Aggregates.Tests;

/// <summary>
/// SH-M069 (TASK-157): a definition is refused when it cannot be mapped, instead of doubling every operation (a
/// navigation registered twice) or handing a null key to the data provider (a navigation with no Via / Through).
/// </summary>
public class AggregateDefinitionValidationTests
{
    private sealed class TwiceRegistered : AggregateDefinition<Product>
    {
        public TwiceRegistered()
        {
            HasMany(p => p.Tags).Via(t => t.ProductGuid);
            HasMany(p => p.Tags).Via(t => t.ProductGuid);
        }
    }

    private sealed class Keyless : AggregateDefinition<Product>
    {
        public Keyless()
        {
            HasMany(p => p.Tags);
        }
    }

    [Fact]
    public void A_navigation_registered_twice_is_refused()
    {
        var act = () => new TwiceRegistered();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Tags*already registered*");
    }

    [Fact]
    public void A_relationship_with_no_key_is_refused_when_read()
    {
        var definition = new Keyless();

        var act = () => definition.Relationships;

        act.Should().Throw<InvalidOperationException>().WithMessage("*Tags*no key*");
    }

    [Fact]
    public void A_complete_definition_reads_as_before()
    {
        new ProductAggregate().Relationships.Should().HaveCount(3);
    }
}
