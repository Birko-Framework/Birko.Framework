using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Birko.EventBus.Routing
{
    /// <summary>
    /// Default topic convention: converts event type name to kebab-case.
    /// Example: <c>OrderPlaced</c> → <c>"events.order-placed"</c>
    /// If the event has a Source property, uses <c>"{source}.{event-name-kebab}"</c>.
    /// </summary>
    public class DefaultTopicConvention : ITopicConvention
    {
        private const string Prefix = "events";

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
            var result = Regex.Replace(input, "(?<!^)([A-Z])", "-$1");
            return result.ToLowerInvariant();
        }
    }
}
