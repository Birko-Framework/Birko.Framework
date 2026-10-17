using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.IndexManagement;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests.IndexManagement;

/// <summary>TASK-651: an index number property is a whole number from 0 up, whatever its numeric type.</summary>
public class Task651IndexNumberTests
{
    private static ElasticSearchIndexManagerAdapter Adapter()
        => new(new ElasticClient(new ConnectionSettings(new Uri("http://127.0.0.1:1")).RequestTimeout(TimeSpan.FromSeconds(1))));

    private static Birko.Data.Patterns.IndexManagement.IndexDefinition With(object value) => new()
    {
        Name = "idx",
        Properties = new Dictionary<string, object> { ["NumberOfShards"] = value },
    };

    [Theory]
    [InlineData(-1)]
    [InlineData(-1L)]
    public async Task A_negative_number_is_refused_as_an_int_or_a_long(object value)
        => await Adapter().Invoking(a => a.CreateAsync(With(value))).Should().ThrowAsync<NotSupportedException>();

    [Fact]
    public async Task A_short_is_accepted_like_an_int()
    {
        var act = () => Adapter().CreateAsync(With((short)2));

        // No server: creation fails, but the number was not refused.
        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<NotSupportedException>();
    }
}
