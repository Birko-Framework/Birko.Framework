using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-680 — the wrappers take a consumer's own translation model through <see cref="IEntityTranslation"/>, and a
/// create the store refuses for its unique key is retried as an update (SH-M074).
/// </summary>
public class LocalizationCustomModelTests
{
    /// <summary>A consumer's tenant base: what a translation model must derive from, so it cannot derive from EntityTranslationModel.</summary>
    public abstract class TenantBase : AbstractModel
    {
        public Guid TenantGuid { get; set; }
    }

    public sealed class TenantTranslation : TenantBase, IEntityTranslation
    {
        public Guid EntityGuid { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string Culture { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
        public string ValueHash { get; set; } = string.Empty;
    }

    private readonly TestEntityLocalizationContext _context = new()
    {
        CurrentCulture = new CultureInfo("sk"),
        DefaultCulture = new CultureInfo("en"),
    };

    [Fact]
    public void A_consumer_model_that_is_not_an_EntityTranslationModel_stores_and_serves_translations()
    {
        var entities = new InMemoryBulkStore<TestLocalizableModel>();
        var translations = new InMemoryBulkStore<TenantTranslation>();
        var bulk = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel, TenantTranslation>(
            entities, translations, _context);

        var guid = bulk.Create(new TestLocalizableModel { Name = "Stolicka", Code = "C1" });

        translations.Read().Single(t => t.FieldName == "Name").Should().BeEquivalentTo(new
        {
            EntityGuid = guid, EntityType = nameof(TestLocalizableModel), FieldName = "Name", Culture = "sk", Value = "Stolicka",
        });
        bulk.Read(guid)!.Name.Should().Be("Stolicka");
        bulk.Read(x => x.Name == "Stolicka", null, null, null).Select(x => x.Guid).Should().Equal(guid);
        bulk.Delete(entities.Read(guid)!);
        translations.Read().Should().BeEmpty();
    }

    [Fact]
    public async Task The_async_single_store_wrapper_takes_a_consumer_model_too()
    {
        var entities = new InMemoryAsyncBulkStore<TestLocalizableModel>();
        var translations = new InMemoryAsyncBulkStore<TenantTranslation>();
        var single = new AsyncLocalizedStoreWrapper<IAsyncStore<TestLocalizableModel>, TestLocalizableModel, TenantTranslation>(
            entities, translations, _context);

        var guid = await single.CreateAsync(new TestLocalizableModel { Name = "Stolicka" });

        (await single.ReadAsync(guid))!.Name.Should().Be("Stolicka");
        (await translations.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .Should().ContainSingle(t => t.FieldName == "Name" && t.EntityGuid == guid);
    }

    /// <summary>Implements the interface explicitly: the members exist, but not as properties the filters can name.</summary>
    public sealed class ExplicitTranslation : AbstractModel, IEntityTranslation
    {
        public string Lang { get; set; } = string.Empty;
        Guid IEntityTranslation.EntityGuid { get; set; }
        string IEntityTranslation.EntityType { get; set; } = string.Empty;
        string IEntityTranslation.FieldName { get; set; } = string.Empty;
        string IEntityTranslation.Culture { get => Lang; set => Lang = value; }
        string IEntityTranslation.Value { get; set; } = string.Empty;
        DateTime? IEntityTranslation.UpdatedAt { get; set; }
        string IEntityTranslation.ValueHash { get; set; } = string.Empty;
    }

    [Fact]
    public void A_model_implementing_the_interface_explicitly_is_refused_when_the_wrapper_is_built()
    {
        var act = () => new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel, ExplicitTranslation>(
            new InMemoryBulkStore<TestLocalizableModel>(), new InMemoryBulkStore<ExplicitTranslation>(), _context);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ExplicitTranslation*EntityGuid*");
    }

    // ---------------------------------------------------------------- SH-M074

    /// <summary>
    /// Simulates the race the unique key resolves: just before our create, another writer stores the same key, and the
    /// store refuses ours as a duplicate.
    /// </summary>
    private static void RaceOnce(InMemoryBulkStore<EntityTranslationModel> store, string otherValue)
    {
        var raced = false;
        store.BeforeCreate = row =>
        {
            if (raced)
            {
                return;
            }
            raced = true;
            store.BeforeCreate = null;
            store.Create(new EntityTranslationModel
            {
                EntityGuid = row.EntityGuid, EntityType = row.EntityType, FieldName = row.FieldName, Culture = row.Culture, Value = otherValue,
            });
            throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));
        };
    }

    [Fact]
    public void SH_M074_A_create_refused_by_the_unique_key_updates_the_row_the_other_writer_created()
    {
        var entities = new InMemoryBulkStore<TestLocalizableModel>();
        var translations = new InMemoryBulkStore<EntityTranslationModel>();
        var bulk = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(entities, translations, _context);
        RaceOnce(translations, "other writer");

        var guid = bulk.Create(new TestLocalizableModel { Name = "Stolicka" });

        translations.Read().Where(t => t.EntityGuid == guid && t.FieldName == "Name")
            .Select(t => t.Value).Should().Equal("Stolicka");
    }

    [Fact]
    public void SH_M074_A_unique_refusal_with_no_row_to_update_is_not_swallowed()
    {
        var entities = new InMemoryBulkStore<TestLocalizableModel>();
        var translations = new InMemoryBulkStore<EntityTranslationModel>();
        var bulk = new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(entities, translations, _context);
        translations.BeforeCreate = _ => throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));

        var act = () => bulk.Create(new TestLocalizableModel { Name = "Stolicka" });

        act.Should().Throw<StoreUniqueViolationException>();
    }

    [Fact]
    public async Task SH_M074_The_async_upsert_retries_as_an_update_too()
    {
        var entities = new InMemoryAsyncBulkStore<TestLocalizableModel>();
        var translations = new InMemoryAsyncBulkStore<EntityTranslationModel>();
        var bulk = new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(entities, translations, _context);
        var raced = false;
        translations.BeforeCreate = row =>
        {
            if (raced)
            {
                return;
            }
            raced = true;
            translations.BeforeCreate = null;
            translations.CreateAsync(new EntityTranslationModel
            {
                EntityGuid = row.EntityGuid, EntityType = row.EntityType, FieldName = row.FieldName, Culture = row.Culture, Value = "other writer",
            }).GetAwaiter().GetResult();
            throw StoreException.DuplicateCreate(typeof(EntityTranslationModel));
        };

        var guid = await bulk.CreateAsync(new TestLocalizableModel { Name = "Stolicka" });

        (await translations.ReadAsync(filter: null, orderBy: null, limit: null, offset: null))
            .Where(t => t.EntityGuid == guid && t.FieldName == "Name").Select(t => t.Value).Should().Equal("Stolicka");
    }
}
