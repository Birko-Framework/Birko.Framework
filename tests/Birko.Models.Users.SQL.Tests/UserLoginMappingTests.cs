using System.Linq;
using Birko.Models.Users;
using Birko.Models.Users.SQL.Mappings;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Users.SQL.Tests;

/// <summary>
/// CR-M229: UserLogin's natural key (Provider, ProviderKey) had no constraint (only Guid was unique).
/// The mapping declares the pair as one composite UNIQUE index (<c>HasUniqueIndex</c>). <c>AppliedIndexTests</c> shows it
/// reaches the table and rejects a duplicate on SQLite (TASK-595; before that this metadata reached nothing).
/// </summary>
public class UserLoginMappingTests
{
    private static System.Collections.Generic.IReadOnlyList<Birko.Data.Patterns.Schema.FieldDescriptor> Map()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new UserLoginMapping());
        return registry.GetPropertyMaps(typeof(UserLogin)).ToList();
    }

    [Fact]
    public void ProviderAndProviderKey_ShareCompositeIndex()
    {
        var fields = Map();
        var provider = fields.First(f => f.Name == "Provider");
        var providerKey = fields.First(f => f.Name == "ProviderKey");

        provider.IndexName.Should().Be("UX_UserLogin_Provider_ProviderKey");
        providerKey.IndexName.Should().Be("UX_UserLogin_Provider_ProviderKey",
            "both columns must join the same composite index (CR-M229)");
        provider.IndexOrder.Should().Be(0);
        providerKey.IndexOrder.Should().Be(1);
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("ProviderKey")]
    public void NaturalKeyColumns_AreBounded(string column)
    {
        Map().First(f => f.Name == column).MaxLength.Should().BeGreaterThan(0, "a string is bounded by HasMaxLength (TASK-692)");
    }
}
