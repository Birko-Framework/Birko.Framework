using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public abstract partial class AbstractLogModel : AbstractModel, ICopyable<AbstractLogModel>, ILoadable<ILogEntity>, ITimestamped
    {
        public virtual DateTime CreatedAt { get; set; }
        public virtual DateTime UpdatedAt { get; set; }
        public virtual DateTime? PrevUpdatedAt { get; set; }

        public AbstractLogModel CopyTo(AbstractLogModel? clone = null)
        {
            base.CopyTo(clone);
            if (clone != null)
            {
                clone.CreatedAt = CreatedAt;
                clone.UpdatedAt = UpdatedAt;
                clone.PrevUpdatedAt = PrevUpdatedAt;
            }
            return clone!;
        }

        public void LoadFrom(ILogEntity data)
        {
            base.LoadFrom(data);
            if(data != null)
            {
                CreatedAt = data.CreatedAt;
                UpdatedAt = data.UpdatedAt;
                PrevUpdatedAt = data.PrevUpdatedAt;
            }
        }
    }
}
