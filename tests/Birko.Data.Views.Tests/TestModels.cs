using Birko.Data.Models;

namespace Birko.Data.Views.Tests;

/// <summary>Test source entities</summary>
public class Customer : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class Order : AbstractModel
{
    public Guid CustomerId { get; set; }
    public decimal Total { get; set; }
    public int Quantity { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class Product : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Category { get; set; } = string.Empty;
}

/// <summary>Test view result types</summary>
public class CustomerView
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
}

public class CustomerOrderSummary
{
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal AvgOrder { get; set; }
    public decimal MinOrder { get; set; }
    public decimal MaxOrder { get; set; }
}

public class ProductCategorySummary
{
    public string Category { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public decimal AvgPrice { get; set; }
}

public class OrderStatusView
{
    public string Status { get; set; } = string.Empty;
    public long Count { get; set; }
}

/// <summary>Non-numeric view for testing aggregate validation</summary>
public class BadAggregateView
{
    public string Name { get; set; } = string.Empty;
    public string TotalAsString { get; set; } = string.Empty;
}
