using System;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that has a physical location (warehouse, building, shelf).
    /// </summary>
    public interface ILocatable
    {
        Guid? LocationGuid { get; set; }
    }
}
