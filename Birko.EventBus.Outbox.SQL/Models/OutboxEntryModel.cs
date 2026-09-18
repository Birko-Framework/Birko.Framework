using System;
using System.Collections.Generic;
using System.Text.Json;
using Birko.Data.Models;

namespace Birko.EventBus.Outbox.SQL.Models
{
    /// <summary>
    /// SQL-persisted model for an outbox entry. Maps to the "__Outbox" table via Birko.Data.SQL attributes.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Birko.BackgroundJobs.SQL.Models.JobDescriptorModel</c> deliberately — the two solve the
    /// same problem (a core contract needs a row) and a second convention would only make both harder to
    /// read.
    /// </remarks>
    [Birko.Data.SQL.Attributes.Table("__Outbox")]
    public class OutboxEntryModel : AbstractModel, ILoadable<OutboxEntry>
    {
        [Birko.Data.SQL.Attributes.PrimaryField]
        [Birko.Data.SQL.Attributes.NamedField("Id")]
        public override Guid? Guid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("EventId")]
        public Guid EventId { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("EventType")]
        public string EventType { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("Payload")]
        public string Payload { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("Source")]
        public string Source { get; set; } = string.Empty;

        [Birko.Data.SQL.Attributes.NamedField("CorrelationId")]
        public Guid? CorrelationId { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("TenantGuid")]
        public Guid? TenantGuid { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("Headers")]
        public string? HeadersJson { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("Status")]
        public int Status { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Birko.Data.SQL.Attributes.NamedField("PublishedAt")]
        public DateTime? PublishedAt { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("ClaimedAt")]
        public DateTime? ClaimedAt { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("Attempts")]
        public int Attempts { get; set; }

        [Birko.Data.SQL.Attributes.NamedField("LastError")]
        public string? LastError { get; set; }

        /// <summary>
        /// Written during an atomic claim so concurrent processors can tell which one won the row — the
        /// same mechanism <c>JobDescriptorModel.ClaimToken</c> uses, and for the same reason: without it
        /// two processors read the same pending batch and publish every event twice.
        /// </summary>
        [Birko.Data.SQL.Attributes.NamedField("ClaimToken")]
        public Guid? ClaimToken { get; set; }

        public OutboxEntry ToEntry()
        {
            var entry = new OutboxEntry
            {
                Id = Guid ?? System.Guid.NewGuid(),
                EventId = EventId,
                EventType = EventType,
                Payload = Payload,
                Source = Source,
                CorrelationId = CorrelationId,
                TenantGuid = TenantGuid,
                Status = (OutboxStatus)Status,
                CreatedAt = CreatedAt,
                PublishedAt = PublishedAt,
                ClaimedAt = ClaimedAt,
                Attempts = Attempts,
                LastError = LastError,
            };

            if (!string.IsNullOrEmpty(HeadersJson))
            {
                try
                {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(HeadersJson);
                    if (headers != null) entry.Headers = headers;
                }
                catch (JsonException)
                {
                    // Unreadable headers must not make the event itself undeliverable: they are metadata,
                    // the payload is the message. An entry dropped here would be silently lost, which is
                    // the exact failure the outbox exists to prevent.
                }
            }

            return entry;
        }

        public static OutboxEntryModel FromEntry(OutboxEntry entry)
        {
            var model = new OutboxEntryModel();
            model.LoadFrom(entry);
            return model;
        }

        public void LoadFrom(OutboxEntry data)
        {
            Guid = data.Id;
            EventId = data.EventId;
            EventType = data.EventType;
            Payload = data.Payload;
            Source = data.Source;
            CorrelationId = data.CorrelationId;
            TenantGuid = data.TenantGuid;
            HeadersJson = data.Headers is { Count: > 0 } ? JsonSerializer.Serialize(data.Headers) : null;
            Status = (int)data.Status;
            CreatedAt = data.CreatedAt;
            PublishedAt = data.PublishedAt;
            ClaimedAt = data.ClaimedAt;
            Attempts = data.Attempts;
            LastError = data.LastError;
        }
    }
}
