using System;

namespace Birko.EventBus.Routing
{
    /// <summary>
    /// Specifies a custom topic name for an event class.
    /// Used by <see cref="AttributeTopicConvention"/> to override the default naming.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TopicAttribute : Attribute
    {
        /// <summary>
        /// The topic name.
        /// </summary>
        public string Topic { get; }

        public TopicAttribute(string topic)
        {
            Topic = topic ?? throw new ArgumentNullException(nameof(topic));
        }
    }
}
