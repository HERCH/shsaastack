using Common.Configuration;
using Infrastructure.Eventing.Interfaces.Notifications;
using Infrastructure.Hosting.Common.Extensions;
using Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;
using Infrastructure.Persistence.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Hosting.Common.RabbitMQ;

/// <summary>
///     Provides extension methods for registering RabbitMQ services
/// </summary>
public static class RabbitMQHostExtensions
{
    /// <summary>
    ///     Registers RabbitMQ as the message store (IQueueStore and IMessageBusStore)
    /// </summary>
    public static IServiceCollection AddRabbitMQStore(this IServiceCollection services, bool isMultiTenanted)
    {
        services.AddForPlatform<IQueueStore, IMessageBusStore, RabbitMQStore>(c =>
            RabbitMQStore.Create(RabbitMQSettings.LoadFromSettings(
                c.GetRequiredServiceForPlatform<IConfigurationSettings>())));

        if (isMultiTenanted)
        {
            services.AddPerHttpRequest<IQueueStore, IMessageBusStore, RabbitMQStore>(c =>
                RabbitMQStore.Create(RabbitMQSettings.LoadFromSettings(
                    c.GetRequiredService<IConfigurationSettings>())));
        }
        else
        {
            services.AddSingleton<IQueueStore, IMessageBusStore, RabbitMQStore>(c =>
                RabbitMQStore.Create(RabbitMQSettings.LoadFromSettings(
                    c.GetRequiredServiceForPlatform<IConfigurationSettings>())));
        }

        return services;
    }

    /// <summary>
    ///     Registers RabbitMQ as the integration event notification message broker
    /// </summary>
    public static IServiceCollection AddRabbitMQEventNotificationMessageBroker(this IServiceCollection services)
    {
        services.AddPerHttpRequest<IEventNotificationMessageBroker, RabbitMQEventNotificationMessageBroker>();

        return services;
    }
}
