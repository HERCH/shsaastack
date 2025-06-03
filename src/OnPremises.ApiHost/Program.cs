using System.Text.Json;
using Application.Interfaces;
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
using OnPremises.ApiHost;
using OnPremises.ApiHost.Listeners;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.OnPremises.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();
if (args != null)
{
    builder.Configuration.AddCommandLine(args);
}

builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "hh:mm:ss ";
    options.SingleLine = true;
    options.IncludeScopes = false;
});
// builder.Logging.AddDebug();

var services = builder.Services;
var configuration = builder.Configuration;
var hostLogger = services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();

// Add services to the container.
services.AddHttpClient();

services.AddSingleton<IConfigurationSettings>(provider =>
    new AspNetDynamicConfigurationSettings(provider.GetRequiredService<IConfiguration>(),
        provider.GetService<ITenancyContext>()));

services.AddSingleton<IHostSettings>(provider =>
    new HostSettings(provider.GetRequiredService<IConfigurationSettings>()));
services.AddSingleton(JsonSerializerOptions.Default);
services.AddSingleton<IFeatureFlags, EmptyFeatureFlags>();
services.AddSingleton<ICrashReporter, NoOpCrashReporter>();
services.AddSingleton<IRecorder>(c => new CrashTraceOnlyRecorder("OnPremises.Api.WorkerHost",
    c.GetRequiredService<ILoggerFactory>(), c.GetRequiredService<ICrashReporter>()));
services.AddSingleton<IEnvironmentVariables, EnvironmentVariables>();
services.AddSingleton<IServiceClientFactory>(c =>
    new InterHostServiceClientFactory(c.GetRequiredService<IHttpClientFactory>(),
        c.GetRequiredService<JsonSerializerOptions>(), c.GetRequiredService<IHostSettings>()));

services.AddRabbitMqInfrastructure(configuration, RabbitMqOptions.SectionName);

services.AddSingleton<IGenericCircuitBreakerStateService, InMemoryCircuitBreakerStateService>();

services.AddTransient<IQueueMonitoringApiRelayWorker<AuditMessage>, DeliverAuditRelayWorker>();
services.AddTransient<IQueueMonitoringApiRelayWorker<EmailMessage>, SendEmailRelayWorker>();
services.AddTransient<IQueueMonitoringApiRelayWorker<ProvisioningMessage>, DeliverProvisioningRelayWorker>();
services.AddTransient<IQueueMonitoringApiRelayWorker<SmsMessage>, SendSmsRelayWorker>();
services.AddTransient<IQueueMonitoringApiRelayWorker<UsageMessage>, DeliverUsageRelayWorker>();

services.AddTransient<IMessageBusMonitoringApiRelayWorker<DomainEventingMessage>, DeliverDomainEventingRelayWorker>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var logger = services.BuildServiceProvider().GetRequiredService<IRecorder>();
var rabbitMqConfigSection = configuration.GetSection(RabbitMqOptions.SectionName);
RegisterAllQueueListeners(builder.Services, rabbitMqConfigSection, logger);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/health", async context =>
{
    var isHealthy = true;
    context.Response.StatusCode = isHealthy
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;
    await context.Response.WriteAsync(isHealthy
        ? "Healthy"
        : "Unhealthy");
});

app.MapPost("{*path}", async (HttpContext context, string path) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    var receiptId = $"receipt_{Guid.NewGuid():N}";

    logger.LogInformation("POST Request: {@RequestDetails}", path);

    return Results.Ok(new
        {
            Id = receiptId,
            Message = "Queued. Thank you."
        }
    );
});

app.Run();

static void RegisterAllQueueListeners(IServiceCollection services, IConfigurationSection rabbitMqConfigSection,
    IRecorder recorder)
{
    RegisterQueueListener<AuditMessage>(services, WorkerConstants.Queues.Audits, "DefaultWorkQueuePolicy", recorder);
    RegisterQueueListener<EmailMessage>(services, WorkerConstants.Queues.Emails, "DefaultWorkQueuePolicy", recorder);
    RegisterQueueListener<ProvisioningMessage>(services, WorkerConstants.Queues.Provisionings, "DefaultWorkQueuePolicy",
        recorder);
    RegisterQueueListener<SmsMessage>(services, WorkerConstants.Queues.Smses, "DefaultWorkQueuePolicy", recorder);
    RegisterQueueListener<UsageMessage>(services, WorkerConstants.Queues.Usages, "DefaultWorkQueuePolicy", recorder);

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
        recorder.Crash(null, CrashLevel.Critical, null,
            "Nombre del Exchange de Eventos de Dominio no configurado en '{ConfigKey}:Name'. No se iniciarán listeners de eventos de dominio.",
            domainEventsExchangeConfigKey);
    }
    else
    {
        foreach (var bindingKeyPrefix in domainEventBindingKeys)
        {
            string fullBindingConfigKey = $"Bindings:{bindingKeyPrefix}";
            string queueConfigPath = rabbitMqConfigSection.GetValue<string>($"{fullBindingConfigKey}:QueueConfigKey");
            string listenerQueueName = rabbitMqConfigSection.GetValue<string>(queueConfigPath);
            string routingKey = rabbitMqConfigSection.GetValue<string>($"{fullBindingConfigKey}:RoutingKey");
            string subscriptionName =
                rabbitMqConfigSection.GetValue<string>($"{fullBindingConfigKey}:SubscriptionName");

            if (string.IsNullOrWhiteSpace(listenerQueueName) || string.IsNullOrWhiteSpace(routingKey)
                                                             || string.IsNullOrWhiteSpace(subscriptionName))
            {
                recorder.TraceError(null,
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
}

static void RegisterQueueListener<TMessage>(
    IServiceCollection services,
    string queueName,
    string retryPolicyName,
    IRecorder recorder)
    where TMessage : class, IQueuedMessage
{
    recorder.TraceInformation(null, $"{typeof(TMessage).Name.Replace("Message", "")}Listener-{queueName}");
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