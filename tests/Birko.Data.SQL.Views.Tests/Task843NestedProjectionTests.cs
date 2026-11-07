using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Views;
using PortableQueryMode = Birko.Data.Views.ViewQueryMode;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-843: a view projecting a nested member (<c>r.Sub.X</c>) was recorded as source field <c>X</c>, so on SQL it
/// read the row's top-level <c>X</c> column with no error (reproduced on SQLite, 2026-11-07: the view answered 7, 6, 5
/// where the nested values were 0, 1, 2). A SQL table maps one column per top-level property and has no nested member
/// to read, so the definition is refused by name when the store or the view manager receives it.
/// </summary>
public sealed class Task843NestedProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t843-{Guid.NewGuid():N}");

    public Task843NestedProjectionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TestTempDirectory.Delete(_root);
    }

    public class T843Sub { public int X { get; set; } }

    public class T843Row : AbstractModel
    {
        public int X { get; set; }
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public T843Sub Sub { get; set; } = new();
    }

    public class NestedView
    {
        public int TopX { get; set; }
        public int SubX { get; set; }
    }

    private sealed class RowMapping : IModelMapping<T843Row>
    {
        public void Configure(ModelMap<T843Row> map)
        {
            map.ToTable("T843Rows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.X);
        }
    }

    private static ViewDefinition Nested(PortableQueryMode mode = PortableQueryMode.OnTheFly)
    {
        var builder = new ViewDefinitionBuilder<NestedView>()
            .From<T843Row>()
            .Select<T843Row, int>(r => r.X, v => v.TopX)
            .Select<T843Row, int>(r => r.Sub.X, v => v.SubX);
        if (mode != PortableQueryMode.OnTheFly) builder = builder.HasName("T843Nested").HasQueryMode(mode);
        return builder.Build();
    }

    private async Task<SqLiteConnector> SeedAsync()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();

        var connector = new SqLiteConnector(new SqLiteSettings(_root, "views.db"));
        connector.CreateTable(new[] { typeof(T843Row) });
        for (var i = 0; i < 3; i++)
        {
            await connector.InsertAsync(typeof(T843Row), new T843Row { Guid = Guid.NewGuid(), X = 7 - i, Sub = new T843Sub { X = i } });
        }
        return connector;
    }

    [Theory]
    [InlineData(PortableQueryMode.OnTheFly)]
    [InlineData(PortableQueryMode.Persistent)]
    public async Task A_nested_projection_is_refused_by_name_when_the_store_receives_it(PortableQueryMode mode)
    {
        var connector = await SeedAsync();

        var act = () => new SqlViewStore<NestedView>(connector, Nested(mode));

        act.Should().Throw<NotSupportedException>().WithMessage("*SubX*T843Row.Sub.X*");
    }

    [Fact]
    public async Task A_nested_projection_is_refused_by_name_when_the_view_is_ensured()
    {
        var connector = await SeedAsync();

        var act = () => new SqlViewManager(connector).EnsureAsync(Nested(PortableQueryMode.Persistent));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*T843Row.Sub.X*");
    }

    [Fact]
    public async Task A_top_level_projection_still_reads_its_column()
    {
        var connector = await SeedAsync();
        var definition = new ViewDefinitionBuilder<NestedView>()
            .From<T843Row>()
            .Select<T843Row, int>(r => r.X, v => v.TopX)
            .Build();

        var rows = (await new SqlViewStore<NestedView>(connector, definition).QueryAsync()).ToList();

        rows.Select(r => r.TopX).Should().BeEquivalentTo(new[] { 7, 6, 5 });
    }
}
