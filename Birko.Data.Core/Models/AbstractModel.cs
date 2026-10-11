using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public abstract partial class AbstractModel : IGuidEntity, ICopyable<AbstractModel>, ILoadable<IGuidEntity>
    {
        public virtual Guid? Guid { get; set; } = null;

        /// <summary>
        /// Copies this model's state onto <paramref name="clone"/> and returns it.
        /// </summary>
        /// <exception cref="ArgumentNullException">No target was supplied. An abstract base cannot allocate the
        /// concrete type, and returning <c>this</c> — what it used to do — handed back the source as its own "copy"
        /// (SH-M424). A concrete model that can allocate overrides this and creates the target itself.</exception>
        public virtual AbstractModel CopyTo(AbstractModel? clone = null)
        {
            if (clone == null)
            {
                throw new ArgumentNullException(nameof(clone),
                    $"{GetType().Name}.CopyTo needs a target: the abstract base cannot allocate one. Pass a new instance, or override CopyTo to allocate it.");
            }
            clone.Guid = Guid;
            return clone;
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
