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

        public static string GenerateSlug(string value)
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

        public static string RemoveDiacritics(string text)
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

        public static string ToUrlSlug(string phrase)
        {
            if (!string.IsNullOrEmpty(phrase))
            {
                string str = RemoveDiacritics(phrase).ToLower();
                //string str = phrase;
                // invalid chars
                str = Regex.Replace(str, @"[^a-z0-9\/\s-]", "");
                str = RemoveMultipleSpaces(str);
                // cut and trim
                str = str.Substring(0, str.Length <= 55 ? str.Length : 55).Trim();
                str = Regex.Replace(str, @"\s", "-"); // hyphens
                str = Regex.Replace(str, @"\/", "-"); // hyphens
                return str;
            }
            return string.Empty;
        }

        public static string RemoveMultipleSpaces(string str)
        {
            if (!string.IsNullOrEmpty(str))
            {
                // convert multiple spaces into one space
                str = Regex.Replace(str, @"\s+", " ").Trim();
                return str;
            }
            return string.Empty;
        }

        public static byte[] CalculateSHA1Hash(string data)
        {
            var dataBytes = Encoding.ASCII.GetBytes(data);
            using (var sha1 = new SHA1CryptoServiceProvider())
            {
                return sha1.ComputeHash(dataBytes);
            }
        }

        public static string HashText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                return Encoding.ASCII.GetString(CalculateSHA1Hash(text));
            }
            return string.Empty;
        }
    }
}
