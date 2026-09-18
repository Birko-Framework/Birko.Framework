using System;
using System.Collections.Generic;

namespace Birko.Models.Contracts
{
    /// <summary>
    /// Entity that supports variants (sizes, colors, configurations).
    /// </summary>
    public interface IVariantable<TVariant>
    {
        ICollection<TVariant> Variants { get; set; }
    }
}
