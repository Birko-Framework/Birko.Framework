using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.EventBus.Routing
{
    /// <summary>
    /// Default topic convention: converts event type name to kebab-case.
    /// Example: <c>OrderPlaced</c> → <c>"events.order-placed"</c>
    /// The instance overload, for an event with a Source, returns <c>"{source}.{event-name-kebab}"</c>.
    /// </summary>
    /// <remarks>
    /// SH-L128: the bus routes by <see cref="GetTopic(Type)"/> only — a subscription knows only the event type, so a
    /// publish must land on the type's topic to reach it. The source-prefixed instance overload therefore never decides
    /// where a distributed event goes; it is a naming helper for callers that use it directly.
    /// </remarks>
    public class DefaultTopicConvention : ITopicConvention
    {
        private const string Prefix = "events";

        // CR-L253: compile the kebab-case pattern once instead of re-parsing it (via the static Regex
        // cache) on every call — GetTopic can run per-publish. Inserts a hyphen before each uppercase
        // letter that isn't at the start.
        private static readonly Regex KebabBoundary =
            new("(?<!^)([A-Z])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <inheritdoc />
        public string GetTopic(Type eventType)
        {
            var name = ToKebabCase(eventType.Name);
            return $"{Prefix}.{name}";
        }

        /// <inheritdoc />
        public string GetTopic(IEvent @event)
        {
            var name = ToKebabCase(@event.GetType().Name);
            if (!string.IsNullOrEmpty(@event.Source))
            {
                return $"{@event.Source.ToLowerInvariant()}.{name}";
            }

            return $"{Prefix}.{name}";
        }

        private static string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // Insert hyphen before each uppercase letter (except the first)
            var result = KebabBoundary.Replace(input, "-$1");
            return result.ToLowerInvariant();
        }
    }
}
