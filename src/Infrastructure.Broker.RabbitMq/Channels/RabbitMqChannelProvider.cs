using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System;

namespace Infrastructure.Broker.RabbitMq.Channels;

public class RabbitMqChannelProvider : IRabbitMqChannelProvider
{
    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqChannelProvider> _logger;

    public RabbitMqChannelProvider(
        IRabbitMqConnectionProvider connectionProvider,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqChannelProvider> logger)
    {
        _connectionProvider = connectionProvider ?? throw new ArgumentNullException(nameof(connectionProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IModel CreateChannel(Action<IModel> configureChannel = null)
    {
        if (!_connectionProvider.TryGetConnection(out var connection) || connection == null || !connection.IsOpen)
        {
            _logger.LogError("Cannot create channel: RabbitMQ connection is not available or not open. Attempting to ensure connection.");
            // Proactively try to establish connection if not available.
            // This might block or throw if connection cannot be made.
            connection = _connectionProvider.GetConnection();
            if (connection == null || !connection.IsOpen)
            {
                 throw new InvalidOperationException("Failed to create channel: RabbitMQ connection could not be established or is not open.");
            }
        }

        IModel channel = null;
        try
        {
            channel = connection.CreateModel();
            _logger.LogDebug("RabbitMQ channel {ChannelNumber} created.", channel.ChannelNumber);

            // Apply default prefetch if configured and no specific configuration action overrides it.
            // Note: QoS settings are typically more relevant for consumer channels.
            if (_options.DefaultPrefetchCount.HasValue)
            {
                // Check if configureChannel already sets QoS to avoid overriding.
                // This simple check might not be foolproof if configureChannel does complex things.
                // A more robust way would be to let configureChannel take precedence or have a more explicit QoS setting mechanism.
                bool qosConfiguredByAction = false;
                if (configureChannel != null)
                {
                    // Temporarily capture current state or use a marker
                    // This is a conceptual check. Real check might involve inspecting channel state if possible,
                    // or relying on convention that configureChannel handles QoS if needed.
                    // For now, assume configureChannel might set it.
                }

                if (!qosConfiguredByAction) // Simplified: apply if not assumed to be set by configureChannel
                {
                     channel.BasicQos(0, _options.DefaultPrefetchCount.Value, false); // `false` for per-consumer, `true` for per-channel
                     _logger.LogDebug("Applied default prefetch count {PrefetchCount} to channel {ChannelNumber}.",
                        _options.DefaultPrefetchCount.Value, channel.ChannelNumber);
                }
            }

            // Apply custom channel configuration if provided
            configureChannel?.Invoke(channel);

            channel.CallbackException += (sender, ea) =>
            {
                var ch = sender as IModel;
                _logger.LogError(ea.Exception, "Callback exception on channel {ChannelNumber}. Details: {Details}", ch?.ChannelNumber, ea.Detail);
            };

            channel.ModelShutdown += (sender, ea) =>
            {
                 var ch = sender as IModel;
                // Don't log error if shutdown is initiated by application (e.g. channel.Close() or channel.Abort())
                if (!IsApplicationInitiatedShutdown(ea.ReplyCode))
                {
                    _logger.LogWarning("Channel {ChannelNumber} was shut down. Initiator: {Initiator}, Code: {ReplyCode}, Reason: {ReplyText}",
                        ch?.ChannelNumber, ea.Initiator, ea.ReplyCode, ea.ReplyText);
                }
                else
                {
                     _logger.LogDebug("Channel {ChannelNumber} was shut down by application. Code: {ReplyCode}, Reason: {ReplyText}",
                        ch?.ChannelNumber, ea.ReplyCode, ea.ReplyText);
                }
            };


            return channel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create RabbitMQ channel.");
            channel?.Dispose(); // Ensure cleanup if channel was partially created
            throw;
        }
    }
     private bool IsApplicationInitiatedShutdown(ushort replyCode)
    {
        // 200 indicates a clean shutdown, often initiated by the application.
        // Add other codes if necessary that signify intentional closure.
        return replyCode == RabbitMQ.Client.Constants.ReplySuccess;
    }
}