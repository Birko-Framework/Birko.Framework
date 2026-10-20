using Birko.Data.Localization.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.UnitOfWork;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Localization.MongoDB.Tests;

/// <summary>
/// TASK-700, measured before designing: each Birko MongoDB store builds its own client. Can one store's session carry
/// another store's writes into the same transaction?
/// </summary>
public sealed class SessionAcrossClientsMeasurementTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string? _host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
    private readonly string _db = "birko_task700_" + Guid.NewGuid().ToString("N");

    public SessionAcrossClientsMeasurementTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task One_stores_session_used_by_a_second_store_on_the_same_settings()
    {
        if (string.IsNullOrWhiteSpace(_host)) return;
        var settings = new Settings(_host, _db);
        var a = new AsyncMongoDBStore<EntityTranslationModel>();
        a.SetSettings(settings);
        var b = new AsyncMongoDBStore<EntityTranslationModel>();
        b.SetSettings(settings);
        await a.InitAsync();
        await b.InitAsync();

        await using var uow = MongoDbUnitOfWork.FromStore(a);
        await uow.BeginAsync();
        a.SetTransactionContext(uow.Context);
        b.SetTransactionContext(uow.Context);
        Exception? thrown = null;
        try
        {
            await a.CreateAsync(new EntityTranslationModel { EntityGuid = Guid.NewGuid(), EntityType = "T", FieldName = "F", Culture = "sk", Value = "a" });
            await b.CreateAsync(new EntityTranslationModel { EntityGuid = Guid.NewGuid(), EntityType = "T", FieldName = "F", Culture = "sk", Value = "b" });
        }
        catch (Exception e)
        {
            thrown = e;
        }
        if (uow.IsActive) await uow.RollbackAsync();
        a.SetTransactionContext(null);
        b.SetTransactionContext(null);

        var left = await new MongoClient($"mongodb://{_host}:27017").GetDatabase(_db)
            .GetCollection<global::MongoDB.Bson.BsonDocument>(nameof(EntityTranslationModel)).CountDocumentsAsync(FilterDefinition<global::MongoDB.Bson.BsonDocument>.Empty);
        _output.WriteLine($"thrown: {thrown?.GetType().Name}: {thrown?.Message}");
        _output.WriteLine($"rows left after rollback: {left}");
        left.Should().Be(0, "both writes were in the rolled-back transaction");
        thrown.Should().BeNull();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(_host)) return;
        try { new MongoClient($"mongodb://{_host}:27017").DropDatabase(_db); } catch { }
    }
}
