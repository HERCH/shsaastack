using RabbitMQ.Client;
using System;
using System.Collections.Generic;

namespace Infrastructure.Broker.RabbitMq.Publishing;

/// <summary>
/// Represents the properties for a message to be published.
/// This class helps in abstracting RabbitMQ's IBasicProperties.
/// </summary>
public class MessageProperties
{
    /// <summary>
    /// MIME Content type of the message body.
    /// If null, the serializer's content type will be used.
    /// </summary>
    public string ContentType { get; set; }

    /// <summary>
    /// MIME Content encoding of the message body.
    /// </summary>
    public string ContentEncoding { get; set; }

    /// <summary>
    /// Collection of custom message headers.
    /// </summary>
    public IDictionary<string, object> Headers { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Message persistence. True if the message should be persisted to disk.
    /// Default is true.
    /// </summary>
    public bool Persistent { get; set; } = true;

    /// <summary>
    /// Message priority, from 0 to 9.
    /// Only relevant for priority queues.
    /// </summary>
    public byte? Priority { get; set; }

    /// <summary>
    /// Application-specific correlation identifier.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Address to reply to (e.g., queue name) for RPC scenarios.
    /// </summary>
    public string ReplyTo { get; set; }

    /// <summary>
    /// Message expiration specification (per-message TTL).
    /// String representation, e.g., "60000" for 60 seconds.
    /// </summary>
    public string Expiration { get; set; }

    /// <summary>
    /// Application-specific message identifier.
    /// </summary>
    public string MessageId { get; set; }

    /// <summary>
    /// Timestamp of the message.
    /// If null, it might not be set, or the broker might set it.
    /// </summary>
    public AmqpTimestamp? Timestamp { get; set; }

    /// <summary>
    /// Message type name.
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// User ID of the publishing user.
    /// Usually set by the broker if authenticated.
    /// </summary>
    public string UserId { get; set; }

    /// <summary>
    /// Application ID that generated the message.
    /// </summary>
    public string AppId { get; set; }

    /// <summary>
    /// Populates RabbitMQ's <see cref="IBasicProperties"/> from this instance.
    /// </summary>
    /// <param name="channelProperties">The IBasicProperties instance to populate.</param>
    /// <param name="defaultContentType">The default content type to use if not set in these properties.</param>
    internal void Populate(IBasicProperties channelProperties, string defaultContentType)
    {
        channelProperties.ContentType = this.ContentType ?? defaultContentType;

        if (!string.IsNullOrWhiteSpace(this.ContentEncoding))
            channelProperties.ContentEncoding = this.ContentEncoding;

        if (this.Headers != null && this.Headers.Count > 0)
            channelProperties.Headers = this.Headers;
        else
            channelProperties.Headers = new Dictionary<string, object>(); // Ensure Headers is not null

        channelProperties.DeliveryMode = this.Persistent ? (byte)2 : (byte)1;

        if (this.Priority.HasValue)
            channelProperties.Priority = this.Priority.Value;

        if (!string.IsNullOrWhiteSpace(this.CorrelationId))
            channelProperties.CorrelationId = this.CorrelationId;

        if (!string.IsNullOrWhiteSpace(this.ReplyTo))
            channelProperties.ReplyTo = this.ReplyTo;

        if (!string.IsNullOrWhiteSpace(this.Expiration))
            channelProperties.Expiration = this.Expiration;

        if (!string.IsNullOrWhiteSpace(this.MessageId))
            channelProperties.MessageId = this.MessageId;
        else // Ensure a message ID is always present for better traceability
            channelProperties.MessageId = Guid.NewGuid().ToString("N");


        if (this.Timestamp.HasValue)
            channelProperties.Timestamp = this.Timestamp.Value;

        if (!string.IsNullOrWhiteSpace(this.Type))
            channelProperties.Type = this.Type;

        if (!string.IsNullOrWhiteSpace(this.UserId))
            channelProperties.UserId = this.UserId;

        if (!string.IsNullOrWhiteSpace(this.AppId))
            channelProperties.AppId = this.AppId;
    }
}