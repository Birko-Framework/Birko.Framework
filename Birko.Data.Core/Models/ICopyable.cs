using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public interface ICopyable<T>
    {
        /// <summary>
        /// Copies this instance's state onto <paramref name="clone"/> and returns it.
        /// <para><b>Nullability contract (CR-L101, SH-M424):</b> the return is always the target, never <c>this</c>.
        /// <c>AbstractModel</c>/<c>AbstractLogModel</c> declare <c>CopyTo(T? clone = null)</c> but, being abstract,
        /// cannot allocate: a null or omitted target throws <see cref="System.ArgumentNullException"/>. A concrete model
        /// that overrides <c>CopyTo</c> may allocate its own type when none is supplied (most do). The interface
        /// parameter is kept non-nullable to avoid churning nullable annotations across the model implementers.</para>
        /// </summary>
        T CopyTo(T clone);
    }
}
