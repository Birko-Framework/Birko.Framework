namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that carries price information including VAT.
    /// </summary>
    public interface IPriceable
    {
        decimal? Price { get; set; }
        decimal? PriceVAT { get; set; }
        decimal? VAT { get; set; }
    }
}
