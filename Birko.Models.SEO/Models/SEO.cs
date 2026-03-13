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
            base.LoadFrom(data);
            if (data == null) return;

            Title = data.Title;
            Path = data.Path;
            Description = data.Description;
        }
    }
}
