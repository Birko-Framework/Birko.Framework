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

        /// <inheritdoc />
        public string GetTopic(Type eventType)
        {
            return _cache.GetOrAdd(eventType, type =>
            {
                var attr = type.GetCustomAttribute<TopicAttribute>();
                return attr != null ? attr.Topic : _fallback.GetTopic(type);
            });
        }
    }
}
