using System;

namespace Birko.Models.Customers.Filters
{
    public class Customer
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public Guid? PriceGroupGuid { get; set; }
    }
}
