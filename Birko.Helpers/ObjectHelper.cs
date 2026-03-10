using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Helpers
{
    public static class ObjectHelper
    {
        public static int Compare(IComparable value, IComparable value2)
        {
            if (value == null && value2 == null)
            {
                return 0;
            }
            else if (value != null && value2 == null)
            {
                return 1;
            }
            else if (value == null && value2 != null)
            {
                return -1;
            }
            else
            {
                return value.CompareTo(value2);
            }
        }

        public static bool CompareHash(byte[] hash1, byte[] hash2)
        {
            // Use SequenceEqual for efficient comparison - handles nulls and uses optimized native code
            return hash1 != null && hash2 != null && hash1.AsSpan().SequenceEqual(hash2);
        }

    }
}
