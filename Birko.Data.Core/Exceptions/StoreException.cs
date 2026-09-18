using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Exceptions
{
    public class StoreException : Exception
    {
        public StoreException(string message) : this(message, null!) { }
        public StoreException(string message, Exception? innerException) : base(message, innerException) { }
    }
}
