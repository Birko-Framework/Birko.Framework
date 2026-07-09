using System;
using System.Linq;
using Birko.Data.MongoDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using MongoDB.Bson;
using Xunit;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// CR-H073: MongoViewTranslator.TranslatePipeline is pure ($lookup/$unwind/$group/$project builder)
/// and shipped with zero coverage. These offline tests pin its output — including the Guid→_id
/// mapping, LeftOuter's preserveNullAndEmptyArrays, and $group emission for aggregates.
/// </summary>
public class MongoViewTranslatorTests
{
    private class Order
    {
        public Guid? Guid { get; set; }
        public Guid CustomerId { get; set; }
        public decimal Amount { get; set; }
    }

    private class Customer
    {
        public Guid? Guid { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private class OrderView
    {
        public Guid? OrderId { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private class CustomerTotalView
    {
        public Guid CustomerId { get; set; }
        public decimal Total { get; set; }
    }

    [Fact]
    public void Null_definition_throws()
    {
        Action act = () => MongoViewTranslator.TranslatePipeline(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetCollectionName_uses_type_name()
    {
        MongoViewTranslator.GetCollectionName(typeof(Order)).Should().Be("Order");
    }

    [Fact]
    public void Simple_projection_emits_only_project_with_guid_mapped_to_id()
    {
        var def = new ViewDefinitionBuilder<OrderView>()
            .From<Order>()
            .Select<Order, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<Order, decimal>(o => o.Amount, v => v.Amount)
            .Build();

        var pipeline = MongoViewTranslator.TranslatePipeline(def);

        pipeline.Should().HaveCount(1);
        var project = pipeline[0]["$project"].AsBsonDocument;
        project["_id"].AsInt32.Should().Be(0);
        project["OrderId"].AsString.Should().Be("$_id");        // Guid -> _id
        project["Amount"].AsString.Should().Be("$Amount");
    }

    [Fact]
    public void LeftJoin_emits_lookup_and_unwind_preserving_empty_arrays()
    {
        var def = new ViewDefinitionBuilder<OrderView>()
            .From<Order>()
            .LeftJoin<Order, Customer, Guid?>(o => o.CustomerId, c => c.Guid)
            .Select<Order, Guid?>(o => o.Guid, v => v.OrderId)
            .Select<Customer, string>(c => c.Name, v => v.CustomerName)
            .Build();

        var pipeline = MongoViewTranslator.TranslatePipeline(def);

        // $lookup, $unwind, $project
        pipeline.Should().HaveCount(3);

        var lookup = pipeline[0]["$lookup"].AsBsonDocument;
        lookup["from"].AsString.Should().Be("Customer");
        lookup["localField"].AsString.Should().Be("CustomerId");
        lookup["foreignField"].AsString.Should().Be("_id");     // c.Guid -> _id
        lookup["as"].AsString.Should().Be("_joined_Customer");

        var unwind = pipeline[1]["$unwind"].AsBsonDocument;
        unwind["path"].AsString.Should().Be("$_joined_Customer");
        unwind["preserveNullAndEmptyArrays"].AsBoolean.Should().BeTrue();

        var project = pipeline[2]["$project"].AsBsonDocument;
        project["OrderId"].AsString.Should().Be("$_id");
        project["CustomerName"].AsString.Should().Be("$_joined_Customer.Name");
    }

    [Fact]
    public void InnerJoin_unwind_does_not_preserve_empty_arrays()
    {
        var def = new ViewDefinitionBuilder<OrderView>()
            .From<Order>()
            .Join<Order, Customer, Guid?>(o => o.CustomerId, c => c.Guid, JoinType.Inner)
            .Select<Order, Guid?>(o => o.Guid, v => v.OrderId)
            .Build();

        var pipeline = MongoViewTranslator.TranslatePipeline(def);

        var unwind = pipeline[1]["$unwind"].AsBsonDocument;
        unwind.Contains("preserveNullAndEmptyArrays").Should().BeFalse();
    }

    [Fact]
    public void Aggregates_emit_group_stage_then_project()
    {
        var def = new ViewDefinitionBuilder<CustomerTotalView>()
            .From<Order>()
            .GroupBy<Order, Guid>(o => o.CustomerId)
            .Select<Order, Guid>(o => o.CustomerId, v => v.CustomerId)
            .Sum<Order, decimal>(o => o.Amount, v => v.Total)
            .Build();

        var pipeline = MongoViewTranslator.TranslatePipeline(def);

        pipeline.Should().Contain(s => s.Contains("$group"));
        var project = pipeline.Last()["$project"].AsBsonDocument;
        project.Contains("Total").Should().BeTrue();
    }
}
