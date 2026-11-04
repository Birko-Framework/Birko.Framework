using Birko.Data.Models;
using Birko.Data.ViewModels;
using System;

namespace Birko.Data.MongoDB.Models
{
    /// <summary>
    /// A MongoDB model with audit timestamps, the MongoDB counterpart of <see cref="AbstractLogModel"/>.
    /// </summary>
    /// <remarks>
    /// TASK-723: it declared the timestamps but not <see cref="ILogEntity"/> (so not <see cref="ITimestamped"/>), and the
    /// timestamp decorator, which selects on that, never stamped one; copying or loading between two Mongo log models
    /// kept only the Guid, as the timestamp copy took an <see cref="AbstractLogModel"/> alone (both measured). It now
    /// copies and loads the timestamps to and from any <see cref="ILogEntity"/>, as <see cref="AbstractLogModel"/> does.
    /// </remarks>
    public abstract partial class MongoDBLogModel : MongoDBModel, ILogEntity, ICopyable<AbstractLogModel>, ILoadable<ViewModels.LogViewModel>, ILoadable<ILogEntity>
    {
        public virtual DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public virtual DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public virtual DateTime? PrevUpdatedAt { get; set; } = null;

        /// <summary>Copies the identity, and the audit timestamps when the target is a log entity.</summary>
        public override AbstractModel CopyTo(AbstractModel? clone = null)
        {
            base.CopyTo(clone);
            if (clone is ILogEntity target)
            {
                CopyLogStateTo(target);
            }
            return clone!;
        }

        /// <exception cref="System.ArgumentNullException">No target was supplied — the base throws (SH-M424); this used
        /// to return the null target as the "copy".</exception>
        public AbstractLogModel CopyTo(AbstractLogModel? clone = null)
        {
            // Non-virtual, as AbstractLogModel's typed copy: dispatching to the override would recurse.
            base.CopyTo(clone);
            CopyLogStateTo(clone!);
            return clone!;
        }

        private void CopyLogStateTo(ILogEntity target)
        {
            target.CreatedAt = CreatedAt;
            target.UpdatedAt = UpdatedAt;
            target.PrevUpdatedAt = PrevUpdatedAt;
        }

        /// <summary>Loads the identity, and the audit timestamps when the source carries them.</summary>
        public override void LoadFrom(IGuidEntity data)
        {
            base.LoadFrom(data);
            if (data is ILogEntity log)
            {
                LoadLogState(log);
            }
        }

        public void LoadFrom(ILogEntity data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                LoadLogState(data);
            }
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

        private void LoadLogState(ILogEntity data)
        {
            CreatedAt = data.CreatedAt;
            UpdatedAt = data.UpdatedAt;
            PrevUpdatedAt = data.PrevUpdatedAt;
        }
    }
}
