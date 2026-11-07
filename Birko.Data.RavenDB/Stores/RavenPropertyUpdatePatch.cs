using System;
using System.Collections.Generic;
using System.Text;
using Birko.Data.Stores;
using Raven.Client.Documents.Operations;

namespace Birko.Data.RavenDB.Stores
{
    /// <summary>
    /// TASK-827 (the user's decision): a <see cref="PropertyUpdate{T}"/> as a server-side RavenDB patch, for the kinds a
    /// patch computes exactly. Patch JavaScript holds every number as a double (measured on RavenDB 6.0: a stored
    /// <c>98765432109876543.21m</c> reads <c>98765432109876544</c>, and even copying it stores <c>98765432109876540</c>), so
    /// only values a double carries exactly may pass through it: a <c>Set</c> of a string, bool, Guid, date, enum or an
    /// integer within 2^53, and an <c>Increment</c> of <c>short</c>, <c>int</c> or <c>long</c>, range-checked on the server.
    /// A <c>decimal</c>, <c>double</c> or <c>float</c>, any other type, and every computed set return <c>null</c>: the store
    /// then keeps its exact read-modify-save path.
    /// </summary>
    public static class RavenPropertyUpdatePatch
    {
        /// <summary>The marker a range check throws with, so the store can report it as <see cref="OverflowException"/>.</summary>
        public const string OverflowMarker = "BIRKO_INCREMENT_OVERFLOW";

        private const long MaxSafeInteger = 9007199254740991; // 2^53 - 1

        private static readonly HashSet<Type> SafeSetTypes = new()
        {
            typeof(string), typeof(bool), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
            typeof(char), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long),
        };

        /// <summary>The patch for <paramref name="updates"/>, or <c>null</c> when any assignment must stay on the exact C# path.</summary>
        public static PatchRequest? TryBuild<T>(PropertyUpdate<T> updates) where T : Data.Models.AbstractModel
        {
            var script = new StringBuilder();
            var values = new Dictionary<string, object?>();
            var index = 0;
            foreach (var assignment in updates.Assignments)
            {
                var property = assignment.PropertyInfo;
                if (property == null || assignment.MemberPath.Contains('.')) return null;
                var name = property.Name;
                var arg = "p" + index++;

                var ok = assignment.Match(
                    set =>
                    {
                        if (!IsSafeSet(property.PropertyType, set.Value)) return false;
                        values[arg] = set.Value;
                        script.Append($"this[{Quote(name)}] = args.{arg};\n");
                        return true;
                    },
                    increment =>
                    {
                        var (min, max) = Range(property.PropertyType);
                        if (min == null || !FitsSafe(increment.Delta)) return false;
                        values[arg] = increment.Delta;
                        script.Append($"{{ var v = (this[{Quote(name)}] == null ? 0 : this[{Quote(name)}]) + args.{arg};\n")
                              .Append($"  if (!Number.isSafeInteger(v) || v < {min} || v > {max}) throw '{OverflowMarker}: {name}';\n")
                              .Append($"  this[{Quote(name)}] = v; }}\n");
                        return true;
                    },
                    computed => false);
                if (!ok) return null;
            }

            return new PatchRequest { Script = script.ToString(), Values = values };
        }

        private static bool IsSafeSet(Type type, object? value)
        {
            var inner = Nullable.GetUnderlyingType(type) ?? type;
            if (!(inner.IsEnum || SafeSetTypes.Contains(inner))) return false;
            return value is not long l || (l >= -MaxSafeInteger && l <= MaxSafeInteger);
        }

        private static (string? Min, string? Max) Range(Type type)
        {
            if (type == typeof(short)) return (short.MinValue.ToString(), short.MaxValue.ToString());
            if (type == typeof(int)) return (int.MinValue.ToString(), int.MaxValue.ToString());
            if (type == typeof(long)) return ((-MaxSafeInteger).ToString(), MaxSafeInteger.ToString());
            return (null, null);
        }

        private static bool FitsSafe(object delta) => delta switch
        {
            short or int => true,
            long l => l >= -MaxSafeInteger && l <= MaxSafeInteger,
            _ => false,
        };

        private static string Quote(string name) => "'" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
    }
}
