using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Views;
using Birko.Data.Views;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using PortableQueryMode = Birko.Data.Views.ViewQueryMode;
using SqlQueryMode = Birko.Data.SQL.ViewQueryMode;

namespace Birko.Data.SQL.Views.Tests;

/// <summary>
/// CR-M154: pure-logic coverage of <see cref="SqlViewTranslator.Translate"/> — name derivation
/// (explicit vs concatenated table names), query-mode translation, the COUNT(*) aggregate path, and
/// the null guard. The field / join / aggregate <em>mapping</em> is exercised end-to-end by
/// <c>SqlViewStoreAsyncTests</c> (which runs a real join view over SQLite through this translator).
/// </summary>
public class SqlViewTranslatorTests
{
    public class TransOrder : AbstractModel
    {
        public Guid PersonId { get; set; }
        public decimal Amount { get; set; }
    }

    public class TransPerson : AbstractModel
    {
        public string? Name { get; set; }
    }

    public class TransView
    {
        public Guid? OrderId { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public int OrderCount { get; set; }
    }

    private sealed class TransOrderMapping : IModelMapping<TransOrder>
    {
        public void Configure(ModelMap<TransOrder> map)
        {
            map.ToTable("TransOrders").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.PersonId);
            map.Property(x => x.Amount);
        }
    }

    private sealed class TransPersonMapping : IModelMapping<TransPerson>
    {
        public void Configure(ModelMap<TransPerson> map)
        {
            map.ToTable("TransPersons").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    public SqlViewTranslatorTests()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new TransOrderMapping());
        registry.Register(new TransPersonMapping());
        registry.ApplyToDatabase();
    }

    private static ViewDefinitionBuilder<TransView> BaseBuilder()
        => new ViewDefinitionBuilder<TransView>()
            .From<TransOrder>()
            .Join<TransOrder, TransPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<TransOrder, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<TransPerson, string>(p => p.Name!, v => v.PersonName);

    [Fact]
    public void Translate_Null_Throws()
    {
        Action act = () => SqlViewTranslator.Translate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Translate_ExplicitName_IsHonored()
    {
        var view = SqlViewTranslator.Translate(BaseBuilder().HasName("MyOrders").Build());
        view.Should().NotBeNull();
        view.Name.Should().Be("MyOrders");
    }

    [Fact]
    public void Translate_DerivesNameFromTableNames_WhenNoneGiven()
    {
        var view = SqlViewTranslator.Translate(BaseBuilder().Build());
        view.Name.Should().NotBeNullOrWhiteSpace();
        view.Name.Should().Contain("TransOrders");
        view.Name.Should().Contain("TransPersons");
    }

    [Theory]
    [InlineData(PortableQueryMode.OnTheFly, SqlQueryMode.OnTheFly)]
    [InlineData(PortableQueryMode.Persistent, SqlQueryMode.Persistent)]
    [InlineData(PortableQueryMode.Auto, SqlQueryMode.Auto)]
    public void Translate_TranslatesQueryMode(PortableQueryMode portable, SqlQueryMode expected)
    {
        var view = SqlViewTranslator.Translate(BaseBuilder().HasName("V").HasQueryMode(portable).Build());
        view.QueryMode.Should().Be(expected);
    }

    [Fact]
    public void Translate_GroupByFieldNotSelected_Throws()
    {
        // CR-M153: grouping by a field that is not also selected used to be silently dropped (the
        // connector groups by the SELECTed non-aggregate fields), giving wrong aggregates. Now rejected.
        var def = new ViewDefinitionBuilder<TransView>()
            .HasName("BadGroup")
            .From<TransOrder>()
            .Join<TransOrder, TransPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<TransPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<TransPerson, string>(p => p.Name!)   // selected + grouped — fine
            .GroupBy<TransOrder, decimal>(o => o.Amount)  // grouped but NOT selected — must be rejected
            .Count<TransOrder>(v => v.OrderCount)
            .Build();

        Action act = () => SqlViewTranslator.Translate(def);
        act.Should().Throw<NotSupportedException>().WithMessage("*Amount*");
    }

    [Fact]
    public void Translate_WellFormedGroupedAggregate_Succeeds()
    {
        // Every GroupBy field is also selected → honored, no throw.
        var def = new ViewDefinitionBuilder<TransView>()
            .HasName("GroupedOk")
            .From<TransOrder>()
            .Join<TransOrder, TransPerson, Guid?>(o => o.PersonId, p => p.Guid)
            .Select<TransPerson, string>(p => p.Name!, v => v.PersonName)
            .GroupBy<TransPerson, string>(p => p.Name!)
            .Count<TransOrder>(v => v.OrderCount)
            .Build();

        var view = SqlViewTranslator.Translate(def);
        view.Name.Should().Be("GroupedOk");
    }

    [Fact]
    public void Translate_WithCountAggregate_ProducesView()
    {
        // Exercises the COUNT(*) base-field-selection branch (agg.SourceProperty == null). No plain
        // Select() calls — the builder requires every non-aggregate field to be in GroupBy otherwise.
        var view = SqlViewTranslator.Translate(
            new ViewDefinitionBuilder<TransView>()
                .HasName("Counted")
                .From<TransOrder>()
                .Count<TransOrder>(v => v.OrderCount)
                .Build());

        view.Should().NotBeNull();
        view.Name.Should().Be("Counted");
    }
}
