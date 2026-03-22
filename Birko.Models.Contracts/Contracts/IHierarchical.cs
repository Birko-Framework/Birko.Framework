using System;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that participates in a parent-child hierarchy.
    /// </summary>
    public interface IHierarchical
    {
        Guid? ParentGuid { get; set; }
        string Path { get; set; }
    }
}
