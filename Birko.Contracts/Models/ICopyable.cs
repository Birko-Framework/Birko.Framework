using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public interface ICopyable<T>
    {
        T CopyTo(T clone);
    }
}
