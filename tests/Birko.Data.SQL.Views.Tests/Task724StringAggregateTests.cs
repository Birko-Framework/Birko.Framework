using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-724, on SQLite. Measured before the fix: a string <c>MIN</c> / <c>MAX</c> view column read with
/// <c>GetString</c> and no null check, so a group whose every value was NULL failed the whole read.
/// </summary>
public sealed class Task724StringAggregateTests : IDisposable
{
    public class T724Person : AbstractModel { public string? Name { get; set; } }
    public class T724Order : AbstractModel { public Guid PersonId { get; set; } public string? Note { get; set; } }

    private sealed class PersonMapping : IModelMapping<T724Person>
    {
        public void Configure(ModelMap<T724Person> map) { map.ToTable("T724Persons").HasPrimary(x => x.Guid); map.Property(x => x.Name); }
    }

    private sealed class OrderMapping : IModelMapping<T724Order>
    {
        public void Configure(ModelMap<T724Order> map) { map.ToTable("T724Orders").HasPrimary(x => x.Guid); map.Property(x => x.PersonId); map.Property(x => x.Note); }
    }

    [Birko.Data.SQL.Attributes.View(typeof(T724Order), typeof(T724Person), nameof(T724Order.PersonId), nameof(T724Person.Guid), name: "T724Notes")]
    public class T724NotesView
    {
        [Birko.Data.SQL.Attributes.ViewField(typeof(T724Person), nameof(T724Person.Name))]
        public string? PersonName { get; set; }

        [Birko.Data.SQL.Attributes.MinField(typeof(T724Order), nameof(T724Order.Note))]
        public string? FirstNote { get; set; }
    }

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t724v-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_string_minimum_over_only_nulls_reads_null()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new PersonMapping());
        registry.Register(new OrderMapping());
        registry.ApplyToDatabase();
        var connector = new SqLiteConnector(new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path)));
        connector.CreateTable(new[] { typeof(T724Person), typeof(T724Order) });
        var person = new T724Person { Guid = Guid.NewGuid(), Name = "p" };
        await connector.InsertAsync(typeof(T724Person), person);
        await connector.InsertAsync(typeof(T724Order), new T724Order { Guid = Guid.NewGuid(), PersonId = person.Guid!.Value, Note = null });

        Expression<Func<T724NotesView, bool>> all = v => v.PersonName != null;
        var rows = connector.SelectView(typeof(T724NotesView), all).Cast<T724NotesView>().ToList();

        rows.Should().ContainSingle().Which.FirstNote.Should().BeNull();
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_path}"));
        try { File.Delete(_path); } catch { }
    }
}
