using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.SQL;
using Birko.Models.SQL.Mapping;
using Birko.Models.Users.SQL.Mappings;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Users.SQL.Tests;

/// <summary>
/// TASK-595 (FIELD-033). A fluent <c>HasIndex</c> must reach the SQL layer's own index declarations — the
/// <c>Table.Indexes</c> that <c>CreateTable</c> emits for attribute-declared indexes. The mapping tests above check only
/// the mapping's metadata, which held the index names while nothing turned them into an index.
/// </summary>
public class AppliedIndexTests
{
    private static Birko.Data.SQL.Tables.Table Applied<T>(IModelMapping<T> mapping) where T : class
    {
        var registry = new ModelMapRegistry();
        registry.Register(mapping);
        registry.ApplyToDatabase();
        return DataBase.LoadTable(typeof(T));
    }

    [Fact]
    public void UserLogin_LookupIndex_ReachesTheTable()
    {
        var table = Applied(new UserLoginMapping());

        table.Indexes.Should().NotBeNull();
        table.Indexes!.Should().ContainKey("IX_UserLogins_UserGuid");
        table.Indexes["IX_UserLogins_UserGuid"].Columns.Select(c => c.ColumnName).Should().Equal("UserGuid");
    }

    [Fact]
    public void UserLogin_NaturalKey_IsAUniqueCompositeIndex_InOrder()
    {
        var table = Applied(new UserLoginMapping());

        table.Indexes.Should().NotBeNull();
        table.Indexes!.Should().ContainKey("UX_UserLogin_Provider_ProviderKey");
        var index = table.Indexes["UX_UserLogin_Provider_ProviderKey"];
        index.Columns.OrderBy(c => c.Order).Select(c => c.ColumnName).Should().Equal("Provider", "ProviderKey");
        index.Unique.Should().BeTrue("(Provider, ProviderKey) is the lookup key of an external sign-in");
    }

    [Fact]
    public async Task OnSQLite_TheNaturalKeyRejectsADuplicateLogin_AndTheIndexesExist()
    {
        Applied(new UserLoginMapping());
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"birko-users-ix-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        var settings = new Birko.Data.SQL.SqLite.Stores.SqLiteSettings(root, "users.db");
        try
        {
            var store = new Birko.Data.SQL.SqLite.Stores.AsyncSQLiteStore<UserLogin>();
            store.SetSettings(settings);
            await store.CreateAsync(new UserLogin { UserGuid = Guid.NewGuid(), Provider = "google", ProviderKey = "1001" });
            await store.CreateAsync(new UserLogin { UserGuid = Guid.NewGuid(), Provider = "github", ProviderKey = "1001" });

            var duplicate = () => store.CreateAsync(new UserLogin { UserGuid = Guid.NewGuid(), Provider = "google", ProviderKey = "1001" });

            await duplicate.Should().ThrowAsync<Exception>("(Provider, ProviderKey) names one external identity");
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'UserLogins'";
            using var reader = command.ExecuteReader();
            var names = new System.Collections.Generic.List<string>();
            while (reader.Read()) names.Add(reader.GetString(0));
            names.Should().Contain("UX_UserLogin_Provider_ProviderKey").And.Contain("IX_UserLogins_UserGuid");
        }
        finally
        {
            using (var probe = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString()))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(probe);
            try { System.IO.Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void MixingHasIndexAndHasUniqueIndexUnderOneName_IsRefused()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new MixedIndexMapping());

        var apply = () => registry.ApplyToDatabase();

        apply.Should().Throw<InvalidOperationException>().WithMessage("*mixes HasIndex and HasUniqueIndex*");
    }

    [Fact]
    public void FluentIndexColumns_AreMarkedIndexed_SoProvidersBoundTheirType()
    {
        // MySQL / SQL Server emit an unbounded string as TEXT, which cannot be an index key, unless the field says
        // it is indexed (rule 37). UserLogin.Provider / ProviderKey are unbounded strings: HasPrecision is metadata only.
        var table = Applied(new UserLoginMapping());

        table.GetFieldByPropertyName("Provider")!.IsIndexed.Should().BeTrue();
        table.GetFieldByPropertyName("ProviderKey")!.IsIndexed.Should().BeTrue();
        table.GetFieldByPropertyName("UserGuid")!.IsIndexed.Should().BeTrue();
        table.GetFieldByPropertyName("DisplayName")!.IsIndexed.Should().BeFalse();
    }

    [Fact]
    public void ApplyingTwice_DoesNotDuplicateIndexColumns()
    {
        Applied(new UserLoginMapping());
        var table = Applied(new UserLoginMapping());

        table.Indexes!["UX_UserLogin_Provider_ProviderKey"].Columns.Should().HaveCount(2);
    }

    /// <summary>A model of the test's own: ApplyToDatabase registers table names process-wide, so mapping a real model
    /// to another table here would rename it for every other test in the run (rule 61).</summary>
    public sealed class MixedRow : Birko.Data.Models.AbstractModel
    {
        public Guid A { get; set; }
        public Guid B { get; set; }
    }

    private sealed class MixedIndexMapping : IModelMapping<MixedRow>
    {
        public void Configure(ModelMap<MixedRow> map)
        {
            map.ToTable("TestMixedIndexRows");
            map.Property(x => x.A).HasIndex("IX_Mixed", order: 0);
            map.Property(x => x.B).HasUniqueIndex("IX_Mixed", order: 1);
        }
    }

    [Fact]
    public void UserRole_ForeignKeyIndexes_ReachTheTable()
    {
        var table = Applied(new UserRoleMapping());

        table.Indexes.Should().NotBeNull();
        table.Indexes!.Keys.Should().Contain(k => k.Contains("UserGuid")).And.Contain(k => k.Contains("RoleGuid"));
    }
}
