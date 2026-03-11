using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models.Category
{
    public class Category : Data.Models.AbstractLogModel, Data.Models.ILoadable<Birko.Models.Category.ViewModels.Category>
    {
        public string Title { get; set; }
        public string Path { get; set; }
        public string Description { get; set; }

        public void LoadFrom(Birko.Models.Category.ViewModels.Category data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Title = data.Title;
                Path = data.Path;
                Description = data.Description;
            }
        }
    }
}
