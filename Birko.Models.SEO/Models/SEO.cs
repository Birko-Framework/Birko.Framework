using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models.SEO
{
    public class SEO : Data.Models.AbstractLogModel, Data.Models.ILoadable<Birko.Models.SEO.ViewModels.SEO>
    {
        public string Title { get; set; } = null!;
        public string Path { get; set; } = null!;
        public string Description { get; set; } = null!;

        public void LoadFrom(Birko.Models.SEO.ViewModels.SEO data)
        {
            // CR-L321: guard first (guard-clause convention) — base is null-safe, so this is an ordering fix.
            if (data == null) return;
            base.LoadFrom(data);

            Title = data.Title;
            Path = data.Path;
            Description = data.Description;
        }
    }
}
