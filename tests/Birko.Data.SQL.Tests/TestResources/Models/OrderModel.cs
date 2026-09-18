using Birko.Data.SQL.Attributes;
using Birko.Data.Models;
using System;

namespace Birko.Data.SQL.Tests.TestResources.Models
{
    [Table("Orders")]
    public class OrderModel : AbstractLogModel
    {
        public Guid CustomerId { get; set; }
        public decimal Total { get; set; }
        public string Status { get; set; } = null!;
    }

    [Table("Customers")]
    public class CustomerModel : AbstractLogModel
    {
        public string Name { get; set; } = null!;
        public string Email { get; set; } = null!;
    }
}
