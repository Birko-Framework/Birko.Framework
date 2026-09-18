using System;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that belongs to a category.
    /// </summary>
    public interface ICategorizeable
    {
        Guid? CategoryGuid { get; set; }
    }
}
