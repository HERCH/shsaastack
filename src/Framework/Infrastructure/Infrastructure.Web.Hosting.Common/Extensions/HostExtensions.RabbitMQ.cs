using Common.Configuration;
using Infrastructure.Eventing.Interfaces.Notifications;
using Infrastructure.Hosting.Common.Extensions;
using Infrastructure.Hosting.Common.RabbitMQ;
using Infrastructure.Hosting.Common.RabbitMQ.ApplicationServices;
using Infrastructure.Persistence.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Web.Hosting.Common.Extensions;

/// <summary>
///     Provides extension methods for conditionally registering RabbitMQ services
/// </summary>
public static class RabbitMQHostExtensions
{
    private const string RabbitMQEnabledSettingName = "ApplicationServices:RabbitMQ:Enabled";

    /// <summary>
    ///     Registers RabbitMQ stores if enabled in configuration
    /// </summary>
    public static void RegisterRabbitMQStoreIfEnabled(this IServiceCollection services,
        IConfiguration configuration, bool isMultiTenanted)
    {
        var useRabbitMQ = configuration.GetValue<bool>(RabbitMQEnabledSettingName);
        if (!useRabbitMQ)
        {
            return;
        }

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
    }

    /// <summary>
    ///     Registers RabbitMQ event notification message broker if enabled in configuration
    /// </summary>
    public static void RegisterRabbitMQEventBrokerIfEnabled(this IServiceCollection services,
        IConfiguration configuration)
    {
        var useRabbitMQ = configuration.GetValue<bool>(RabbitMQEnabledSettingName);
        if (!useRabbitMQ)
        {
            return;
        }

        services.AddPerHttpRequest<IEventNotificationMessageBroker, RabbitMQEventNotificationMessageBroker>();
    }
}
