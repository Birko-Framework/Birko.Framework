using System;

namespace Birko.Models.Product.Filters
{
    public class ProductPartnerCode
    {
        public Guid? ProductGuid { get; set; }
        public string? PartnerName { get; set; }
        public string? Code { get; set; }
    }
}
