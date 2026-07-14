using Birko.Data.SQL.Attributes;
using Birko.Data.Models;
using System;

namespace Birko.Data.SQL.MSSql.View.Tests;

[Table("SbCustomers")]
public class SbCustomerModel : AbstractLogModel
{
    public string Name { get; set; } = null!;
    public string Email { get; set; } = null!;
}

[Table("SbOrders")]
public class SbOrderModel : AbstractLogModel
{
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
}

/// <summary>
/// Inner-join aggregate view (mirrors the reference CustomerOrderView) used to assert the
/// SCHEMABINDING SELECT builder emits two-part [dbo].[Table] names + aggregate AS aliases + GROUP BY.
/// </summary>
[View(typeof(SbCustomerModel), typeof(SbOrderModel), nameof(SbCustomerModel.Guid), nameof(SbOrderModel.CustomerId), connect: ViewConnect.CheckExisting)]
public class SbCustomerOrderView
{
    [ViewField(typeof(SbCustomerModel), nameof(SbCustomerModel.Guid))]
    public Guid? CustomerId { get; set; }

    [ViewField(typeof(SbCustomerModel), nameof(SbCustomerModel.Name))]
    public string CustomerName { get; set; } = null!;

    [CountField(typeof(SbOrderModel), nameof(SbOrderModel.Guid))]
    public int OrderCount { get; set; }

    [SumField(typeof(SbOrderModel), nameof(SbOrderModel.Total))]
    public decimal TotalSpent { get; set; }
}

/// <summary>
/// Non-aggregate join view (only ViewFields, no Count/Sum) — HasAggregateFields() is false, so it is
/// accepted by the indexed-view guard (CR-L181).
/// </summary>
[View(typeof(SbCustomerModel), typeof(SbOrderModel), nameof(SbCustomerModel.Guid), nameof(SbOrderModel.CustomerId), connect: ViewConnect.CheckExisting)]
public class SbCustomerOrderPlainView
{
    [ViewField(typeof(SbCustomerModel), nameof(SbCustomerModel.Guid))]
    public Guid? CustomerId { get; set; }

    [ViewField(typeof(SbCustomerModel), nameof(SbCustomerModel.Name))]
    public string CustomerName { get; set; } = null!;

    [ViewField(typeof(SbOrderModel), nameof(SbOrderModel.Total))]
    public decimal Total { get; set; }
}
