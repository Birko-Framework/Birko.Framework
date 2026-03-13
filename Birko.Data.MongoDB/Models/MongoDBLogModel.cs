using Birko.Data.Models;
using Birko.Data.ViewModels;
using System;

namespace Birko.Data.MongoDB.Models
{
    public abstract partial class MongoDBLogModel : MongoDBModel, ICopyable<AbstractLogModel>, ILoadable<ViewModels.LogViewModel>
    {
        public virtual DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public virtual DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public virtual DateTime? PrevUpdatedAt { get; set; } = null;

        public AbstractLogModel CopyTo(AbstractLogModel clone = null!)
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

        public void LoadFrom(LogViewModel data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                CreatedAt = data.CreatedAt;
                UpdatedAt = data.UpdatedAt;
                PrevUpdatedAt = data.PrevUpdatedAt;
            }
        }
    }
}
