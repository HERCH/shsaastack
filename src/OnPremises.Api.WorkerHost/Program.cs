using System.Text.Json;
using Application.Interfaces.Services;
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
                // Asegúrate de que appsettings.json se carga, y opcionalmente appsettings.{Environment}.json
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
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
                // Considera añadir Application Insights u otro proveedor de logging aquí si es necesario
            })
            .ConfigureServices((hostContext, services) =>
            {
                IConfiguration configuration = hostContext.Configuration;

                // --- 1. Registrar Configuración y Servicios Comunes (Adaptado de tu HostExtensions) ---
                services.AddHttpClient();
                services.AddSingleton<IConfigurationSettings>(new AspNetDynamicConfigurationSettings(configuration));
                services.AddSingleton<IHostSettings, HostSettings>();
                services.AddSingleton(JsonSerializerOptions.Default);
                
                // FeatureFlags, CrashReporter, Recorder (adapta según tus necesidades)
                services.AddSingleton<IFeatureFlags, EmptyFeatureFlags>();
                services.AddSingleton<ICrashReporter>(new NoOpCrashReporter());
                services.AddSingleton<IRecorder>(c =>
                    new CrashTraceOnlyRecorder("RabbitMQ.WorkerHost", c.GetRequiredService<ILoggerFactory>(),
                        c.GetRequiredService<ICrashReporter>()));

                // Fábrica de Clientes API (si tus RelayWorkers la usan)
                services.AddSingleton<IEnvironmentVariables, EnvironmentVariables>();
                services.AddSingleton<IServiceClientFactory>(c =>
                    new InterHostServiceClientFactory(c.GetRequiredService<IHttpClientFactory>(),
                        c.GetRequiredService<JsonSerializerOptions>(), c.GetRequiredService<IHostSettings>()));


                // --- 2. Registrar Infraestructura de RabbitMQ ---
                services.AddRabbitMqInfrastructure(configuration, RabbitMqOptions.SectionName, jsonOptions =>
                {
                    // Personaliza JsonSerializerOptions para el broker si es necesario
                    // jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                });

                // --- 3. Registrar Servicio de Circuit Breaker ---
                // Asume que tienes una implementación (InMemory o Distribuida)
                services.AddSingleton<IGenericCircuitBreakerStateService, InMemoryCircuitBreakerStateService>();


                // --- 4. Registrar tus Relay Workers Concretos ---
                // Estos son los que hacen el trabajo de relay a tus APIs internas.
                // Asegúrate de que estas clases estén definidas y sean accesibles.
                services.AddTransient<IQueueMonitoringApiRelayWorker<AuditMessage>, DeliverAuditRelayWorker>();
                // services.AddTransient<IQueueMonitoringApiRelayWorker<UsageMessage>, DeliverUsageRelayWorker>();
                // services.AddTransient<IQueueMonitoringApiRelayWorker<EmailMessage>, SendEmailRelayWorker>();
                // services.AddTransient<IQueueMonitoringApiRelayWorker<SmsMessage>, SendSmsRelayWorker>();
                // services.AddTransient<IQueueMonitoringApiRelayWorker<ProvisioningMessage>, DeliverProvisioningRelayWorker>();
                services.AddTransient<IMessageBusMonitoringApiRelayWorker<DomainEventingMessage>, DeliverDomainEventingRelayWorker>();
                // NOTA: El ciclo de vida (Transient, Scoped, Singleton) de tus RelayWorkers
                // dependerá de sus propias dependencias y si tienen estado. Transient es a menudo seguro.


                // --- 5. Registrar los RabbitMqMessageListener como HostedServices ---
                // Necesitarás uno por cada tipo de mensaje/cola que quieras procesar.
                var rabbitMqConfig = configuration.GetSection(RabbitMqOptions.SectionName);

                // Ejemplo para AuditMessage (escuchando una cola simple)
                string auditQueue = rabbitMqConfig.GetValue<string>("Queues:Audits"); // Nombre desde appsettings.json
                if (!string.IsNullOrWhiteSpace(auditQueue))
                {
                    services.AddHostedService(provider =>
                        new RabbitMqMessageListener<AuditMessage>(
                            provider.GetRequiredService<ILoggerFactory>(), // Para el logger del listener
                            provider.GetRequiredService<IRabbitMqChannelProvider>(),
                            provider.GetRequiredService<ITopologyManager>(),
                            provider.GetRequiredService<IQueueMonitoringApiRelayWorker<AuditMessage>>(), // El worker específico
                            provider.GetRequiredService<IConfigurationSettings>(), // Para config de reintentos, etc.
                            provider.GetRequiredService<IRecorder>(),
                            provider.GetRequiredService<JsonSerializerOptions>(), // O IMessageSerializer
                            provider.GetRequiredService<IGenericCircuitBreakerStateService>(),
                            new ListenerQueueConfiguration // Configuración específica del listener
                            {
                                QueueName = auditQueue,
                                ListenerId = $"AuditListener-{auditQueue}",
                                RetryPolicyName = "DefaultWorkQueuePolicy", // Aplicar la política definida
                                MainWorkExchangeName = "", // Asumiendo que 'company_audits_queue' está bindeada al default exchange
                                MainWorkRoutingKey = auditQueue, // Routing key es el nombre de la cola para el default exchange
                                DeclareQueueOptions = new QueueDeclarationOptions // Configuración para la cola principal
                                {
                                    Durable = true,
                                    // El DeadLettering aquí configurado será para el DLQ FINAL después de todos los reintentos
                                    DeadLettering = new DeadLetterOptions 
                                    { 
                                        DeclareDeadLetterQueue = true 
                                        // Nombres para DLX/DLQ final se tomarán de la política o convención si no se especifican aquí
                                    } 
                                }
                            }
                        ));
                }

                // Ejemplo para DomainEventingMessage (escuchando una "suscripción" de un topic exchange)
                // Asume que tienes un exchange "DomainEventsExchange" y varias colas binedadas a él.
                // Cada cola representa una "suscripción" de tu anterior Service Bus Topic.
                string domainEventsExchangeName = rabbitMqConfig.GetValue<string>("Exchanges:DomainEventsExchange:Name"); // "domain_events_topic"
                string domainEventsExchangeType = rabbitMqConfig.GetValue<string>("Exchanges:DomainEventsExchange:Type"); // "topic"

                // Listener para la "suscripción" de ApiHost1EndUsers
                string endUsersQueue = rabbitMqConfig.GetValue<string>("Queues:DomainEventsApiHost1EndUsers"); // "domain_events_apihost1_endusers_queue"
                string endUsersRoutingKey = rabbitMqConfig.GetValue<string>("Bindings:DomainEventsApiHost1EndUsers:RoutingKey"); // ej. "apihost1.endusers.*" o específico
                if (!string.IsNullOrWhiteSpace(domainEventsExchangeName) && !string.IsNullOrWhiteSpace(endUsersQueue) && endUsersRoutingKey != null)
                {
                    services.AddHostedService(provider =>
                        new RabbitMqMessageListener<DomainEventingMessage>(
                            provider.GetRequiredService<ILoggerFactory>(),
                            provider.GetRequiredService<IRabbitMqChannelProvider>(),
                            provider.GetRequiredService<ITopologyManager>(),
                            provider.GetRequiredService<IMessageBusMonitoringApiRelayWorker<DomainEventingMessage>>(),
                            provider.GetRequiredService<IConfigurationSettings>(),
                            provider.GetRequiredService<IRecorder>(),
                            provider.GetRequiredService<JsonSerializerOptions>(),
                            provider.GetRequiredService<IGenericCircuitBreakerStateService>(),
                            new ListenerQueueConfiguration
                            {
                                QueueName = endUsersQueue,
                                ListenerId = $"DomainEventRelay-{endUsersQueue}",
                                DeclareExchangeOptions = new ExchangeDeclarationOptions
                                {
                                    Name = domainEventsExchangeName,
                                    Type = domainEventsExchangeType, // "topic"
                                    Durable = true
                                },
                                BindToExchange = true,
                                ExchangeName = domainEventsExchangeName,
                                RoutingKey = endUsersRoutingKey,
                                // Parámetros para IMessageBusMonitoringApiRelayWorker
                                SubscriberHostName = "ApiHost1", // O de configuración
                                SubscriptionName = "apihost1-endusers" // O de configuración/convención
                            }
                        ));
                }

                // ... Registra otros listeners para Usages, Emails, SMS, Provisioning, y otras suscripciones de DomainEvents ...
            });
}