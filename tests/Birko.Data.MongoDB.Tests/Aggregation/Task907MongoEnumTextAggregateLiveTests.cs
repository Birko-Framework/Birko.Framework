using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Tests.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Aggregation;

/// <summary>Names in another order than the values: by name High &lt; Low &lt; Medium &lt; Zero, by value Zero &lt; Low &lt; Medium &lt; High.</summary>
public enum T907Priority { Zero = 0, High = 30, Low = 10, Medium = 20 }

[Flags]
public enum T907Rights { None = 0, Read = 1, Write = 2, Admin = 4 }

public class T907EnumDoc : Birko.Data.MongoDB.Models.MongoDBModel
{
    public string? Kind { get; set; }

    [BsonRepresentation(BsonType.String)]
    public T907Priority Priority { get; set; }

    [BsonRepresentation(BsonType.String)]
    public T907Priority? Maybe { get; set; }

    [BsonRepresentation(BsonType.String)]
    public T907Rights Rights { get; set; }

    public T907Priority Numeric { get; set; }
}

/// <summary>
/// TASK-907, live MongoDB: a Min / Max of an enum stored as its name (<c>[BsonRepresentation(BsonType.String)]</c>) is
/// taken by the enum's value, as <see cref="AggregateHelper.LinqAggregate{T}"/> takes it (C# compares an enum by its
/// value), not by its name. A [Flags] enum stored as text is refused by name: a combination is stored as "Read, Write",
/// which no list of names orders. Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public sealed class Task907MongoEnumTextAggregateLiveTests : IDisposable
{
    private readonly MongoLive _live;
    private readonly ITestOutputHelper _output;

    public Task907MongoEnumTextAggregateLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _live = new MongoLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static IEnumerable<T907EnumDoc> Seed()
    {
        yield return new T907EnumDoc { Kind = "a", Priority = T907Priority.High, Maybe = null, Numeric = T907Priority.High };
        yield return new T907EnumDoc { Kind = "a", Priority = T907Priority.Zero, Maybe = T907Priority.Low, Numeric = T907Priority.Zero };
        yield return new T907EnumDoc { Kind = "b", Priority = T907Priority.Low, Maybe = T907Priority.High, Numeric = T907Priority.Low };
        yield return new T907EnumDoc { Kind = "b", Priority = T907Priority.Medium, Maybe = T907Priority.Medium, Numeric = T907Priority.Medium };
        yield return new T907EnumDoc { Kind = "c", Priority = T907Priority.Medium, Maybe = null, Numeric = T907Priority.Medium };
    }

    private static readonly AggregateField[] Extremes =
    {
        new(AggregateFunction.Count, "Kind", "count"),
        new(AggregateFunction.Min, "Priority", "minPriority"),
        new(AggregateFunction.Max, "Priority", "maxPriority"),
        new(AggregateFunction.Min, "Maybe", "minMaybe"),
        new(AggregateFunction.Max, "Maybe", "maxMaybe"),
        new(AggregateFunction.Min, "Numeric", "minNumeric"),
        new(AggregateFunction.Max, "Numeric", "maxNumeric"),
    };

    private static string Render(AggregateResult row)
        => string.Join("; ", row.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={(kv.Value == null ? "null" : kv.Value.GetType().Name + ":" + kv.Value)}"));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_min_or_max_of_an_enum_stored_as_text_is_taken_by_its_value(bool grouped, bool async)
    {
        if (!_live.Ready()) return;
        var query = new AggregateQuery<T907EnumDoc>
        {
            GroupByFields = grouped ? new[] { "Kind" } : Array.Empty<string>(),
            Aggregates = Extremes,
        };
        var expected = AggregateHelper.LinqAggregate(Seed().ToList(), query).Select(Render).OrderBy(x => x, StringComparer.Ordinal).ToList();

        List<string> got;
        if (async)
        {
            var store = new AsyncMongoDBStore<T907EnumDoc>();
            store.SetSettings(_live.NewSettings());
            foreach (var doc in Seed()) await store.CreateAsync(doc);
            got = (await store.AggregateAsync(query)).Select(Render).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
        else
        {
            var store = new MongoDBStore<T907EnumDoc>();
            store.SetSettings(_live.NewSettings());
            foreach (var doc in Seed()) store.Create(doc);
            got = store.Aggregate(query).Select(Render).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        _output.WriteLine("expected: " + string.Join(" | ", expected));
        _output.WriteLine("got:      " + string.Join(" | ", got));
        got.Should().Equal(expected);
    }

    private sealed class RawStore : AsyncMongoDBStore<T907EnumDoc>
    {
        public IMongoCollection<BsonDocument> Raw()
            => Collection!.Database.GetCollection<BsonDocument>(Collection.CollectionNamespace.CollectionName);
    }

    /// <summary>
    /// A document written before the member was stored as text holds the number. The driver reads it as the value (checked
    /// by a read here), so its Min / Max is taken by value too. Measured before: the number was skipped, so the Max of 30
    /// (High) and "Low" was Low.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_value_still_stored_as_a_number_is_ordered_by_value_beside_the_names(bool grouped)
    {
        if (!_live.Ready()) return;
        var store = new RawStore();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(new T907EnumDoc { Kind = "a", Priority = T907Priority.Low, Maybe = T907Priority.Low });
        await store.Raw().InsertOneAsync(new BsonDocument { { "_id", Guid.NewGuid().ToString() }, { "Kind", "a" }, { "Priority", 30 }, { "Maybe", 0 }, { "Rights", "None" }, { "Numeric", 0 } });

        var rows = (await store.ReadAsync(x => true)).ToList();
        rows.Select(r => r.Priority).Should().BeEquivalentTo(new[] { T907Priority.Low, T907Priority.High }, "the driver reads the number as the value");

        var query = new AggregateQuery<T907EnumDoc>
        {
            GroupByFields = grouped ? new[] { "Kind" } : Array.Empty<string>(),
            Aggregates = Extremes,
        };
        var expected = AggregateHelper.LinqAggregate(rows, query).Select(Render).ToList();
        var got = (await store.AggregateAsync(query)).Select(Render).ToList();
        _output.WriteLine("expected: " + string.Join(" | ", expected));
        _output.WriteLine("got:      " + string.Join(" | ", got));
        got.Should().Equal(expected);
    }

    [Theory]
    [InlineData(AggregateFunction.Min)]
    [InlineData(AggregateFunction.Max)]
    public async Task A_min_or_max_of_a_flags_enum_stored_as_text_is_refused_by_name(AggregateFunction function)
    {
        if (!_live.Ready()) return;
        var store = new AsyncMongoDBStore<T907EnumDoc>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(new T907EnumDoc { Kind = "a", Rights = T907Rights.Read | T907Rights.Write });
        await store.CreateAsync(new T907EnumDoc { Kind = "a", Rights = T907Rights.Admin });

        var act = () => store.AggregateAsync(new AggregateQuery<T907EnumDoc>
        {
            Aggregates = new[] { new AggregateField(function, "Rights", "r") },
        });

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("Rights").And.Contain("Flags");
    }
}
