using System;

namespace Birko.Models.Customers.Filters
{
    public class CustomerBankAccount
    {
        public Guid? CustomerGuid { get; set; }
        public string? CurrencyCode { get; set; }
        public bool? IsDefault { get; set; }
    }
}
