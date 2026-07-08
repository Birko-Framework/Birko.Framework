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
        , Birko.Data.Patterns.Models.ISluggable
    {
        public string Title { get; set; } = null!;
        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = null!;
        public int Depth { get; set; }
        public string? Slug { get; set; }
        public string? GetSlugSource() => Title;
        public string Description { get; set; } = null!;

        public void LoadFrom(Birko.Models.Category.ViewModels.Category data)
        {
            base.LoadFrom(data);
            if (data == null) return;

            Title = data.Title;
            Slug = data.Slug;
            Path = data.Path;
            Description = data.Description;

            // The ViewModel carries only Path, so restore ParentGuid/Depth from it — otherwise a
            // Category edited through the ViewModel layer would lose its parent link and depth on
            // save, leaving the materialized Path inconsistent with ParentGuid/Depth.
            Birko.Models.Contracts.HierarchyHelper.DeriveParentAndDepthFromPath(this);
        }
    }
}
