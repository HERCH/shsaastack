using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Broker.RabbitMq.Configuration;

namespace Infrastructure.Broker.RabbitMq.Connections;

public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider
{
    private readonly ILogger<RabbitMqConnectionProvider> _logger;
    private readonly RabbitMqOptions _options;
    private readonly IConnectionFactory _connectionFactory;
    private IConnection _connection;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly AsyncRetryPolicy _connectionRetryPolicy;
    private bool _disposed;

    public RabbitMqConnectionProvider(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqConnectionProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new ArgumentException("RabbitMQ ConnectionString must be configured.", nameof(options));
        }

        _connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(_options.ConnectionString),
            AutomaticRecoveryEnabled = _options.AutomaticRecoveryEnabled,
            NetworkRecoveryInterval = _options.NetworkRecoveryInterval,
            RequestedHeartbeat = TimeSpan.FromSeconds(_options.RequestedHeartbeat),
            DispatchConsumersAsync = _options.DispatchConsumersAsync,
            ClientProvidedName = _options.ConnectionName ?? System.AppDomain.CurrentDomain.FriendlyName
        };

        _logger.LogInformation("RabbitMQ Connection Provider configured: Uri={Uri}, AutoRecovery={AutoRecovery}, Heartbeat={Heartbeat}s, ConnectionName='{ConnectionName}'",
            _options.ConnectionString, _options.AutomaticRecoveryEnabled, _options.RequestedHeartbeat, _connectionFactory.ClientProvidedName);

        _connectionRetryPolicy = Policy
            .Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .Or<IOException>()
            .WaitAndRetryAsync(
                retryCount: 5, // Configurable?
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), // Exponential backoff
                onRetry: (exception, timespan, attempt, context) =>
                {
                    _logger.LogWarning(exception, "RabbitMQ connection failed. Attempt {Attempt}. Retrying in {Timespan}...", attempt, timespan);
                });
    }

    public IConnection GetConnection()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        if (_connection != null && _connection.IsOpen)
        {
            return _connection;
        }

        // Using EnsureConnectionAsync().GetAwaiter().GetResult() can lead to deadlocks in some contexts.
        // It's generally safer to call the async method and wait for it if a synchronous version is truly needed,
        // but better to keep the call chain async if possible.
        // For simplicity in a sync GetConnection(), we'll use a simple lock and try to connect.
        // A truly robust sync GetConnection would need more care or be avoided.

        _connectionLock.Wait(); // Synchronous wait
        try
        {
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;
            }
            _logger.LogInformation("Attempting to establish RabbitMQ connection synchronously.");
            Connect(); // Internal connect method
            return _connection;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public bool TryGetConnection(out IConnection connection)
    {
        connection = null;
        if (_disposed)
        {
            return false;
        }

        if (_connection != null && _connection.IsOpen)
        {
            connection = _connection;
            return true;
        }
        // Do not attempt to connect here, just report status.
        return false;
    }

    public async Task EnsureConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }

        if (_connection != null && _connection.IsOpen)
        {
            return;
        }

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection != null && _connection.IsOpen)
            {
                return;
            }

            _logger.LogInformation("RabbitMQ connection not established or closed. Attempting to connect...");
            await _connectionRetryPolicy.ExecuteAsync(async () =>
            {
                Connect(); // Internal connect method
                await Task.CompletedTask; // Connect is sync, wrap for policy
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to connect to RabbitMQ after multiple retries.");
            throw; // Rethrow critical failure
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void Connect()
    {
        // This internal method is synchronous as _connectionFactory.CreateConnection() is synchronous.
        if (_connection != null && _connection.IsOpen)
        {
            _logger.LogDebug("RabbitMQ connection already open.");
            return;
        }

        _logger.LogInformation("Creating new RabbitMQ connection to {Uri} with client name '{ClientName}'.",
            _options.ConnectionString, _connectionFactory.ClientProvidedName);

        try
        {
            _connection = _connectionFactory.CreateConnection();
            _connection.ConnectionShutdown += OnConnectionShutdown;
            _connection.CallbackException += OnCallbackException;
            _connection.ConnectionBlocked += OnConnectionBlocked;
            _connection.ConnectionUnblocked += OnConnectionUnblocked;
            // For AutomaticRecoveryEnabled = true, these are also useful:
            // _connection.RecoverySucceeded += OnRecoverySucceeded;
            // _connection.ConnectionRecoveryError += OnConnectionRecoveryError;


            _logger.LogInformation("RabbitMQ connection established successfully. Endpoint: {Endpoint}, Client Port: {ClientPort}",
                _connection.Endpoint.ToString(), _connection.LocalPort);
        }
        catch (BrokerUnreachableException ex)
        {
            _logger.LogError(ex, "Failed to connect to RabbitMQ broker. Please check network and broker status.");
            throw; // Allow Polly to catch and retry
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while creating RabbitMQ connection.");
            throw; // Allow Polly to catch and retry if applicable
        }
    }

    private void OnConnectionBlocked(object sender, global::RabbitMQ.Client.Events.ConnectionBlockedEventArgs e)
    {
        _logger.LogWarning("RabbitMQ connection is blocked. Reason: {Reason}", e.Reason);
    }

    private void OnConnectionUnblocked(object sender, EventArgs e)
    {
        _logger.LogInformation("RabbitMQ connection is unblocked.");
    }

    private void OnCallbackException(object sender, global::RabbitMQ.Client.Events.CallbackExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "RabbitMQ callback exception. Detail: {Detail}", e.Detail);
    }

    private void OnConnectionShutdown(object sender, ShutdownEventArgs e)
    {
        if (_disposed) return; // If we are disposing, this is expected.

        _logger.LogWarning("RabbitMQ connection shut down. Initiator: {Initiator}, Reply Code: {ReplyCode}, Reason: {ReplyText}",
            e.Initiator, e.ReplyCode, e.ReplyText);

        // If not initiated by application (e.g. not during dispose), and auto-recovery is not handling it,
        // you might want to trigger reconnection logic here, though AutomaticRecoveryEnabled should handle most cases.
        // Forcing a reconnect attempt outside of GetConnection/EnsureConnectionAsync might be needed if
        // the client library's auto-recovery fails persistently or if a proactive re-check is desired.
        // However, with AutomaticRecoveryEnabled = true, the client library handles this.
    }

    // Optional: Handlers for recovery events if fine-grained logging/action is needed
    // private void OnRecoverySucceeded(object sender, EventArgs e)
    // {
    //     _logger.LogInformation("RabbitMQ connection recovery succeeded.");
    // }

    // private void OnConnectionRecoveryError(object sender, global::RabbitMQ.Client.Events.ConnectionRecoveryErrorEventArgs e)
    // {
    //     _logger.LogError(e.Exception, "RabbitMQ connection recovery failed.");
    // }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing RabbitMqConnectionProvider.");
        _connectionLock.Wait(); // Ensure no other thread is trying to connect
        try
        {
            if (_connection != null)
            {
                // Unsubscribe from events to prevent issues during dispose
                _connection.ConnectionShutdown -= OnConnectionShutdown;
                _connection.CallbackException -= OnCallbackException;
                _connection.ConnectionBlocked -= OnConnectionBlocked;
                _connection.ConnectionUnblocked -= OnConnectionUnblocked;
                // _connection.RecoverySucceeded -= OnRecoverySucceeded;
                // _connection.ConnectionRecoveryError -= OnConnectionRecoveryError;

                if (_connection.IsOpen)
                {
                    _logger.LogDebug("Closing RabbitMQ connection.");
                    _connection.Close(TimeSpan.FromSeconds(10)); // Close with a timeout
                }
                _connection.Dispose();
                _connection = null;
                _logger.LogInformation("RabbitMQ connection closed and disposed.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during RabbitMQ connection disposal.");
        }
        finally
        {
            _connectionLock.Release();
            _connectionLock.Dispose();
        }
    }
}