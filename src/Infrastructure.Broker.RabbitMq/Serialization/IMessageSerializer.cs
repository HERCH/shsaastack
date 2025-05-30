namespace Infrastructure.Broker.RabbitMq.Serialization;

/// <summary>
/// Defines an interface for serializing and deserializing messages.
/// </summary>
public interface IMessageSerializer
{
    /// <summary>
    /// Serializes the given message object into a byte array.
    /// </summary>
    /// <typeparam name="T">The type of the message.</typeparam>
    /// <param name="message">The message object to serialize.</param>
    /// <returns>A byte array representing the serialized message.</returns>
    byte[] Serialize<T>(T message);

    /// <summary>
    /// Deserializes the given byte array into a message object of the specified type.
    /// </summary>
    /// <typeparam name="T">The type of the message.</typeparam>
    /// <param name="body">The byte array to deserialize.</param>
    /// <returns>The deserialized message object.</returns>
    T Deserialize<T>(byte[] body);

    /// <summary>
    /// Gets the content type string that this serializer corresponds to (e.g., "application/json").
    /// </summary>
    string ContentType { get; }
}