using System;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT topic utilities and validation.
    /// </summary>
    public static class MqttTopic
    {
        /// <summary>
        /// Single-level wildcard. Matches exactly one topic level.
        /// Example: "sensors/+/temperature" matches "sensors/living-room/temperature"
        /// </summary>
        public const char SingleLevelWildcard = '+';

        /// <summary>
        /// Multi-level wildcard. Must be the last character. Matches any number of levels.
        /// Example: "sensors/#" matches "sensors/living-room/temperature" and "sensors/kitchen"
        /// </summary>
        public const char MultiLevelWildcard = '#';

        /// <summary>
        /// Topic level separator.
        /// </summary>
        public const char Separator = '/';

        /// <summary>
        /// Validates a topic name for publishing (no wildcards allowed).
        /// </summary>
        public static bool IsValidPublishTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic))
            {
                return false;
            }

            return topic.IndexOf(SingleLevelWildcard) < 0
                && topic.IndexOf(MultiLevelWildcard) < 0;
        }

        /// <summary>
        /// Validates a topic filter for subscribing (wildcards allowed).
        /// </summary>
        public static bool IsValidSubscribeFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return false;
            }

            var levels = filter.Split(Separator);
            for (int i = 0; i < levels.Length; i++)
            {
                var level = levels[i];

                // Multi-level wildcard must be the last level and stand alone
                if (level.IndexOf(MultiLevelWildcard) >= 0)
                {
                    if (i != levels.Length - 1 || level.Length != 1)
                    {
                        return false;
                    }
                }

                // Single-level wildcard must stand alone in its level
                if (level.IndexOf(SingleLevelWildcard) >= 0 && level.Length != 1)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks whether a topic matches a subscription filter (including wildcards).
        /// </summary>
        public static bool Matches(string filter, string topic)
        {
            if (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(topic))
            {
                return false;
            }

            var filterLevels = filter.Split(Separator);
            var topicLevels = topic.Split(Separator);

            for (int i = 0; i < filterLevels.Length; i++)
            {
                var filterLevel = filterLevels[i];

                // Multi-level wildcard matches everything from here
                if (filterLevel.Length == 1 && filterLevel[0] == MultiLevelWildcard)
                {
                    return true;
                }

                // No more topic levels to match
                if (i >= topicLevels.Length)
                {
                    return false;
                }

                // Single-level wildcard matches this level
                if (filterLevel.Length == 1 && filterLevel[0] == SingleLevelWildcard)
                {
                    continue;
                }

                // Exact match required
                if (!string.Equals(filterLevel, topicLevels[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            // Filter and topic must have same number of levels (unless # was used)
            return filterLevels.Length == topicLevels.Length;
        }
    }
}
