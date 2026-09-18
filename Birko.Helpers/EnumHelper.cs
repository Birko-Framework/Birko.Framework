using System;

namespace Birko.Helpers
{
    /// <summary>
    /// Enum parsing that treats the enum member's <b>name</b> as the stable wire value, the way a
    /// JSON API / DTO boundary should. <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// on its own also accepts numeric ordinals ("1" → the member with value 1) and out-of-range
    /// integers ("999" → an undefined member), both of which break an "enums are stable names"
    /// contract.
    /// </summary>
    public static class EnumHelper
    {
        /// <summary>
        /// Parse <paramref name="value"/> as the (case-insensitive) <b>name</b> of a defined
        /// <typeparamref name="TEnum"/> member. Rejects null/whitespace, numeric or ordinal input
        /// ("1", "-3", "999"), and names that don't resolve to a defined member. Returns
        /// <c>false</c> instead of throwing; <paramref name="result"/> is <c>default</c> on failure.
        /// </summary>
        public static bool TryParseName<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
        {
            result = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string trimmed = value.Trim();
            // Reject numeric ordinals ("1", "-3", "999"): a real enum member name starts with a
            // letter or an underscore, never a digit or sign.
            if (!char.IsLetter(trimmed[0]) && trimmed[0] != '_')
            {
                return false;
            }
            return Enum.TryParse(trimmed, ignoreCase: true, out result) && Enum.IsDefined(result);
        }
    }
}
