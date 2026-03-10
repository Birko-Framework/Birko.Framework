using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace Birko.Helpers
{
    public static class StringHelper
    {
        static readonly Regex WordDelimiters = new Regex(@"[\s—–_\/]", RegexOptions.Compiled);

        // characters that are not valid
        static readonly Regex InvalidChars = new Regex(@"[^a-z0-9\-]", RegexOptions.Compiled);

        // multiple hyphens
        static readonly Regex MultipleHyphens = new Regex(@"-{2,}", RegexOptions.Compiled);

        public static string GenerateSlug(this string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            // convert to lower case
            value = value.ToLowerInvariant();

            // remove diacritics (accents)
            value = RemoveDiacritics(value);

            // ensure all word delimiters are hyphens
            value = WordDelimiters.Replace(value, "-");

            // strip out invalid characters
            value = InvalidChars.Replace(value, "");

            // replace multiple hyphens (-) with a single hyphen
            value = MultipleHyphens.Replace(value, "-");

            // trim hyphens (-) from ends
            return value.Trim('-');
        }

        public static string RemoveDiacritics(this string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

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

        public static string ToHexText(byte[] bytes, bool upperCase = false)
        {
            if ((bytes?.Length ?? 0) == 0)
            {
                return string.Empty;
            }

            // Use optimized .NET native API (available in .NET 5+)
            // This is significantly faster than manual StringBuilder loop
            return upperCase
                ? Convert.ToHexString(bytes)
                : Convert.ToHexStringLower(bytes);
        }
    }
}
