namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that has a physical address.
    /// </summary>
    public interface IAddressable
    {
        string Street { get; set; }
        string City { get; set; }
        string ZIP { get; set; }
        string Country { get; set; }
    }
}
