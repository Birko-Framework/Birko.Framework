using System;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that supports batch tracking with optional expiry.
    /// </summary>
    public interface IBatchable
    {
        string BatchNumber { get; set; }
        DateTime? ExpiryDate { get; set; }
    }
}
