using System;
using System.Linq.Expressions;
using FluentAssertions;
using Xunit;
using SqlView = Birko.Data.SQL.Tables.View;
using SqlTable = Birko.Data.SQL.Tables.Table;
using SqlDataBase = Birko.Data.SQL.DataBase;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// CR-M147: the View constructor overwrote an explicit name with the concatenated table names.
/// CR-M148: GetViewField cast the lambda body to UnaryExpression unconditionally (InvalidCastException
/// for a plain MemberExpression body) and returned null! (NRE at the caller). Both are offline-testable
/// because SQL.Tests compiles the Birko.Data.SQL.View projitems.
/// </summary>
public class ViewNameAndFieldTests
{
    private sealed class Sample
    {
        public string? Title { get; set; }
    }

    private static SqlTable T(string name) => new() { Name = name, Type = typeof(Sample) };

    [Fact]
    public void View_keeps_an_explicit_name()
    {
        var view = new SqlView(new[] { T("Orders"), T("Customers") }, join: null, name: "OrderSummary");

        view.Name.Should().Be("OrderSummary", "CR-M147: an explicit name must not be overwritten");
    }

    [Fact]
    public void View_derives_a_name_from_tables_when_none_supplied()
    {
        var view = new SqlView(new[] { T("Orders"), T("Customers") }, join: null, name: null);

        view.Name.Should().Be("OrdersCustomers");
    }

    [Fact]
    public void View_keeps_an_explicit_name_even_with_no_tables()
    {
        new SqlView(tables: null, join: null, name: "Standalone").Name.Should().Be("Standalone");
    }

    [Fact]
    public void GetViewField_plain_member_body_throws_descriptively_not_invalidcast()
    {
        // x => x.Title (reference-type property, no boxing) is a plain MemberExpression body — the old
        // unconditional (UnaryExpression) cast threw InvalidCastException here. It now parses the member
        // and, lacking a [ViewField] mapping, throws a descriptive InvalidOperationException (not null!).
        Action act = () => SqlDataBase.GetViewField<Sample, string?>(x => x.Title);

        act.Should().Throw<InvalidOperationException>();
        act.Should().NotThrow<InvalidCastException>();
    }
}
