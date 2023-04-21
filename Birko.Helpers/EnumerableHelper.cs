using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.Data.Helpers
{
    public static class EnumerableHelper
    {
        public static (IEnumerable<T> added, IEnumerable<T> removed, IEnumerable<T> same) Diff<T>(
            IEnumerable<T> source,
            IEnumerable<T> destination,
            Func<T, T, bool> equalFunction = null
        )
        {
            IList<T> added = null;
            IList<T> removed = null;
            Func<T, T, bool> equal = (x, y) => equalFunction?.Invoke(x, y) ?? x.Equals(y);
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

            return (added, removed, (removed?.Any() ?? false) ? source?.Where(x => !removed.Any(y => equal(x, y))) : source);
        }
    }
}
