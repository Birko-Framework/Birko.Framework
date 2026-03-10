using System;
using Birko.Data.Models;

namespace Birko.Models
{
    public abstract class AbstractPercentage
        : AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.AbstractPercentage>
        , ICopyable<AbstractPercentage>
    {
        public virtual decimal Percentage { get; set; } = 0;

        public virtual void LoadFrom(ViewModels.AbstractPercentage data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Percentage = data.Percentage;
            }
        }

        public virtual AbstractPercentage CopyTo(AbstractPercentage clone)
        {
            if (clone != null)
            {
                base.CopyTo(clone);
                clone.Percentage = Percentage;
            }

            return clone;
        }
    }
}
