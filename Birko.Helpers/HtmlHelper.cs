using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Data.Helpers
{
    //gitea.dev.zdrojak.eu:3000/
    public static class HtmlHelper
    {
        /// <summary>
        /// Compiled regular expression for performance.
        /// </summary>
        static Regex _htmlRegex = new Regex("<.*?>", RegexOptions.Compiled);
        static Regex _htmlARegex = new Regex(@"<\/*a.*?>", RegexOptions.Compiled);
        static Regex _htmlImgRegex = new Regex(@"<img[^>]*>", RegexOptions.Compiled);


        /// <summary>
        /// Remove HTML from string with compiled Regex.
        /// </summary>
        public static string StripTagsRegexCompiled(string source)
        {
            return (!string.IsNullOrEmpty(source)) ? _htmlRegex.Replace(source, string.Empty) : string.Empty;
        }

        public static string StripATagsRegexCompiled(string source)
        {
            return (!string.IsNullOrEmpty(source)) ? _htmlARegex.Replace(source, string.Empty) : string.Empty;
        }

        public static string StripImgTagsRegexCompiled(string source)
        {
            return (!string.IsNullOrEmpty(source)) ? _htmlImgRegex.Replace(source, string.Empty) : string.Empty;
        }
    }
}
