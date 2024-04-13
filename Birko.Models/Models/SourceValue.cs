using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models
{
    public class SourceValue<T> : Data.Models.ILoadable<ViewModels.SourceValue<T>>
    {
        public string Source { get; set; }
        public T Value { get; set; }

        public void LoadFrom(ViewModels.SourceValue<T> data)
        {
            if (data != null)
            {
                Source = data.Source;
                Value = data.Value;
            }
        }
    }
}
