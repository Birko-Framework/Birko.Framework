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
            // CR-L301: AbstractPercentage is abstract (can't self-instantiate like ValueData.CopyTo does),
            // so a null clone can't be satisfied — fail fast instead of returning `clone!` (a provably-null
            // value with a suppressed warning) and NRE-ing the caller downstream.
            if (clone == null)
            {
                throw new ArgumentNullException(nameof(clone));
            }

            base.CopyTo(clone);
            clone.Percentage = Percentage;
            return clone;
        }
    }
}
