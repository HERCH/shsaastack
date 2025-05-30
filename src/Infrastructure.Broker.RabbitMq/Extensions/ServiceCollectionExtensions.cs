using System.Text.Json;
using Infrastructure.Broker.RabbitMq.Channels;
using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Connections;
using Infrastructure.Broker.RabbitMq.Publishing;
using Infrastructure.Broker.RabbitMq.Serialization;
using Infrastructure.Broker.RabbitMq.Topology;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Broker.RabbitMq.Extensions;

/// <summary>
/// Provides extension methods for setting up RabbitMQ infrastructure services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RabbitMQ infrastructure services to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configuration">The application's <see cref="IConfiguration"/>.</param>
    /// <param name="rabbitMqSectionName">The name of the configuration section containing RabbitMQ settings. Defaults to "RabbitMQ".</param>
    /// <param name="configureJsonOptions">Optional action to configure JsonSerializerOptions for message serialization.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddRabbitMqInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string rabbitMqSectionName = RabbitMqOptions.SectionName,
        Action<JsonSerializerOptions> configureJsonOptions = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configuration == null) throw new ArgumentNullException(nameof(configuration));
        if (string.IsNullOrWhiteSpace(rabbitMqSectionName))
            throw new ArgumentNullException(nameof(rabbitMqSectionName));

        // 1. Configure RabbitMqOptions
        // This makes IOptions<RabbitMqOptions> available.
        services.Configure<RabbitMqOptions>(configuration.GetSection(rabbitMqSectionName));

        // 2. Register Connection Provider (Singleton)
        // Manages a single, resilient connection to the broker.
        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();

        // 3. Register Channel Provider (Transient recommended)
        // Creates channels on demand. Channels are not thread-safe and should be short-lived.
        // The caller of CreateChannel() is responsible for disposing the channel.
        services.TryAddTransient<IRabbitMqChannelProvider, RabbitMqChannelProvider>();

        // 4. Register Message Serializer (Singleton)
        // Default to JsonMessageSerializer. Allow customization of JsonSerializerOptions.
        services.TryAddSingleton<IMessageSerializer>(serviceProvider =>
        {
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web); // Good defaults
            configureJsonOptions?.Invoke(jsonOptions); // Apply custom configurations
            return new JsonMessageSerializer(jsonOptions);
        });

        // 5. Register Message Publisher (Transient recommended)
        // Acquires a channel per publish operation (or batch).
        services.TryAddTransient<IMessagePublisher, RabbitMqMessagePublisher>();

        // 6. Register Topology Manager (Transient recommended)
        // Acquires a channel per topology operation.
        services.TryAddTransient<ITopologyManager, RabbitMqTopologyManager>();

        _ValidateRequiredConfiguration(services, rabbitMqSectionName);

        return services;
    }

    private static void _ValidateRequiredConfiguration(IServiceCollection services, string rabbitMqSectionName)
    {
        // Build a temporary service provider to validate options.
        // This is a good practice to fail fast if critical configuration is missing.
        var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>()?.Value;
        var logger = serviceProvider.GetService<ILogger<RabbitMqConnectionProvider>>(); // Use a relevant logger

        if (options == null)
        {
            var errorMessage = $"RabbitMQ options section '{rabbitMqSectionName}' not found or could not be bound.";
            logger?.LogCritical(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var errorMessage =
                $"RabbitMQ ConnectionString is missing in the '{rabbitMqSectionName}' configuration section.";
            logger?.LogCritical(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        logger?.LogInformation(
            "RabbitMQ Infrastructure services registered. ConnectionString: {ConnectionStringPrefix}...",
            options.ConnectionString.Substring(0, Math.Min(options.ConnectionString.Length,
                options.ConnectionString.IndexOf('@') > 0
                    ? options.ConnectionString.IndexOf('@')
                    : options.ConnectionString.Length))
        );
    }
}