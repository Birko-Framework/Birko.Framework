using System;
using System.IO;
using System.Linq;

namespace Birko.Helpers
{
    /// <summary>
    /// Finds the files a one-file-per-record (or per-batch) store owns in a folder it may share with other stores
    /// (TASK-811). A store's files are named from a <b>template</b> holding one <c>*</c> where the record's key goes —
    /// <c>items-*</c> or <c>items-*.xml</c> — and a file belongs to it only when the text in place of the <c>*</c> is
    /// exactly one key. A bare <c>items-*</c> glob also matched <c>items-archive-{guid}</c>, so a store named
    /// <c>items</c> read, rewrote and destroyed the records of one named <c>items-archive</c> (measured on the JSON and
    /// XML stores).
    /// </summary>
    public static class RecordFiles
    {
        /// <summary>A key that is one Guid in its <c>D</c> form, as the stores write it.</summary>
        public static bool IsGuid(string key) => Guid.TryParseExact(key, "D", out _);

        /// <summary>A key that is a batch number: digits only.</summary>
        public static bool IsIndex(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

        /// <summary>
        /// The files in <paramref name="directory"/> whose name is <paramref name="template"/> with one key in place of
        /// its <c>*</c>; an empty array when the directory does not exist.
        /// </summary>
        public static string[] List(string directory, string template, Func<string, bool> isKey)
        {
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }
            return Directory.GetFiles(directory, template).Where(path => Belongs(System.IO.Path.GetFileName(path), template, isKey)).ToArray();
        }

        /// <summary>Whether <paramref name="fileName"/> is <paramref name="template"/> with one key in place of its <c>*</c>.</summary>
        public static bool Belongs(string fileName, string template, Func<string, bool> isKey)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentNullException.ThrowIfNull(isKey);
            var star = template.IndexOf('*');
            if (star < 0)
            {
                return string.Equals(fileName, template, StringComparison.OrdinalIgnoreCase);
            }
            var prefix = template.Substring(0, star);
            var suffix = template.Substring(star + 1);
            if (fileName.Length < prefix.Length + suffix.Length
                || !fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return isKey(fileName.Substring(prefix.Length, fileName.Length - prefix.Length - suffix.Length));
        }
    }
}
