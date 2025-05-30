using RabbitMQ.Client;

namespace Infrastructure.Broker.RabbitMq.Extensions;

public static class BasicPropertiesExtensions
{
    public static IBasicProperties CloneTo(this IBasicProperties source, IModel channel)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (channel == null) throw new ArgumentNullException(nameof(channel));

        var target = channel.CreateBasicProperties();

        target.ContentType = source.ContentType;
        target.ContentEncoding = source.ContentEncoding;
        target.Headers = source.Headers;
        target.DeliveryMode = source.DeliveryMode;
        target.Priority = source.Priority;
        target.CorrelationId = source.CorrelationId;
        target.ReplyTo = source.ReplyTo;
        target.Expiration = source.Expiration;
        target.MessageId = source.MessageId;
        target.Timestamp = source.Timestamp;
        target.Type = source.Type;
        target.UserId = source.UserId;
        target.AppId = source.AppId;
        target.ClusterId = source.ClusterId;

        return target;
    }
}