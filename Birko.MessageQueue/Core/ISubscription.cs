using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.MessageQueue
{
    /// <summary>
    /// Represents an active subscription to a destination.
    /// Dispose to unsubscribe.
    /// </summary>
    public interface ISubscription : IDisposable
    {
        /// <summary>
        /// The destination this subscription is listening to.
        /// </summary>
        string Destination { get; }

        /// <summary>
        /// Whether this subscription is still active.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Unsubscribes from the destination.
        /// </summary>
        Task UnsubscribeAsync(CancellationToken cancellationToken = default);
    }
}
