using System.Text.Json;
using Application.Interfaces.Services;
using Application.Persistence.Interfaces; 
using Application.Persistence.Shared.ReadModels;
using Common; 
using Common.Configuration;
using Common.FeatureFlags;
using Common.Recording;
using Infrastructure.Broker.RabbitMq.Channels;
using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Extensions;
using Infrastructure.Broker.RabbitMq.Topology;
using Infrastructure.Common.Recording;
using Infrastructure.Hosting.Common;
using Infrastructure.Interfaces; 
using Infrastructure.Web.Api.Common.Clients; 
using Infrastructure.Web.Api.Interfaces.Clients; 
using Infrastructure.Workers.Api; 
using Infrastructure.Workers.Api.Workers; 
using OnPremises.Api.WorkerHost; 



public class Program
{
    public static async Task Main(string[] args)
    {
        await CreateHostBuilder(args).Build().RunAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json",
                    optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
                if (args != null)
                {
                    config.AddCommandLine(args);
                }
            })
            .ConfigureLogging((hostingContext, logging) =>
            {
                logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                logging.AddConsole();
                logging.AddDebug();
                
                
            })
            .ConfigureServices((hostContext, services) =>
            {
                IConfiguration configuration = hostContext.Configuration;
                var logger =
                    services.BuildServiceProvider()
                        .GetRequiredService<ILogger<Program>>(); 

                
                services.AddHttpClient();
                
                services.AddSingleton<IConfigurationSettings>(provider =>
                    new AspNetDynamicConfigurationSettings(
                        provider.GetRequiredService<IConfiguration>(),
                        provider.GetService<ITenancyContext>() 
                    ));
                
                services.AddSingleton<IHostSettings>(provider =>
                    new HostSettings(provider.GetRequiredService<IConfigurationSettings>()));
                services.AddSingleton(JsonSerializerOptions.Default); 
                services.AddSingleton<IFeatureFlags, EmptyFeatureFlags>();
                services.AddSingleton<ICrashReporter, NoOpCrashReporter>(); 
                services.AddSingleton<IRecorder>(c =>
                    new CrashTraceOnlyRecorder("OnPremises.Api.WorkerHost", c.GetRequiredService<ILoggerFactory>(),
                        c.GetRequiredService<ICrashReporter>()));
                services.AddSingleton<IEnvironmentVariables, EnvironmentVariables>();
                services.AddSingleton<IServiceClientFactory>(c =>
                    new InterHostServiceClientFactory(
                        c.GetRequiredService<IHttpClientFactory>(),
                        c.GetRequiredService<JsonSerializerOptions>(),
                        c.GetRequiredService<IHostSettings>()
                    ));

                
                services.AddRabbitMqInfrastructure(configuration, RabbitMqOptions.SectionName);

                
                services.AddSingleton<IGenericCircuitBreakerStateService, InMemoryCircuitBreakerStateService>();

                
                services.AddTransient<IQueueMonitoringApiRelayWorker<AuditMessage>, DeliverAuditRelayWorker>();
                services.AddTransient<IQueueMonitoringApiRelayWorker<UsageMessage>, DeliverUsageRelayWorker>();
                services.AddTransient<IQueueMonitoringApiRelayWorker<EmailMessage>, SendEmailRelayWorker>();
                services.AddTransient<IQueueMonitoringApiRelayWorker<SmsMessage>, SendSmsRelayWorker>();
                services
                    .AddTransient<IQueueMonitoringApiRelayWorker<ProvisioningMessage>,
                        DeliverProvisioningRelayWorker>();
                services
                    .AddTransient<IMessageBusMonitoringApiRelayWorker<DomainEventingMessage>,
                        DeliverDomainEventingRelayWorker>();

                
                var rabbitMqConfigSection = configuration.GetSection(RabbitMqOptions.SectionName);

                
                RegisterQueueListener<AuditMessage>(services, rabbitMqConfigSection, "Queues:Audits",
                    "DefaultWorkQueuePolicy", logger);
                RegisterQueueListener<UsageMessage>(services, rabbitMqConfigSection, "Queues:Usages",
                    "DefaultWorkQueuePolicy", logger);
                RegisterQueueListener<EmailMessage>(services, rabbitMqConfigSection, "Queues:Emails",
                    "DefaultWorkQueuePolicy", logger);
                RegisterQueueListener<SmsMessage>(services, rabbitMqConfigSection, "Queues:Smses",
                    "DefaultWorkQueuePolicy", logger); 
                RegisterQueueListener<ProvisioningMessage>(services, rabbitMqConfigSection, "Queues:Provisioning",
                    "DefaultWorkQueuePolicy", logger);

                
                var domainEventBindingKeys = new[]
                {
                    "DomainEvents_ApiHost1_EndUsers",
                    "DomainEvents_ApiHost1_Organizations",
                    "DomainEvents_ApiHost1_Subscriptions",
                    "DomainEvents_ApiHost1_UserProfiles"
                };

                string domainEventsExchangeConfigKey = "Exchanges:DomainEventsTopic";
                string domainEventsExchangeName =
                    rabbitMqConfigSection.GetValue<string>($"{domainEventsExchangeConfigKey}:Name");
                string domainEventsExchangeType =
                    rabbitMqConfigSection.GetValue<string>($"{domainEventsExchangeConfigKey}:Type");

                if (string.IsNullOrWhiteSpace(domainEventsExchangeName))
                {
                    logger.LogCritical(
                        "Nombre del Exchange de Eventos de Dominio no configurado en '{ConfigKey}:Name'. No se iniciarán listeners de eventos de dominio.",
                        domainEventsExchangeConfigKey);
                }
                else
                {
                    foreach (var bindingKeyPrefix in domainEventBindingKeys)
                    {
                        string fullBindingConfigKey = $"Bindings:{bindingKeyPrefix}";
                        string queueConfigPath =
                            rabbitMqConfigSection.GetValue<string>($"{fullBindingConfigKey}:QueueConfigKey");
                        string listenerQueueName = rabbitMqConfigSection.GetValue<string>(queueConfigPath);
                        string routingKey =
                            rabbitMqConfigSection.GetValue<string>($"{fullBindingConfigKey}:RoutingKey");
                        string subscriptionName =
                            rabbitMqConfigSection.GetValue<string>($"{fullBindingConfigKey}:SubscriptionName");

                        if (string.IsNullOrWhiteSpace(listenerQueueName) || string.IsNullOrWhiteSpace(routingKey)
                                                                         || string.IsNullOrWhiteSpace(subscriptionName))
                        {
                            logger.LogWarning(
                                "Configuración incompleta para el binding '{BindingKeyPrefix}' (queue: '{QueuePath}', rk: '{RoutingKey}', subName: '{SubName}'). Listener no iniciado.",
                                bindingKeyPrefix, queueConfigPath, routingKey, subscriptionName);
                            continue;
                        }

                        services.AddHostedService(provider =>
                            new RabbitMqMessageListener<DomainEventingMessage>(
                                provider.GetRequiredService<ILoggerFactory>(),
                                provider.GetRequiredService<IRabbitMqChannelProvider>(),
                                provider.GetRequiredService<ITopologyManager>(),
                                provider
                                    .GetRequiredService<IMessageBusMonitoringApiRelayWorker<DomainEventingMessage>>(),
                                provider.GetRequiredService<IConfigurationSettings>(),
                                provider.GetRequiredService<IRecorder>(),
                                provider.GetRequiredService<JsonSerializerOptions>(),
                                provider.GetRequiredService<IGenericCircuitBreakerStateService>(),
                                new ListenerQueueConfiguration
                                {
                                    QueueName = listenerQueueName,
                                    ListenerId = $"DomainEventRelay-{subscriptionName}",
                                    DeclareQueueOptions = new QueueDeclarationOptions
                                    {
                                        Durable = true,
                                        DeadLettering = new DeadLetterOptions { DeclareDeadLetterQueue = true }
                                    },
                                    DeclareExchangeOptions = new ExchangeDeclarationOptions
                                    {
                                        Name = domainEventsExchangeName,
                                        Type = domainEventsExchangeType ?? "topic",
                                        Durable = true
                                    },
                                    BindToExchange = true,
                                    ExchangeName = domainEventsExchangeName,
                                    RoutingKey = routingKey,
                                    RetryPolicyName = "DomainEventsPolicy", 
                                    MainWorkExchangeName = domainEventsExchangeName, 
                                    MainWorkRoutingKey = routingKey, 
                                    SubscriberHostName = "ApiHost1", 
                                    SubscriptionName = subscriptionName
                                }
                            ));
                    }
                }
            });

    
    private static void RegisterQueueListener<TMessage>(
        IServiceCollection services,
        IConfigurationSection rabbitMqConfigSection,
        string queueConfigPath, 
        string retryPolicyName,
        ILogger logger)
        where TMessage : class, IQueuedMessage 
    {
        string queueName = rabbitMqConfigSection.GetValue<string>(queueConfigPath);
        if (string.IsNullOrWhiteSpace(queueName))
        {
            logger.LogWarning(
                "Nombre de cola no encontrado en configuración en '{ConfigPath}'. Listener para {MessageType} no iniciado.",
                queueConfigPath, typeof(TMessage).Name);
            return;
        }

        services.AddHostedService(provider =>
            new RabbitMqMessageListener<TMessage>(
                provider.GetRequiredService<ILoggerFactory>(),
                provider.GetRequiredService<IRabbitMqChannelProvider>(),
                provider.GetRequiredService<ITopologyManager>(),
                provider.GetRequiredService<IQueueMonitoringApiRelayWorker<TMessage>>(),
                provider.GetRequiredService<IConfigurationSettings>(),
                provider.GetRequiredService<IRecorder>(),
                provider.GetRequiredService<JsonSerializerOptions>(),
                provider.GetRequiredService<IGenericCircuitBreakerStateService>(),
                new ListenerQueueConfiguration
                {
                    QueueName = queueName,
                    ListenerId = $"{typeof(TMessage).Name.Replace("Message", "")}Listener-{queueName}",
                    RetryPolicyName = retryPolicyName,
                    MainWorkExchangeName = "", 
                    MainWorkRoutingKey = queueName,
                    DeclareQueueOptions = new QueueDeclarationOptions
                        { Durable = true, DeadLettering = new DeadLetterOptions { DeclareDeadLetterQueue = true } }
                }
            ));
    }
}