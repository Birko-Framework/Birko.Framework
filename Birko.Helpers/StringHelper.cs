using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Data.Helpers
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
            if (!string.IsNullOrEmpty(value))
            {
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
            return string.Empty;
        }

        public static string RemoveDiacritics(this string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
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
            return string.Empty;
        }

        public static string RemoveMultipleSpaces(this string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                // convert multiple spaces into one space
                str = Regex.Replace(str, @"\s+", " ").Trim();
                return str;
            }
            return string.Empty;
        }

        public static byte[] CalculateSHA1Hash(this string data)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);
            using var sha1 = new SHA1CryptoServiceProvider();
            return sha1.ComputeHash(dataBytes);
        }

        public static byte[] CalculateSHA256Hash(this string data)
        {
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        public static byte[] CalculateSHA512Hash(this string data)
        {
            using SHA512 sha512 = SHA512.Create();
            return sha512.ComputeHash(Encoding.UTF8.GetBytes(data));
        }

        public static string ToHexText(byte[] bytes, bool upperCase = false)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);

            for (int i = 0; i < bytes.Length; i++)
                result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));

            return result.ToString();
        }
    }
}
