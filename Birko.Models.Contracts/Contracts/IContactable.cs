namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that has contact information.
    /// </summary>
    public interface IContactable
    {
        string Phone { get; set; }
        string Email { get; set; }
    }
}
