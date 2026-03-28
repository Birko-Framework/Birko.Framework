namespace Birko.Models.Pricing.Filters
{
    public class Tax
    {
        public string? Name { get; set; }
        public string? ShortCut { get; set; }
        public bool? IsDefault { get; set; }
        public decimal? PercentageFrom { get; set; }
        public decimal? PercentageTo { get; set; }
    }
}
