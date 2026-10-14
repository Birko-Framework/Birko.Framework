using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-596 (FIELD-034). A filtered <c>UpdateAsync(filter, PropertyUpdate)</c> must store a value exactly as an
/// entity write stores it. <c>PropertyUpdateSqlTranslator</c> bound a Set's value raw, while an entity write goes
/// through <c>AbstractField.Write</c> → <c>ToStorage</c>, which reshapes a <c>[UtcField]</c> <c>DateTime</c> into a
/// UTC <c>DateTimeOffset</c> and a <c>TimeOnly</c> into fixed-format text.
/// </summary>
public class PropertyUpdateStorageConversionTests : IDisposable
{
    public enum Stage { Draft = 0, Live = 2 }

    [Table("SqPropertyUpdateConversion")]
    public class Row : AbstractModel
    {
        [UtcField]
        public DateTime At { get; set; }

        public TimeOnly Opens { get; set; }

        public Stage State { get; set; }

        public decimal Amount { get; set; }

        public DateTime Plain { get; set; }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-pu-conv-{Guid.NewGuid():N}");
    private readonly SqLiteSettings _settings;
    private readonly AsyncSQLiteStore<Row> _store = new();

    public PropertyUpdateStorageConversionTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, "conv.db");
        _store.SetSettings(_settings);
    }

    public void Dispose()
    {
        SqlitePool.ClearFor(_settings);
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static readonly DateTime Instant = new(2026, 10, 14, 9, 30, 15, 123, DateTimeKind.Utc);
    private static readonly TimeOnly Opening = new(8, 15, 30);
    private const decimal Money = 1234567890.123456m;

    /// <summary>Each column's stored value and storage class, read straight from SQLite.</summary>
    private Dictionary<string, (object? Value, string Type)> Stored(Guid id)
    {
        using var connection = new SqliteConnection(_settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT At, typeof(At), Opens, typeof(Opens), State, typeof(State), Amount, typeof(Amount), Plain, typeof(Plain) " +
                              "FROM SqPropertyUpdateConversion WHERE Guid = @g";
        command.Parameters.AddWithValue("@g", id.ToString());
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        var result = new Dictionary<string, (object?, string)>();
        foreach (var (name, i) in new[] { ("At", 0), ("Opens", 2), ("State", 4), ("Amount", 6), ("Plain", 8) })
            result[name] = (reader.IsDBNull(i) ? null : reader.GetValue(i), reader.GetString(i + 1));
        return result;
    }

    private async Task<(Guid Entity, Guid Updated)> WriteBothWaysAsync()
    {
        var entity = new Row { At = Instant, Opens = Opening, State = Stage.Live, Amount = Money, Plain = Instant };
        await _store.CreateAsync(entity);

        var updated = new Row();
        await _store.CreateAsync(updated);
        var id = updated.Guid!.Value;
        await _store.UpdateAsync(r => r.Guid == id, new PropertyUpdate<Row>()
            .Set(r => r.At, Instant)
            .Set(r => r.Opens, Opening)
            .Set(r => r.State, Stage.Live)
            .Set(r => r.Amount, Money)
            .Set(r => r.Plain, Instant));
        return (entity.Guid!.Value, id);
    }

    [Theory]
    [InlineData("At")]
    [InlineData("Opens")]
    [InlineData("State")]
    [InlineData("Amount")]
    [InlineData("Plain")]
    public async Task APropertyUpdateSet_StoresWhatAnEntityWriteStores(string column)
    {
        var (entity, updated) = await WriteBothWaysAsync();

        var viaEntity = Stored(entity)[column];
        var viaUpdate = Stored(updated)[column];

        viaUpdate.Type.Should().Be(viaEntity.Type, $"{column}'s storage class");
        viaUpdate.Value.Should().Be(viaEntity.Value, $"{column}'s stored value");
    }

    [Fact]
    public async Task APropertyUpdateSet_ReadsBackLikeAnEntityWrite()
    {
        var (entity, updated) = await WriteBothWaysAsync();

        var a = (await _store.ReadAsync(entity))!;
        var b = (await _store.ReadAsync(updated))!;

        b.At.Should().Be(a.At);
        b.At.Kind.Should().Be(a.At.Kind);
        b.Opens.Should().Be(a.Opens);
        b.State.Should().Be(a.State);
        b.Amount.Should().Be(a.Amount);
        b.Plain.Should().Be(a.Plain);
    }

    [Fact]
    public async Task AFilterOnAUtcColumn_MatchesARowWrittenEitherWay()
    {
        // The stored shape decides whether an equality filter on the column matches at all.
        var (entity, updated) = await WriteBothWaysAsync();

        var matches = (await _store.ReadAsync(r => r.At == Instant, null, null, null)).Select(r => r.Guid).ToList();

        matches.Should().Contain(entity).And.Contain(updated);
    }

    [Fact]
    public async Task AUtcFilter_Matches_WithTheConstantOnEitherSide_AndInARange()
    {
        var (entity, updated) = await WriteBothWaysAsync();
        var before = Instant.AddMinutes(-1);
        var after = Instant.AddMinutes(1);

        var reversed = (await _store.ReadAsync(r => Instant == r.At, null, null, null)).Select(r => r.Guid).ToList();
        var ranged = (await _store.ReadAsync(r => r.At > before && r.At < after, null, null, null)).Select(r => r.Guid).ToList();
        var excluded = (await _store.ReadAsync(r => r.At > after, null, null, null)).ToList();

        reversed.Should().Contain(entity).And.Contain(updated);
        ranged.Should().Contain(entity).And.Contain(updated);
        excluded.Should().BeEmpty();
    }

    /// <summary>
    /// TASK-597 (FIELD-035). <c>values.Contains(x.At)</c> becomes <c>IN (…)</c>, built by the method-call branch of
    /// <c>ParseConditionExpression</c>, which TASK-596's comparison fix did not reach.
    /// </summary>
    [Fact]
    public async Task AnInList_OnAUtcColumn_MatchesRowsWrittenEitherWay()
    {
        var (entity, updated) = await WriteBothWaysAsync();
        var instants = new[] { Instant, Instant.AddDays(1) };
        var asList = new List<DateTime> { Instant };

        var viaArray = (await _store.ReadAsync(r => instants.Contains(r.At), null, null, null)).Select(r => r.Guid).ToList();
        var viaList = (await _store.ReadAsync(r => asList.Contains(r.At), null, null, null)).Select(r => r.Guid).ToList();

        viaArray.Should().Contain(entity).And.Contain(updated);
        viaList.Should().Contain(entity).And.Contain(updated);
    }

    [Fact]
    public async Task ANotInList_OnAUtcColumn_ExcludesRowsWrittenEitherWay()
    {
        var (entity, updated) = await WriteBothWaysAsync();
        var instants = new[] { Instant };

        var rest = (await _store.ReadAsync(r => !instants.Contains(r.At), null, null, null)).Select(r => r.Guid).ToList();

        rest.Should().NotContain(entity).And.NotContain(updated);
    }

    [Fact]
    public async Task AnEmptyInList_StillMatchesNothing()
    {
        await WriteBothWaysAsync();
        var none = Array.Empty<DateTime>();

        (await _store.ReadAsync(r => none.Contains(r.At), null, null, null)).Should().BeEmpty();
    }

    [Fact]
    public void TheSyncBulkStore_StoresAndFiltersTheSameWay()
    {
        var store = new SQLiteStore<Row>();
        store.SetSettings(_settings);
        var entity = new Row { At = Instant, Opens = Opening, State = Stage.Live, Amount = Money, Plain = Instant };
        store.Create(entity);
        var updated = new Row();
        store.Create(updated);
        var id = updated.Guid!.Value;

        store.Update(r => r.Guid == id, new PropertyUpdate<Row>().Set(r => r.At, Instant));

        Stored(id)["At"].Should().Be(Stored(entity.Guid!.Value)["At"]);
        store.Read(r => r.At == Instant, null, null, null).Select(r => r.Guid).Should().Contain(entity.Guid).And.Contain(updated.Guid);
    }
}
