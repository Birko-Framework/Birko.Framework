using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using PortableViewQueryMode = Birko.Data.Views.ViewQueryMode;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// TASK-207 — <c>View.AddField</c> silently dropped a field whose dictionary key was already present:
/// no column in the view, no exception, no log entry, and the view property it belonged to read back as
/// <c>default(T)</c>.
///
/// <para>
/// <b>The mechanism.</b> Each of a view's tables holds a <c>Dictionary&lt;string, AbstractField&gt;</c> of the
/// columns it contributes, and <c>AddField</c> ended with <c>if (!table.Fields.ContainsKey(fieldName))</c> —
/// a key already present meant the incoming field was discarded. [[TASK-129]] measured this for aggregates
/// (two <c>Sum</c>s on one table, both keyed <c>"SUM"</c>, second gone) and closed that instance by keying
/// aggregates on their view property. <b>The guard itself was unchanged</b>, so two other collision shapes
/// stayed live:
/// </para>
///
/// <list type="number">
/// <item><b>Non-aggregate vs non-aggregate.</b> A non-aggregate was keyed on <c>field.Name</c> — the
/// <i>source column</i> — so two view properties projecting the same source column
/// (<c>Select(p =&gt; p.Name, v =&gt; v.DisplayName)</c> and <c>Select(p =&gt; p.Name, v =&gt; v.SortName)</c>)
/// collided and the second never populated. <c>ViewDefinitionBuilder.Build</c> does not reject it, so it is
/// reachable straight off the public fluent API.</item>
/// <item><b>Aggregate vs non-aggregate — introduced by TASK-129's own keying change.</b> That task keys
/// aggregates by <b>view property</b> while non-aggregates in the <i>same dictionary</i> stay keyed by
/// <b>source column</b>. Two namespaces in one key space: a view selecting <c>Order.Total → OrderTotal</c>
/// (keyed <c>Total</c>) alongside <c>Sum(Order.Amount) → Total</c> (keyed <c>Total</c>) collides, and
/// whichever is added second is dropped. TASK-129's comments claimed view properties are "unique by
/// construction", which is true among view properties and not against the source-column keys beside them.</item>
/// </list>
///
/// <para>
/// <b>The fix, and why it is not simply a throw.</b> Every view field is now keyed by the property it
/// populates, so both shapes cannot occur rather than being reported. A throw alone was rejected because the
/// guard is load-bearing for three legitimate <i>re-add</i> paths, one of which the task's own scoping
/// missed: <c>ViewAttribute</c> is <c>AllowMultiple = true</c>, so <c>LoadView</c> runs its whole field loop
/// once per <c>[View]</c> attribute and re-adds every field on the second pass — as a fresh
/// <c>AbstractField</c> instance, so reference equality cannot tell it from a collision. A throw is kept as a
/// backstop for a genuinely <i>different</i> field arriving on a key already held, which is what
/// <c>AddTable</c> (keyed by source column, outside the view builders) can still present.
/// </para>
/// </summary>
public class ViewFieldKeyCollisionTests : IDisposable
{
    private readonly string _root;

    public ViewFieldKeyCollisionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-viewkey-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    // ─────────────────────────────────────────────────────────────────── models

    public class VkPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class VkOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Total { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>Shape 1: two view properties projecting the SAME source column.</summary>
    public class VkTwoNamesView
    {
        public string DisplayName { get; set; } = string.Empty;
        public string SortName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Shape 2: a non-aggregate keyed on its source column <c>Total</c> beside an aggregate keyed on its
    /// view property <c>Total</c>. The name coincidence is the whole point — the two keys come from
    /// different namespaces sharing one dictionary.
    /// </summary>
    public class VkCollidingView
    {
        public string PersonName { get; set; } = string.Empty;
        public decimal OrderTotal { get; set; }
        public decimal Total { get; set; }
    }

