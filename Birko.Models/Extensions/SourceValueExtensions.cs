using Birko.Models;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Extensions
{
    public static class SourceValueExtensions
    {
        public static T? GetValue<T>(this IEnumerable<SourceValue<T>> values, string source)
        {
            // CR-L300: single pass — the old Any()+FirstOrDefault() enumerated the sequence twice (and
            // re-ran the predicate), which is wasteful for a lazy source and can even see a different
            // element between the two passes.
            if (string.IsNullOrEmpty(source) || values == null)
            {
                return default;
            }
            var match = values.FirstOrDefault(x => x.Source == source);
            return match != null ? match.Value : default;
        }

        public static SourceValue<T>[] SetValue<T>(this SourceValue<T>[] values, string source, T value)
        {
            if (string.IsNullOrEmpty(source))
            {
                return values;
            }

            // CR-L300: single scan instead of Any()+FirstOrDefault().
            if (values != null)
            {
                var existing = values.FirstOrDefault(x => x.Source == source);
                if (existing != null)
                {
                    existing.Value = value;
                    return values;
                }
            }

            var add = new[] {
                new SourceValue<T>() {
                    Source = source,
                    Value = value,
                }
            };
            if (values == null)
            {
                return add;
            }
            values = values.Concat(add).ToArray();
            return values;
        }
    }
}
