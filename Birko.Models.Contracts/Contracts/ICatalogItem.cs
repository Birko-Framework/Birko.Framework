namespace Birko.Models.Contracts
{
    /// <summary>
    /// Common properties for catalog items (products, warehouse items).
    /// </summary>
    public interface ICatalogItem
    {
        string Name { get; set; }
        string Code { get; set; }
        string BarCode { get; set; }
        string Description { get; set; }
    }
}
