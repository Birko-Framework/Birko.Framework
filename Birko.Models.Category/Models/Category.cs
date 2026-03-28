using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models.Category
{
    public class Category
        : Data.Models.AbstractLogModel
        , Data.Models.ILoadable<Birko.Models.Category.ViewModels.Category>
        , Birko.Models.Contracts.IHierarchical
    {
        public string Title { get; set; } = null!;
        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = null!;
        public int Depth { get; set; }
        public string Description { get; set; } = null!;

        public void LoadFrom(Birko.Models.Category.ViewModels.Category data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Title = data.Title;
            Path = data.Path;
            Description = data.Description;
        }
    }
}
