using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Models
{
    public interface ILoadable<T>
    {
        void LoadFrom(T data);
    }
}
