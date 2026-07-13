using System.Collections.Generic;
using System.Linq;
using Birko.Models;
using Birko.Models.Product;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Product.Tests;

/// <summary>
/// CR-M223: the default-interface LoadProperties normalization (flatten the source→values dictionary
/// into SourceValue&lt;string&gt; and drop null values) was untested.
/// </summary>
public class ProductPropertiesLoadTests
{
    private sealed class Bag : IProductProperties
    {
        public IEnumerable<SourceValue<string>> Properties { get; set; } = new List<SourceValue<string>>();
    }

    [Fact]
    public void LoadProperties_FlattensSourceToValues()
    {
        IProductProperties bag = new Bag();
        bag.LoadProperties(new Dictionary<string, IList<string>>
        {
            ["color"] = new List<string> { "red", "green" },
            ["size"] = new List<string> { "L" },
        });

        var props = bag.Properties.ToList();
        props.Should().HaveCount(3);
        props.Where(p => p.Source == "color").Select(p => p.Value).Should().BeEquivalentTo(new[] { "red", "green" });
        props.Single(p => p.Source == "size").Value.Should().Be("L");
    }

    [Fact]
    public void LoadProperties_DropsNullValues()
    {
        IProductProperties bag = new Bag();
        bag.LoadProperties(new Dictionary<string, IList<string>>
        {
            ["k"] = new List<string> { "a", null!, "b" },
        });

        bag.Properties.Select(p => p.Value).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void LoadProperties_NullDictionary_YieldsEmpty()
    {
        IProductProperties bag = new Bag();
        bag.LoadProperties(null!);
        bag.Properties.Should().BeEmpty();
    }
}
