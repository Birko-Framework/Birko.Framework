using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public abstract partial class AbstractLogModel : AbstractModel, ILogEntity, ICopyable<AbstractLogModel>, ILoadable<ILogEntity>
    {
        public virtual DateTime CreatedAt { get; set; }
        public virtual DateTime UpdatedAt { get; set; }
        public virtual DateTime? PrevUpdatedAt { get; set; }

        /// <summary>
        /// Copies the identity and the audit timestamps when the target is a log model. An <b>override</b>, so a
        /// model held as <see cref="AbstractModel"/> or <c>ICopyable&lt;AbstractModel&gt;</c> keeps its timestamps;
        /// the typed overload below used to be the only copy, and the base-typed call skipped it (SH-M422).
        /// </summary>
        public override AbstractModel CopyTo(AbstractModel? clone = null)
        {
            base.CopyTo(clone);
            if (clone is AbstractLogModel target)
            {
                CopyLogStateTo(target);
            }
            return clone!;
        }

        /// <exception cref="ArgumentNullException">No target was supplied (SH-M424) — see <see cref="AbstractModel.CopyTo"/>.</exception>
        public AbstractLogModel CopyTo(AbstractLogModel? clone = null)
        {
            // Non-virtual on purpose: dispatching to the override from here would recurse through a derived override
            // that calls base.CopyTo with its own type, which binds to this overload.
            base.CopyTo(clone);
            CopyLogStateTo(clone!);
            return clone!;
        }

        private void CopyLogStateTo(AbstractLogModel target)
        {
            target.CreatedAt = CreatedAt;
            target.UpdatedAt = UpdatedAt;
            target.PrevUpdatedAt = PrevUpdatedAt;
        }

        /// <summary>
        /// Loads the identity, and the audit timestamps when the source carries them. An <b>override</b>, so
        /// <c>ILoadable&lt;IGuidEntity&gt;</c> dispatch no longer discards timestamps the argument has (SH-M423).
        /// </summary>
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
            // Non-virtual for the same reason as the typed CopyTo.
            base.LoadFrom(data);
            if (data != null)
            {
                LoadLogState(data);
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
