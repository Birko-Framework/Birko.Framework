using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Data.Helpers
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
            if ((hash1?.Length ?? 0) == (hash2?.Length ?? 0))
            {
                int i = 0;
                while ((i < hash1.Length) && (hash1[i] == hash2[i]))
                {
                    i += 1;
                }
                if (i == hash1.Length)
                {
                    return true;
                }
            }
            return false;
        }

    }
}
