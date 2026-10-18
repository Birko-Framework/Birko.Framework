using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Birko.EventBus.Routing
{
    /// <summary>
    /// Topic convention that uses <see cref="TopicAttribute"/> on event classes.
    /// Falls back to <see cref="DefaultTopicConvention"/> if no attribute is present.
    /// </summary>
    public class AttributeTopicConvention : ITopicConvention
    {
        private readonly DefaultTopicConvention _fallback = new();
        private readonly ConcurrentDictionary<Type, string> _cache = new();
        private readonly ConcurrentDictionary<Type, bool> _hasAttribute = new();

        /// <inheritdoc />
        public string GetTopic(Type eventType)
        {
            return _cache.GetOrAdd(eventType, type =>
            {
                var attr = type.GetCustomAttribute<TopicAttribute>();
                return attr != null ? attr.Topic : _fallback.GetTopic(type);
            });
        }

        /// <summary>
        /// CR-L251: an explicit <see cref="TopicAttribute"/> wins (it is deliberate and source-independent);
        /// otherwise defer to the fallback's event-based mapping so a source-bearing, attribute-less event
        /// routes the same way it would under <see cref="DefaultTopicConvention"/> (source-prefixed).
        /// Without this override the interface default (<c>GetTopic(@event.GetType())</c>) would ignore
        /// <see cref="IEvent.Source"/> for attribute-less events, disagreeing with DefaultTopicConvention.
        /// </summary>
        public string GetTopic(IEvent @event)
        {
            // SH-L129: whether the type carries [Topic] is looked up once per type; it was reflected on every call,
            // and the attribute-less path never reached the cache. The result is unchanged.
            var type = @event.GetType();
            var attributed = _hasAttribute.GetOrAdd(type, static t => t.GetCustomAttribute<TopicAttribute>() != null);
            return attributed ? GetTopic(type) : _fallback.GetTopic(@event);
        }
    }
}
