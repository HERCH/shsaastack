#if !TESTINGONLY
using Infrastructure.Persistence.Common.ApplicationServices;
#if HOSTEDONAZURE
using Infrastructure.External.Persistence.Azure.ApplicationServices;
#elif HOSTEDONAWS
using Infrastructure.External.Persistence.AWS.ApplicationServices;
#elif HOSTEDONPREMISES
using Infrastructure.Broker.RabbitMq.Configuration;
using Infrastructure.Broker.RabbitMq.Extensions;
using Infrastructure.Broker.RabbitMq.Publishing;
using Infrastructure.Broker.RabbitMq.Topology;
using Infrastructure.External.Persistence.OnPremises.ApplicationServices;
using Infrastructure.External.Persistence.OnPremises.Extensions;
#endif
#else
using Infrastructure.Persistence.Interfaces;
#endif
using Application.Interfaces;
using Application.Interfaces.Services;
using Application.Persistence.Shared;
using Application.Persistence.Shared.ReadModels;
using Common;
using Common.Configuration;
using Common.Extensions;
using Common.Recording;
using Domain.Interfaces;
using Domain.Interfaces.Services;
using Infrastructure.Persistence.Shared.ApplicationServices;

namespace Infrastructure.Common.Recording;

/// <summary>
///     Provides an <see cref="IUsageReporter" /> that asynchronously brokers the audit to a reliable queue for future
///     delivery
/// </summary>
public class QueuedUsageReporter : IUsageReporter
{
    private readonly IHostSettings _hostSettings;
    private readonly IUsageMessageQueue _queue;

    // ReSharper disable once UnusedParameter.Local
    public QueuedUsageReporter(IDependencyContainer container, IConfigurationSettings settings,
        IHostSettings hostSettings)
        : this(new UsageMessageQueue(NoOpRecorder.Instance,
            container.GetRequiredService<IHostSettings>(),
            container.GetRequiredService<IMessageQueueMessageIdFactory>(),
#if !TESTINGONLY
#if HOSTEDONAZURE
            AzureStorageAccountQueueStore.Create(NoOpRecorder.Instance, AzureStorageAccountStoreOptions.Credentials(settings))
#elif HOSTEDONAWS
            AWSSQSQueueStore.Create(NoOpRecorder.Instance, settings)
#elif HOSTEDONPREMISES
            RabbitMqQueueStore.Create(
                container.GetRequiredService<IMessagePublisher>(),
                container.GetRequiredService<ITopologyManager>(),
                settings.GetOptions<RabbitMqOptions>(RabbitMqOptions.SectionName),
                NoOpRecorder.Instance,
                container.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            )
#endif
#else
            container.GetRequiredServiceForPlatform<IQueueStore>()
#endif
        ), hostSettings)
    {
    }

    internal QueuedUsageReporter(IUsageMessageQueue queue, IHostSettings hostSettings)
    {
        _queue = queue;
        _hostSettings = hostSettings;
    }

    public async Task<Result<Error>> TrackAsync(ICallContext? call, string forId, string eventName,
        Dictionary<string, object>? additional = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(forId);
        ArgumentException.ThrowIfNullOrEmpty(eventName);

        var region = _hostSettings.GetRegion();
        var safeCall = call ?? CallContext.CreateUnknown(region);
        var properties = additional ?? new Dictionary<string, object>();

        properties[UsageConstants.Properties.CallId] = safeCall.CallId;
        properties[UsageConstants.Properties.TenantId] = safeCall.TenantId.ValueOrNull!;

        var message = new UsageMessage
        {
            EventName = eventName,
            ForId = forId,
            Additional = properties.ToDictionary(pair => pair.Key, pair => pair.Value.Exists()
                ? pair.Value.ToString() ?? string.Empty
                : string.Empty)
        };

        var queued = await _queue.PushAsync(safeCall, message, cancellationToken);
        if (queued.IsFailure)
        {
            return queued.Error;
        }

        return Result.Ok;
    }
}