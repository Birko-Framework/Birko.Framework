using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Tests.TestResources.Models;
using System;

namespace Birko.Data.SQL.Tests.TestResources.Views
{
    [View(typeof(CustomerModel), typeof(OrderModel), nameof(CustomerModel.Guid), nameof(OrderModel.CustomerId), connect: ViewConnect.CheckExisting)]
    public class CustomerOrderView
    {
        [ViewField(typeof(CustomerModel), nameof(CustomerModel.Guid))]
        public Guid? CustomerId { get; set; }

        [ViewField(typeof(CustomerModel), nameof(CustomerModel.Name))]
        public string CustomerName { get; set; } = null!;

        [CountField(typeof(OrderModel), nameof(OrderModel.Guid))]
        public int OrderCount { get; set; }

        [SumField(typeof(OrderModel), nameof(OrderModel.Total))]
        public decimal TotalSpent { get; set; }
    }

    [View(typeof(CustomerModel), typeof(OrderModel), nameof(CustomerModel.Guid), nameof(OrderModel.CustomerId), connect: ViewConnect.Check)]
    public class CustomerOrderLeftJoinView
    {
        [ViewField(typeof(CustomerModel), nameof(CustomerModel.Guid))]
        public Guid? CustomerId { get; set; }

        [ViewField(typeof(CustomerModel), nameof(CustomerModel.Name))]
        public string CustomerName { get; set; } = null!;

        [CountField(typeof(OrderModel), nameof(OrderModel.Guid))]
        public int OrderCount { get; set; }
    }
}
