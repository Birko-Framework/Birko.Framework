using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public abstract partial class AbstractModel : IGuidEntity, ICopyable<AbstractModel>, ILoadable<IGuidEntity>
    {
        public virtual Guid? Guid { get; set; } = null;

        public virtual AbstractModel CopyTo(AbstractModel? clone = null)
        {
            if (clone != null)
            {
                clone.Guid = Guid;
            }
            return clone!;
        }

        public virtual void LoadFrom(IGuidEntity data)
        {
            if (data != null)
            {
                Guid = data.Guid;
            }
        }
    }
}
