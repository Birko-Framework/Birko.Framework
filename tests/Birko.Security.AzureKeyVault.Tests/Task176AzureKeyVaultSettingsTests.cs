using Birko.Security.AzureKeyVault;
using FluentAssertions;
using Xunit;

namespace Birko.Security.AzureKeyVault.Tests;

/// <summary>
/// TASK-176 — SH-L223: TenantId / ClientId / ClientSecret are <c>string?</c> aliases whose setters stored the value through
/// <c>value!</c> into non-nullable base properties, so a null landed where the type promised none. They store
/// <c>string.Empty</c>, as <c>PasswordSettings</c> and <c>VaultSettings.Token</c> already do.
/// </summary>
public class Task176AzureKeyVaultSettingsTests
{
    [Fact]
    public void Assigning_null_to_an_alias_stores_an_empty_string_not_a_null()
    {
        var settings = new AzureKeyVaultSettings { TenantId = "t", ClientId = "c", ClientSecret = "s" };

        settings.TenantId = null;
        settings.ClientId = null;
        settings.ClientSecret = null;

        settings.Name.Should().NotBeNull().And.BeEmpty();
        settings.UserName.Should().NotBeNull().And.BeEmpty();
        settings.Password.Should().NotBeNull().And.BeEmpty();
    }
}
