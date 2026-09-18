using System.Linq;
using Birko.Models.Users;
using Birko.Models.Users.SQL.Mappings;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Users.SQL.Tests;

/// <summary>
/// CR-L325: broaden coverage of the 8 Users.SQL mappings — table names, Guid primary/unique,
/// UserProfile.UserGuid uniqueness — and CR-L324's FK lookup indexes.
/// </summary>
public class UserMappingTests
{
    private static ModelMap<T> Configure<T>(IModelMapping<T> mapping) where T : class
    {
        var map = new ModelMap<T>();
        mapping.Configure(map);
        return map;
    }

    [Fact]
    public void Mappings_HaveExpectedTableNames()
    {
        Configure(new RoleMapping()).TableName.Should().Be("Roles");
        Configure(new RolePermissionMapping()).TableName.Should().Be("RolePermissions");
        Configure(new TenantMapping()).TableName.Should().Be("Tenants");
        Configure(new UserMapping()).TableName.Should().Be("Users");
        Configure(new UserLoginMapping()).TableName.Should().Be("UserLogins");
        Configure(new UserProfileMapping()).TableName.Should().Be("UserProfiles");
        Configure(new UserRoleMapping()).TableName.Should().Be("UserRoles");
        Configure(new UserTenantMapping()).TableName.Should().Be("UserTenants");
    }

    [Fact]
    public void Mappings_MarkGuidAsPrimaryAndUnique()
    {
        AssertGuidKey(Configure(new RoleMapping()));
        AssertGuidKey(Configure(new RolePermissionMapping()));
        AssertGuidKey(Configure(new TenantMapping()));
        AssertGuidKey(Configure(new UserMapping()));
        AssertGuidKey(Configure(new UserLoginMapping()));
        AssertGuidKey(Configure(new UserProfileMapping()));
        AssertGuidKey(Configure(new UserRoleMapping()));
        AssertGuidKey(Configure(new UserTenantMapping()));

        static void AssertGuidKey<T>(ModelMap<T> map) where T : class
        {
            var guid = map.Properties.Single(p => p.Name == "Guid");
            guid.IsPrimary.Should().BeTrue("Guid must be the primary key");
            guid.IsUnique.Should().BeTrue("Guid must be unique");
        }
    }

    [Fact]
    public void UserProfile_UserGuid_IsUnique()
    {
        Configure(new UserProfileMapping()).Properties.Single(p => p.Name == "UserGuid")
            .IsUnique.Should().BeTrue("UserProfile has a 1:1 relationship with User");
    }

    [Theory]
    [InlineData(nameof(UserRole.UserGuid), "IX_UserRoles_UserGuid")]
    [InlineData(nameof(UserRole.RoleGuid), "IX_UserRoles_RoleGuid")]
    public void UserRole_FkColumns_AreIndexed(string column, string indexName)
    {
        Configure(new UserRoleMapping()).Properties.Single(p => p.Name == column)
            .IndexName.Should().Be(indexName);
    }

    [Fact]
    public void UserTenant_RolePermission_UserLogin_FkColumns_AreIndexed()
    {
        Configure(new UserTenantMapping()).Properties.Single(p => p.Name == "UserGuid")
            .IndexName.Should().Be("IX_UserTenants_UserGuid");
        Configure(new RolePermissionMapping()).Properties.Single(p => p.Name == "RoleGuid")
            .IndexName.Should().Be("IX_RolePermissions_RoleGuid");
        Configure(new UserLoginMapping()).Properties.Single(p => p.Name == "UserGuid")
            .IndexName.Should().Be("IX_UserLogins_UserGuid");
    }
}
