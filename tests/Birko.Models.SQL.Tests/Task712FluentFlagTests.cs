using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-712. Measured before the fix: a fluent <c>IsUnique</c> on a nullable column was written onto the loaded field
/// after its indexes were built, so it got neither an inline UNIQUE nor the <c>ux_</c> index a <c>[UniqueField]</c> gets,
/// and SQLite took a duplicate; <c>Ignore</c> / <c>IsIgnored</c> was never applied, so the property stayed a column;
/// a flag on a property that is no column was dropped; a second <c>Property()</c> call for one member got a fresh
/// descriptor, so the duplicate-index refusal never saw the first; and <c>RegisterPrecision(…, null, null)</c> left an
/// earlier registration in place. Each test owns its model types: registrations are process-wide, keyed by type.
/// </summary>
public class Task712FluentFlagTests
{
    // ---- IsUnique on a nullable column ----

    [Table("T712Accounts")]
    public class Account : Birko.Data.Models.AbstractModel
    {
        [NamedField, MaxLengthField(64)] public string? Email { get; set; }
    }

    private sealed class AccountMap : IModelMapping<Account>
    {
        public void Configure(ModelMap<Account> map) => map.Property(x => x.Email).IsUnique();
    }

    [Fact]
    public void A_fluent_unique_on_a_nullable_column_gets_the_index_an_attribute_gets()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new AccountMap());
        registry.ApplyToDatabase();

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Account));

        table.Indexes.Should().ContainKey("ux_T712Accounts_Email");
        table.Indexes!["ux_T712Accounts_Email"].Unique.Should().BeTrue();
    }

    [Fact]
    public async Task On_SQLite_a_fluent_unique_on_a_nullable_column_refuses_a_duplicate_and_allows_two_nulls()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new AccountMap());
        registry.ApplyToDatabase();
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"birko-t712-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(root);
        var settings = new Birko.Data.SQL.SqLite.Stores.SqLiteSettings(root, "t712.db");
        try
        {
            var store = new Birko.Data.SQL.SqLite.Stores.AsyncSQLiteStore<Account>();
            store.SetSettings(settings);
            await store.CreateAsync(new Account { Email = "a@x" });
            await store.CreateAsync(new Account { Email = null });
            await store.CreateAsync(new Account { Email = null });

            var duplicate = () => store.CreateAsync(new Account { Email = "a@x" });

            await duplicate.Should().ThrowAsync<Exception>("Email is unique among the rows that have one");
        }
        finally
        {
            using (var probe = new Microsoft.Data.Sqlite.SqliteConnection(settings.GetConnectionString()))
                Microsoft.Data.Sqlite.SqliteConnection.ClearPool(probe);
            try { System.IO.Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // ---- Ignore ----

    [Table("T712Notes")]
    public class Note
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Text { get; set; } = string.Empty;
        [NamedField] public string Draft { get; set; } = string.Empty;
        [NamedField] public string Scratch { get; set; } = string.Empty;
    }

    private sealed class NoteMap : IModelMapping<Note>
    {
        public void Configure(ModelMap<Note> map)
        {
            map.Ignore(x => x.Draft);
            map.Property(x => x.Scratch).IsIgnored();
        }
    }

    [Fact]
    public void An_ignored_property_is_no_column()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new NoteMap());
        registry.ApplyToDatabase();

        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Note));

        table.GetFieldByPropertyName("Draft").Should().BeNull();
        table.GetFieldByPropertyName("Scratch").Should().BeNull();
        table.GetFieldByPropertyName("Text").Should().NotBeNull();
    }

    // ---- a declaration on a property that is no column is refused ----

    [Table("T712Shaped")]
    public class Shaped
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string First { get; set; } = string.Empty;
        public string Display => First;
        [IgnoreField] public string Cached { get; set; } = string.Empty;
    }

    private sealed class DelegateMap<T> : IModelMapping<T> where T : class
    {
        private readonly Action<ModelMap<T>> _configure;
        public DelegateMap(Action<ModelMap<T>> configure) => _configure = configure;
        public void Configure(ModelMap<T> map) => _configure(map);
    }

    public static TheoryData<string> NotAColumn => new() { "unique-getonly", "required-ignorefield", "column-getonly", "primary-getonly", "unique-ignored" };

    [Theory]
    [MemberData(nameof(NotAColumn))]
    public void A_flag_on_a_property_that_is_no_column_is_refused(string kind)
    {
        Action<ModelMap<Shaped>> configure = kind switch
        {
            "unique-getonly" => m => m.Property(x => x.Display).IsUnique(),
            "required-ignorefield" => m => m.Property(x => x.Cached).IsRequired(),
            "column-getonly" => m => m.Property(x => x.Display).HasColumnName("display"),
            "primary-getonly" => m => m.Property(x => x.Display).IsPrimary(),
            _ => m => m.Property(x => x.First).IsIgnored().IsUnique(),
        };
        var registry = new ModelMapRegistry();
        registry.Register(new DelegateMap<Shaped>(configure));

        registry.Invoking(r => r.ApplyToDatabase()).Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("Shaped.");
    }

    // ---- a second Property() call reaches the same descriptor ----

    [Table("T712Twice")]
    public class Twice
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField] public string Code { get; set; } = string.Empty;
    }

    [Fact]
    public void Declaring_one_index_twice_through_two_Property_calls_is_refused()
    {
        var map = new ModelMap<Twice>();
        map.Property(x => x.Code).HasIndex("IX_T712_Code");

        map.Invoking(m => m.Property(x => x.Code).HasIndex("IX_T712_Code")).Should().Throw<InvalidOperationException>();
        map.Properties.Count(p => p.Name == nameof(Twice.Code)).Should().Be(1);
    }

    [Fact]
    public void Two_Property_calls_for_one_member_combine()
    {
        var map = new ModelMap<Twice>();
        map.Property(x => x.Code).IsRequired();
        map.Property(x => x.Code).HasMaxLength(10);

        var descriptor = map.Properties.Single(p => p.Name == nameof(Twice.Code));
        descriptor.IsRequired.Should().BeTrue();
        descriptor.MaxLength.Should().Be(10);
    }

    // ---- RegisterPrecision(null, null) removes a registration ----

    [Table("T712Priced")]
    public class Priced712
    {
        [PrimaryField, NamedField] public Guid? Guid { get; set; }
        [NamedField, PrecisionField(12), ScaleField(3)] public decimal Rate { get; set; }
    }

    [Fact]
    public void Registering_no_precision_and_no_scale_gives_the_attributes_back()
    {
        Birko.Data.SQL.DataBase.RegisterPrecision(typeof(Priced712), nameof(Priced712.Rate), 8, 2);
        ((DecimalField)Birko.Data.SQL.DataBase.LoadTable(typeof(Priced712)).GetFieldByPropertyName("Rate")!).Precision.Should().Be(8);

        Birko.Data.SQL.DataBase.RegisterPrecision(typeof(Priced712), nameof(Priced712.Rate), null, null);

        var field = (DecimalField)Birko.Data.SQL.DataBase.LoadTable(typeof(Priced712)).GetFieldByPropertyName("Rate")!;
        field.Precision.Should().Be(12);
        field.Scale.Should().Be(3);
    }
}
