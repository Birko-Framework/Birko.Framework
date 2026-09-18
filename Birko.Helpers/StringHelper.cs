using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Helpers
{
    public static class StringHelper
    {
        public static string RemoveMultipleSpaces(this string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return string.Empty;
            }
            // convert multiple spaces into one space
            str = Regex.Replace(str, @"\s+", " ").Trim();
            return str;
        }

        public static byte[] CalculateSHA256Hash(this string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return Array.Empty<byte>();
            }
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        public static byte[] CalculateSHA512Hash(this string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                return Array.Empty<byte>();
            }
            using SHA512 sha512 = SHA512.Create();
            return sha512.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        public static string ToHexText(byte[]? bytes, bool upperCase = false)
        {
            if ((bytes?.Length ?? 0) == 0)
            {
                return string.Empty;
            }

            // Use optimized .NET native API (available in .NET 5+)
            // This is significantly faster than manual StringBuilder loop
            return upperCase
                ? Convert.ToHexString(bytes!)
                : Convert.ToHexStringLower(bytes!);
        }
    }
}
