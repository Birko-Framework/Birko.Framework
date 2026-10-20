using System;
using System.Globalization;
using Birko.Data.Localization.Decorators;
using Birko.Data.Localization.Models;
using Birko.Data.Localization.Tests.TestResources;
using Birko.Data.Localization.Translations;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// TASK-698. A translation row is created under <see cref="LocalizedTranslationRows.KeyGuid"/>, a name-based UUID of
/// its key, so two writers of one key collide on the Guid on every provider. A soft-delete-wrapped translation store is
/// refused, because a hidden row keeps its key (decided by the user). The race itself runs per provider in the shared
/// TranslationRaceContract.
/// </summary>
public class TranslationKeyGuidTests
{
    private static readonly Guid Entity = new("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void The_key_guid_is_the_RFC_4122_version_5_uuid_of_the_key()
    {
        // Cross-checked with Python: uuid.uuid5(UUID("756ec46f-0f20-450f-85e0-d8234384440b"),
        //   "11111111-2222-3333-4444-555555555555\x1fProduct\x1fName\x1fsk")
        LocalizedTranslationRows.KeyGuid(Entity, "Product", "Name", "sk")
            .Should().Be(new Guid("f30bafdf-48d5-513c-a663-846f9bb7f753"));
    }

    [Fact]
    public void The_key_guid_is_deterministic_and_carries_version_5_and_the_RFC_variant()
    {
        var guid = LocalizedTranslationRows.KeyGuid(Entity, "Product", "Name", "sk");

        LocalizedTranslationRows.KeyGuid(Entity, "Product", "Name", "sk").Should().Be(guid);
        guid.Version.Should().Be(5);
        guid.Variant.Should().BeInRange(8, 11, "the RFC 4122 variant is 10xx");
    }

    [Fact]
    public void Each_part_of_the_key_changes_the_guid()
    {
        var guid = LocalizedTranslationRows.KeyGuid(Entity, "Product", "Name", "sk");

        LocalizedTranslationRows.KeyGuid(Guid.NewGuid(), "Product", "Name", "sk").Should().NotBe(guid);
        LocalizedTranslationRows.KeyGuid(Entity, "Category", "Name", "sk").Should().NotBe(guid);
        LocalizedTranslationRows.KeyGuid(Entity, "Product", "Slug", "sk").Should().NotBe(guid);
        LocalizedTranslationRows.KeyGuid(Entity, "Product", "Name", "cs").Should().NotBe(guid);
        LocalizedTranslationRows.KeyGuid(Entity, "Product", "Name", "SK").Should().NotBe(guid, "the values are taken exactly as stored");
    }

    [Fact]
    public void The_separator_keeps_distinct_keys_apart()
    {
        LocalizedTranslationRows.KeyGuid(Entity, "ProductName", "x", "sk")
            .Should().NotBe(LocalizedTranslationRows.KeyGuid(Entity, "Product", "Namex", "sk"));
    }

    [Fact]
    public void A_key_without_an_entity_type_or_field_is_refused()
    {
        FluentActions.Invoking(() => LocalizedTranslationRows.KeyGuid(Entity, "", "Name", "sk")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => LocalizedTranslationRows.KeyGuid(Entity, "Product", "", "sk")).Should().Throw<ArgumentException>();
    }

    private sealed class SoftDeletingTranslations : InMemoryAsyncBulkStore<EntityTranslationModel>, ISoftDeleteStoreWrapper
    {
        public object? GetInnerStore() => null;
    }

    private sealed class SyncSoftDeletingTranslations : InMemoryBulkStore<EntityTranslationModel>, ISoftDeleteStoreWrapper
    {
        public object? GetInnerStore() => null;
    }

    private sealed class Wrapper : IStoreWrapper
    {
        private readonly object _inner;
        public Wrapper(object inner) => _inner = inner;
        public object? GetInnerStore() => _inner;
    }

    private static TestEntityLocalizationContext Context() => new() { CurrentCulture = new CultureInfo("en"), DefaultCulture = new CultureInfo("en") };

    [Fact]
    public void A_soft_delete_decorator_anywhere_in_the_translation_chain_is_refused()
    {
        FluentActions.Invoking(() => LocalizedTranslationRows.RequireNoSoftDelete(new Wrapper(new Wrapper(new SoftDeletingTranslations()))))
            .Should().Throw<InvalidOperationException>().WithMessage("*soft-delete*");
        FluentActions.Invoking(() => LocalizedTranslationRows.RequireNoSoftDelete(new InMemoryAsyncBulkStore<EntityTranslationModel>()))
            .Should().NotThrow();
    }

    [Fact]
    public void Every_localized_entry_point_refuses_a_soft_deleting_translation_store()
    {
        var entities = new InMemoryAsyncBulkStore<TestLocalizableModel>();
        var syncEntities = new InMemoryBulkStore<TestLocalizableModel>();

        FluentActions.Invoking(() => new AsyncLocalizedBulkStoreWrapper<IAsyncBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            entities, new SoftDeletingTranslations(), Context())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new AsyncLocalizedStoreWrapper<IAsyncStore<TestLocalizableModel>, TestLocalizableModel>(
            entities, new SoftDeletingTranslations(), Context())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new LocalizedBulkStoreWrapper<IBulkStore<TestLocalizableModel>, TestLocalizableModel>(
            syncEntities, new SyncSoftDeletingTranslations(), Context())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new LocalizedStoreWrapper<IStore<TestLocalizableModel>, TestLocalizableModel>(
            syncEntities, new SyncSoftDeletingTranslations(), Context())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new EntityTranslations<TestLocalizableModel>(entities, new SoftDeletingTranslations(), Context()))
            .Should().Throw<InvalidOperationException>();
    }
}
