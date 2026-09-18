using Birko.Data.Models;
using System;
using System.Collections.Generic;

namespace Birko.Data.Aggregates.Tests.TestResources;

public class Product : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public IEnumerable<Category> Categories { get; set; } = [];
    public IEnumerable<Tag> Tags { get; set; } = [];
    public Image? DefaultImage { get; set; }
}

public class Category : AbstractModel
{
    public string Name { get; set; } = string.Empty;
}

public class Tag : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public Guid? ProductGuid { get; set; }
}

public class Image : AbstractModel
{
    public string Url { get; set; } = string.Empty;
    public Guid? ProductGuid { get; set; }
}

public class ProductCategory : AbstractModel
{
    public Guid? ProductGuid { get; set; }
    public Guid? CategoryGuid { get; set; }
}

public class Order : AbstractModel
{
    public string OrderNumber { get; set; } = string.Empty;
    public IEnumerable<OrderLine> Lines { get; set; } = [];
    public Customer? Customer { get; set; }
}

public class OrderLine : AbstractModel
{
    public Guid? OrderGuid { get; set; }
    public int Quantity { get; set; }
}

public class Customer : AbstractModel
{
    public string Email { get; set; } = string.Empty;
    public Guid? OrderGuid { get; set; }
}