    private sealed class VkPersonMapping : IModelMapping<VkPerson>
    {
        public void Configure(ModelMap<VkPerson> map)
        {
            map.ToTable("VkPersons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private sealed class VkOrderMapping : IModelMapping<VkOrder>
    {
        public void Configure(ModelMap<VkOrder> map)
        {
            map.ToTable("VkOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Total);
            map.Property(x => x.Amount);
        }
    }

    private static void RegisterMappings()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new VkPersonMapping());
        registry.Register(new VkOrderMapping());
        registry.ApplyToDatabase();
    }

    /// <summary>One person, one order, with Total and Amount distinct so a dropped column is observable.</summary>
    private SqLiteConnector Seed()
    {
        RegisterMappings();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "viewkey.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(VkPerson), typeof(VkOrder) });

        var person = new VkPerson { Guid = Guid.NewGuid(), Name = "alice" };
        connector.Insert(typeof(VkPerson), person);
        connector.Insert(typeof(VkOrder), new VkOrder
        {
            Guid = Guid.NewGuid(),
            PersonId = person.Guid!.Value,
            Total = 70m,
            Amount = 5m,
        });
        return connector;
    }

    // Joined to VkOrder rather than selecting from VkPerson alone: the view SELECT path requires a Join
    // (AbstractConnector.CreateSelectCommand throws ArgumentNullException("Join") without one), so a
    // single-table view could not exercise the read-back at all.
    private static ViewDefinition TwoNamesDefinition()
        => new ViewDefinitionBuilder<VkTwoNamesView>()
            .HasName("VkTwoNames")
            .HasQueryMode(PortableViewQueryMode.OnTheFly)
            .From<VkOrder>()
            .Join<VkOrder, VkPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<VkPerson, string>(p => p.Name!, v => v.DisplayName)
            .Select<VkPerson, string>(p => p.Name!, v => v.SortName)
            .Build();

    private static ViewDefinition CollidingDefinition(PortableViewQueryMode mode)
        => new ViewDefinitionBuilder<VkCollidingView>()
            .HasName("VkColliding")
            .HasQueryMode(mode)
            .From<VkOrder>()
            .Join<VkOrder, VkPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<VkPerson, string>(p => p.Name!, v => v.PersonName)
            .Select<VkOrder, decimal>(o => o.Total, v => v.OrderTotal)
            .GroupBy<VkPerson, string>(p => p.Name!)
            .GroupBy<VkOrder, decimal>(o => o.Total)
            .Sum<VkOrder, decimal>(o => o.Amount, v => v.Total)
            .Build();

    // ────────────────────────────────── shape 1: non-aggregate vs non-aggregate

    [Fact]
    public void Two_view_properties_over_one_source_column_both_survive_translation()
    {
        // Before the fix both were keyed "Name" (the source column) and the second was dropped: the view
        // contributed ONE column, and SortName read back as null.
        RegisterMappings();

        var view = SqlViewTranslator.Translate(TwoNamesDefinition());

        var properties = view.GetTableFields().Select(f => f.Property.Name).ToList();
        properties.Should().BeEquivalentTo(new[] { "DisplayName", "SortName" });
    }

    [Fact]
    public void Two_view_properties_over_one_source_column_both_read_back_their_value()
    {
        // "Both columns exist" is not "both columns are correct" — this reads the rows out of SQLite.
        // Both properties read "alice", which for the two-Sum shape would be a weak assertion (a dropped
        // column and a duplicated one look alike). Here they project the SAME source column, so equal
        // values ARE the correct answer, and the pre-fix failure is unambiguous: SortName was dropped and
        // read back as null, not as "alice". The structural claim — that the two are distinct fields — is
        // asserted by the translation test above.
        var connector = Seed();
        var definition = TwoNamesDefinition();
        var store = new SqlViewStore<VkTwoNamesView>(connector, definition);

        var rows = store.QueryAsync(null, null, null, null).GetAwaiter().GetResult().ToList();

        rows.Should().HaveCount(1);
        rows[0].DisplayName.Should().Be("alice");
        rows[0].SortName.Should().Be("alice");
    }

    // ────────────────────────────────── shape 2: aggregate vs non-aggregate (TASK-129 created this one)

    [Fact]
    public void An_aggregate_whose_view_property_matches_another_columns_source_name_survives()
    {
        // The namespace clash TASK-129 introduced: OrderTotal is keyed "Total" (its source column) and the
        // Sum is keyed "Total" (its view property). Before the fix the Sum was added second and dropped.
        RegisterMappings();

        var view = SqlViewTranslator.Translate(CollidingDefinition(PortableViewQueryMode.OnTheFly));

        var properties = view.GetTableFields().Select(f => f.Property.Name).ToList();
        properties.Should().BeEquivalentTo(new[] { "PersonName", "OrderTotal", "Total" });
    }

    [Fact]
    public void The_colliding_aggregate_and_non_aggregate_read_back_their_own_values()
    {
        // Total (SUM of Amount) is 5 and OrderTotal (the source column) is 70, so a dropped column reads 0
        // and a mis-wired one reads the other's number. Only correct keying gives 70/5.
        //
        // OnTheFly, deliberately. A Persistent variant would exercise the DDL builder, but this fixture's
        // non-aggregate `OrderTotal` is a PascalCase column and the DDL projects those unquoted while the
        // persistent read quotes them — TASK-209, open and unfixed. SQLite is case-insensitive for
        // identifiers so such a test would pass, encoding the broken behaviour as though it were correct.
        // TASK-129 declined to assert that either way for the same reason; this follows it.
        var connector = Seed();
        var definition = CollidingDefinition(PortableViewQueryMode.OnTheFly);
        var store = new SqlViewStore<VkCollidingView>(connector, definition);

        var rows = store.QueryAsync(null, null, null, null).GetAwaiter().GetResult().ToList();

        rows.Should().HaveCount(1);
        rows[0].PersonName.Should().Be("alice");
        rows[0].OrderTotal.Should().Be(70m);
        rows[0].Total.Should().Be(5m);
    }

    // ────────────────────────────────── the attribute-driven builder has the same two shapes

    [Table("VkaPersons")]
    public class VkaPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    [Table("VkaOrders")]
    public class VkaOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Total { get; set; }
        public decimal Amount { get; set; }
    }

