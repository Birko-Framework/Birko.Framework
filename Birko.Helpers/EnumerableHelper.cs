using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Helpers
{
    public static class EnumerableHelper
    {
        [Obsolete("Use DiffByKey<T> instead for O(n) key-based comparison with DiffResult<T> return type.")]
        public static (IEnumerable<T>? added, IEnumerable<T>? removed, IEnumerable<T>? same) Diff<T>(
            IEnumerable<T>? source,
            IEnumerable<T>? destination,
            Func<T, T, bool>? equalFunction = null
        )
        {
            IList<T>? added = null;
            IList<T>? removed = null;
            Func<T, T, bool> equal = (x, y) => equalFunction?.Invoke(x, y) ?? x!.Equals(y);
            if (destination?.Any() ?? false)
            {
                foreach (var item in destination)
                {
                    if (!(source?.Any(x => equal(x, item)) ?? false))
                    {
                        added ??= new List<T>();
                        added.Add(item);
                    }
                }
            }

            if (source?.Any() ?? false)
            {
                foreach (var item in source)
                {
                    if (!(destination?.Any(x => equal(x, item)) ?? false))
                    {
                        removed ??= new List<T>();
                        removed.Add(item);
                    }
                }
            }

            return (added, removed, (removed?.Any() ?? false) ? source?.Where(x => !removed!.Any(y => equal(x, y))) : source);
        }

        /// <summary>
        /// Diffs two collections using a key selector for O(n) HashSet-based comparison.
        /// Items whose key selector returns null are excluded from the diff.
        /// </summary>
        /// <typeparam name="T">Item type.</typeparam>
        /// <param name="current">The current/existing collection.</param>
        /// <param name="desired">The desired/target collection.</param>
        /// <param name="keySelector">Extracts a comparable key from each item. Items where this returns null are excluded.</param>
        /// <param name="filter">Optional filter — items not matching are excluded from the diff entirely.</param>
        /// <returns>A <see cref="DiffResult{T}"/> with Added, Removed, and Unchanged lists.</returns>
        public static DiffResult<T> DiffByKey<T>(
            IEnumerable<T> current,
            IEnumerable<T> desired,
            Func<T, object?> keySelector,
            Func<T, bool>? filter = null)
        {
            bool HasKey(T e) => keySelector(e) != null;

            var currentList = filter != null
                ? current.Where(filter).Where(HasKey).ToList()
                : current.Where(HasKey).ToList();

            var desiredList = filter != null
                ? desired.Where(filter).Where(HasKey).ToList()
                : desired.Where(HasKey).ToList();

            var currentKeys = new HashSet<object>(currentList.Select(e => keySelector(e)!));
            var desiredKeys = new HashSet<object>(desiredList.Select(e => keySelector(e)!));

            var added = desiredList.Where(e => !currentKeys.Contains(keySelector(e)!)).ToList();
            var removed = currentList.Where(e => !desiredKeys.Contains(keySelector(e)!)).ToList();
            var unchanged = currentList.Where(e => desiredKeys.Contains(keySelector(e)!)).ToList();

            return new DiffResult<T>(added, removed, unchanged);
        }
    }
}
