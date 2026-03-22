using System;

namespace Birko.Models.Customers.Filters
{
    public class ContactPerson
    {
        public Guid? CustomerGuid { get; set; }
        public string? Name { get; set; }
        public bool? IsPrimary { get; set; }
    }
}