    [View(typeof(VkaPerson), typeof(VkaOrder), nameof(VkaPerson.Guid), nameof(VkaOrder.PersonId), name: "VkaTwoNames")]
    public class VkaTwoNamesView
    {
        [ViewField(typeof(VkaPerson), nameof(VkaPerson.Name))]
        public string DisplayName { get; set; } = string.Empty;

        [ViewField(typeof(VkaPerson), nameof(VkaPerson.Name))]
        public string SortName { get; set; } = string.Empty;
    }

    [View(typeof(VkaPerson), typeof(VkaOrder), nameof(VkaPerson.Guid), nameof(VkaOrder.PersonId), name: "VkaColliding")]
    public class VkaCollidingView
    {
        [ViewField(typeof(VkaOrder), nameof(VkaOrder.Total))]
        public decimal OrderTotal { get; set; }

        [SumField(typeof(VkaOrder), nameof(VkaOrder.Amount))]
        public decimal Total { get; set; }
    }

    [Fact]
    public void The_attribute_builder_keeps_two_view_properties_over_one_source_column()
    {
        RegisterMappings();

        var view = DataBase.LoadView(typeof(VkaTwoNamesView));

        view.Should().NotBeNull();
        view!.GetTableFields().Select(f => f.Property.Name)
            .Should().BeEquivalentTo(new[] { "DisplayName", "SortName" });
    }

    [Fact]
    public void The_attribute_builder_keeps_a_colliding_aggregate_and_non_aggregate()
    {
        RegisterMappings();

        var view = DataBase.LoadView(typeof(VkaCollidingView));

        view.Should().NotBeNull();
        view!.GetTableFields().Select(f => f.Property.Name)
            .Should().BeEquivalentTo(new[] { "OrderTotal", "Total" });
    }

    // ────────────────────────────────── the re-add paths that must stay silent

    [Table("VkmPersons")]
    public class VkmPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    [Table("VkmOrders")]
    public class VkmOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    [Table("VkmRegions")]
    public class VkmRegion : AbstractModel
    {
        public Guid PersonId { get; set; }
        public string? Code { get; set; }
    }

