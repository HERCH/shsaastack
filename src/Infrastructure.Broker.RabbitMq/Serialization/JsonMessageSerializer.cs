using System;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Broker.RabbitMq.Serialization;

/// <summary>
/// Implements <see cref="IMessageSerializer"/> using System.Text.Json.
/// </summary>
public class JsonMessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    public string ContentType => "application/json";

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonMessageSerializer"/> class
    /// with optional <see cref="JsonSerializerOptions"/>.
    /// </summary>
    /// <param name="options">Optional JsonSerializerOptions. If null, default options are used.</param>
    public JsonMessageSerializer(JsonSerializerOptions options = null)
    {
        _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web); // JsonSerializerDefaults.Web is a good starting point
    }

    public byte[] Serialize<T>(T message)
    {
        if (message == null)
        {
            return Array.Empty<byte>();
        }
        // Using System.Text.Json to serialize
        return JsonSerializer.SerializeToUtf8Bytes(message, _options);
    }

    public T Deserialize<T>(byte[] body)
    {
        if (body == null || body.Length == 0)
        {
            return default;
        }
        // Using System.Text.Json to deserialize
        // Ensure the body is a valid UTF-8 string for JSON.
        // If issues arise with encoding, consider using Encoding.UTF8.GetString(body) first,
        // but SerializeToUtf8Bytes/Deserialize should handle UTF-8 directly.
        try
        {
            return JsonSerializer.Deserialize<T>(body, _options);
        }
        catch (JsonException ex)
        {
            // Log or handle deserialization errors appropriately
            // For robustness, you might want to include the raw string in the error log (if small enough)
            // string rawJson = Encoding.UTF8.GetString(body); // Be careful with large messages
            // _logger.LogError(ex, "Failed to deserialize JSON message. Raw (truncated): {RawJson}", rawJson.Substring(0, Math.Min(rawJson.Length, 500)));
            throw new MessageDeserializationException($"Failed to deserialize message of type {typeof(T).FullName} from JSON.", ex, body);
        }
    }
}

/// <summary>
/// Custom exception for message deserialization failures.
/// </summary>
public class MessageDeserializationException : Exception
{
    public byte[] RawMessageBody { get; }

    public MessageDeserializationException(string message, Exception innerException, byte[] rawMessageBody)
        : base(message, innerException)
    {
        RawMessageBody = rawMessageBody;
    }
}