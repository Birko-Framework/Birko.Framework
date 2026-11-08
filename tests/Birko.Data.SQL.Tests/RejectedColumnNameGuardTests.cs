using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-862 — the guard in <c>Birko.Data.SQL</c>, tested here and not only from the provider suites that use it
/// (§ TASK-255: a guard whose only coverage lives downstream can be deleted by a downstream cleanup). A connector with
/// a list refuses a listed column by name, case-insensitively, before its emitter runs; one without a list refuses
/// nothing; and the check is not virtual, so a provider cannot override it away (rule 12).
/// </summary>
public class RejectedColumnNameGuardTests
{
    [Table("T862GuardRows")]
    public class GuardRow : AbstractModel
    {
        public int order { get; set; }
    }

    [Table("T862GuardCleanRows")]
    public class GuardCleanRow : AbstractModel
    {
        public int Amount { get; set; }
    }

    [Table("T862GuardNamedRows")]
    public class GuardNamedRow : AbstractModel
    {
        [NamedField("SortOrder")]
        public int Order { get; set; }
    }

    private sealed class ListingConnector : AbstractConnector
    {
        private readonly RejectedColumnNames? _refusals;
        public ListingConnector(RejectedColumnNames? refusals) : base(new Birko.Configuration.PasswordSettings()) => _refusals = refusals;

        public readonly List<string> Emitted = new();
        protected override RejectedColumnNames? ColumnNameRefusals => _refusals;

        protected override void CreateTableCore(string name, IEnumerable<string> fields) => Emitted.Add(name);

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new InvalidOperationException("no database in this test");
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field) => "INT";
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field) => field.Name + " INT";
    }

    private static readonly RejectedColumnNames Fake = new("FakeSQL", "test", new[] { "ORDER", "GROUP" });

    [Fact]
    public void A_listed_column_is_refused_by_name_case_insensitively_before_the_emitter_runs()
    {
        var connector = new ListingConnector(Fake);

        var act = () => connector.CreateTable(new[] { typeof(GuardCleanRow), typeof(GuardRow) });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("Column \"order\" of entity *GuardRow (property order, table \"T862GuardRows\") is a reserved word on FakeSQL*"
                + "[NamedField(\"...\")]*Property(x => x.order).HasColumnName(\"...\")*");
        connector.Emitted.Should().BeEmpty("the clean table in the same batch is not created either");
    }

    [Fact]
    public void A_column_mapped_to_another_name_is_judged_by_that_name()
    {
        var connector = new ListingConnector(Fake);

        connector.CreateTable(new[] { typeof(GuardNamedRow) });

        connector.Emitted.Should().Equal("T862GuardNamedRows");
    }

    [Fact]
    public void A_connector_without_a_list_refuses_nothing()
    {
        var connector = new ListingConnector(null);

        connector.CreateTable(new[] { typeof(GuardRow) });

        connector.Emitted.Should().Equal("T862GuardRows");
        connector.RejectsColumnName("Order").Should().BeFalse();
    }

    [Fact]
    public void AlterTableAdd_refuses_before_it_opens_a_connection()
    {
        var connector = new ListingConnector(Fake);
        var field = global::Birko.Data.SQL.DataBase.LoadTable(typeof(GuardRow)).Fields.Values.Single(f => f.Name == "order");

        // The fake's CreateConnection throws InvalidOperationException, so reaching it would fail with that type.
        FluentActions.Invoking(() => connector.AlterTableAdd("T862GuardRows", new[] { field }))
            .Should().Throw<NotSupportedException>().WithMessage("*\"order\"*FakeSQL*");
        FluentActions.Invoking(() => connector.RequireUsableColumnName("T862GuardRows", "group"))
            .Should().Throw<NotSupportedException>().WithMessage("Column \"group\" of table \"T862GuardRows\"*FakeSQL*");
    }

    [Fact]
    public void The_checks_are_not_virtual()
    {
        foreach (var name in new[] { nameof(AbstractConnectorBase.RequireUsableColumnNames), nameof(AbstractConnectorBase.RequireUsableColumnName), nameof(AbstractConnectorBase.RejectsColumnName) })
        {
            typeof(AbstractConnectorBase).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)!
                .IsVirtual.Should().BeFalse(name + " is the check; the list is the override point");
        }
    }
}
