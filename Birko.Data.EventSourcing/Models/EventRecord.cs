using System;
using System.Security.Cryptography;
using System.Text;
using Birko.Data.EventSourcing.Events;
using Birko.Data.Models;

namespace Birko.Data.EventSourcing.Models
{
    /// <summary>
    /// One stored event, for the generic event store over any <c>IAsyncBulkStore</c> (TASK-995). Its <see cref="AbstractModel.Guid"/>
    /// is <see cref="IdFor"/> of (aggregate, version), so two writers of one version create the SAME id and the backend refuses
    /// the second natively: a store's create never overwrites (measured on every database backend, TASK-995).
    /// </summary>
    /// <remarks>The event's own <see cref="IEvent.EventGuid"/> is a field of its own, as in the SQL store's <c>DomainEventModel</c>.</remarks>
    public class EventRecord : AbstractModel
    {
        // A fixed namespace for the name-based ids; changing it orphans every stored event.
        private static readonly Guid IdNamespace = new("3f1c2b9e-6a7d-4e58-9b0c-2d5e8f71a4c6");

        public Guid EventGuid { get; set; }

        public Guid AggregateGuid { get; set; }

        public long Version { get; set; }

        public string EventType { get; set; } = string.Empty;

        public DateTime OccurredAt { get; set; }

        public string EventData { get; set; } = string.Empty;

        public string? Metadata { get; set; }

        public Guid? UserGuid { get; set; }

        /// <summary>
        /// The record id of (<paramref name="aggregateGuid"/>, <paramref name="version"/>): a name-based UUID, version 5 (RFC 9562,
        /// SHA-1), under a fixed Birko namespace. The same pair always gives the same id, in every process.
        /// </summary>
        public static Guid IdFor(Guid aggregateGuid, long version)
            => NameBasedV5(IdNamespace, aggregateGuid.ToString("D") + ":" + version.ToString(System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>The RFC 9562 version 5 UUID of <paramref name="text"/> (UTF-8) in namespace <paramref name="namespaceId"/>.</summary>
        internal static Guid NameBasedV5(Guid namespaceId, string text)
        {
            var name = Encoding.UTF8.GetBytes(text);
            var ns = ToNetworkOrder(namespaceId.ToByteArray());
            var input = new byte[ns.Length + name.Length];
            Buffer.BlockCopy(ns, 0, input, 0, ns.Length);
            Buffer.BlockCopy(name, 0, input, ns.Length, name.Length);
#pragma warning disable CA5350 // RFC 9562 version 5 is defined over SHA-1; this is an identifier, not a security use.
            var hash = SHA1.HashData(input);
#pragma warning restore CA5350
            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
            bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant
            return new Guid(ToNetworkOrder(bytes));
        }

        // Guid.ToByteArray stores the first three fields little-endian; RFC 9562 hashes and lays out big-endian. The swap is
        // its own inverse, so it converts both ways.
        private static byte[] ToNetworkOrder(byte[] b)
        {
            var r = (byte[])b.Clone();
            Array.Reverse(r, 0, 4);
            Array.Reverse(r, 4, 2);
            Array.Reverse(r, 6, 2);
            return r;
        }

        public static EventRecord FromEvent(IEvent e) => new()
        {
            Guid = IdFor(e.AggregateGuid, e.Version),
            EventGuid = e.EventGuid,
            AggregateGuid = e.AggregateGuid,
            Version = e.Version,
            EventType = e.EventType,
            OccurredAt = e.OccurredAt,
            EventData = e.EventData,
            Metadata = e.Metadata,
            UserGuid = e.UserGuid,
        };

        public DomainEvent ToEvent() => new()
        {
            EventGuid = EventGuid,
            AggregateGuid = AggregateGuid,
            Version = Version,
            EventType = EventType,
            OccurredAt = OccurredAt,
            EventData = EventData,
            Metadata = Metadata,
            UserGuid = UserGuid,
        };
    }
}
