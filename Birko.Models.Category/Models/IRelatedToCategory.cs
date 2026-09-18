using System;
using Birko.Data.Models;

namespace Birko.Models.Category
{
    public interface IRelatedToCategory : ILoadable<ViewModels.Category>
    {
        Guid? CategoryGuid { get; set; }
    }
}