    /// <summary>
    /// Two <c>[View]</c> attributes — <c>ViewAttribute</c> is <c>AllowMultiple = true</c>, which is how a
    /// three-table view declares its second join. <c>LoadView</c> runs the whole per-property field loop once
    /// per attribute, so every field is added twice, the second time as a <b>fresh</b> <c>AbstractField</c>
    /// instance carrying the same name and property. This is the legitimate re-add the task's scoping missed;
    /// an unconditional throw on a duplicate key would break every view of this shape.
    /// </summary>
    [View(typeof(VkmPerson), typeof(VkmOrder), nameof(VkmPerson.Guid), nameof(VkmOrder.PersonId), name: "VkmMulti")]
    [View(typeof(VkmPerson), typeof(VkmRegion), nameof(VkmPerson.Guid), nameof(VkmRegion.PersonId), name: "VkmMulti")]
    public class VkmMultiAttributeView
    {
        [ViewField(typeof(VkmPerson), nameof(VkmPerson.Name))]
        public string PersonName { get; set; } = string.Empty;

        [SumField(typeof(VkmOrder), nameof(VkmOrder.Amount))]
        public decimal TotalAmount { get; set; }

        [ViewField(typeof(VkmRegion), nameof(VkmRegion.Code))]
        public string RegionCode { get; set; } = string.Empty;
    }

    // ────────────────────────────────── the backstop, for a key the view builders no longer produce

    [Fact]
    public void A_genuinely_different_field_on_a_taken_key_is_refused_rather_than_dropped()
    {
        // Keying by view property removes both reachable collision shapes, so nothing in the view builders
        // can reach this any more. It stays because AddField is public and takes an explicit `name`, and
        // because AddTable keys source fields by their source property — a caller can still present one key
        // for two different fields, and the old behaviour for that was to discard the second in silence.
        RegisterMappings();
        var orders = DataBase.LoadTable(typeof(VkOrder))!;
        var total = orders.GetFieldByPropertyName(nameof(VkOrder.Total))!;
        var amount = orders.GetFieldByPropertyName(nameof(VkOrder.Amount))!;
        var view = new Birko.Data.SQL.Tables.View();
        view.AddField(orders.Name, orders.Type, total, "SharedKey");

        var collide = () => view.AddField(orders.Name, orders.Type, amount, "SharedKey");

        collide.Should().Throw<Birko.Data.Exceptions.FieldAttributeException>()
            .WithMessage("*SharedKey*")
            .WithMessage("*Total*")
            .WithMessage("*Amount*");
    }

    [Fact]
    public void Re_adding_the_very_same_field_on_a_taken_key_stays_silent()
    {
        // The other half of the backstop: idempotent re-adds are not collisions. Asserted separately from
        // the multi-[View] test so a regression tells you which of the two rules broke.
        RegisterMappings();
        var orders = DataBase.LoadTable(typeof(VkOrder))!;
        var total = orders.GetFieldByPropertyName(nameof(VkOrder.Total))!;
        var view = new Birko.Data.SQL.Tables.View();
        view.AddField(orders.Name, orders.Type, total, "SharedKey");

        var again = () => view.AddField(orders.Name, orders.Type, total, "SharedKey");

        again.Should().NotThrow();
        view.GetTableFields().Should().HaveCount(1);
    }

    [Fact]
    public void A_view_with_two_view_attributes_still_loads_and_keeps_each_field_once()
    {
        // The blast-radius check for the backstop throw. The second attribute pass re-presents every field
        // on a key already held; that is an idempotent re-add, not a collision, and must stay silent.
        RegisterMappings();

        var load = () => DataBase.LoadView(typeof(VkmMultiAttributeView));

        var view = load.Should().NotThrow().Subject;
        view.Should().NotBeNull();
        view!.GetTableFields().Select(f => f.Property.Name)
            .Should().BeEquivalentTo(new[] { "PersonName", "TotalAmount", "RegionCode" });
    }
}
