namespace Birko.Models.Pricing.Filters
{
    public class PriceGroup
    {
        public string? Name { get; set; }
        public bool? IsDefault { get; set; }
        public decimal? PercentageFrom { get; set; }
        public decimal? PercentageTo { get; set; }
    }
}
