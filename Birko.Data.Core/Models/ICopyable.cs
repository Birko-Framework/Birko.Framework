using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public interface ICopyable<T>
    {
        /// <summary>
        /// Copies this instance's state onto <paramref name="clone"/> and returns it.
        /// <para><b>Nullability contract (CR-L101):</b> implementations accept a null or omitted target
        /// (<c>AbstractModel</c>/<c>AbstractLogModel</c> declare <c>CopyTo(T? clone = null)</c>) and, when
        /// none is supplied, allocate a fresh instance — the return is always non-null. The interface
        /// parameter is kept non-nullable to avoid churning nullable annotations across the ~15 model
        /// implementers (several use a non-nullable <c>CopyTo(T clone)</c> overload); a caller may still
        /// pass a target or rely on the implementation's null-tolerant default.</para>
        /// </summary>
        T CopyTo(T clone);
    }
}
