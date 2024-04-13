using Birko.Models;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Extensions
{
    public static class SourceValueExtensions
    {
        public static T GetValue<T>(this IEnumerable<SourceValue<T>> values, string source)
        {
            if (!string.IsNullOrEmpty(source) && (values?.Any(x => x.Source == source) ?? false))
            {
                return values.FirstOrDefault(x => x.Source == source).Value;
            }
            return default;
        }

        public static SourceValue<T>[] SetValue<T>(this SourceValue<T>[] values, string source, T value)
        {
            if (string.IsNullOrEmpty(source))
            {
                return values;
            }

            if (values != null && values.Any(x => x.Source == source))
            {
                values.FirstOrDefault(x => x.Source == source).Value = value;
                return values;
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
