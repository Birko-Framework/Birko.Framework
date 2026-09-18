namespace Birko.MessageQueue.Retry
{
    /// <summary>
    /// Configuration for dead letter queue handling.
    /// Messages that exceed retry limits are moved to a dead letter destination.
    /// </summary>
    public class DeadLetterOptions
    {
        /// <summary>
        /// Whether dead letter queue is enabled. Default is true.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Suffix appended to the original destination name to form the DLQ name.
        /// Default is ".dlq".
        /// </summary>
        public string Suffix { get; set; } = ".dlq";

        /// <summary>
        /// Explicit dead letter destination name. When set, overrides the suffix-based naming.
        /// </summary>
        public string? Destination { get; set; }

        /// <summary>
        /// Gets the dead letter destination for the given source destination.
        /// </summary>
        public string GetDeadLetterDestination(string sourceDestination)
        {
            return Destination ?? (sourceDestination + Suffix);
        }
    }
}
