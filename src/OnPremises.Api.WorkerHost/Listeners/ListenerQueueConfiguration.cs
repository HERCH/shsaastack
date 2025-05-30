using Infrastructure.Broker.RabbitMq.Configuration;

namespace OnPremises.Api.WorkerHost;


public class ListenerQueueConfiguration
{
    /// <summary>
    /// ID único para este listener (usado para logging, circuit breaker, etc.).
    /// </summary>
    public string ListenerId { get; set; }

    /// <summary>
    /// Nombre de la cola a la que escuchar.
    /// </summary>
    public string QueueName { get; set; }

    /// <summary>
    /// Opciones para declarar la cola (si no está ya definida en la configuración global de RabbitMQOptions).
    /// El listener intentará declararla idempotentemente.
    /// </summary>
    public QueueDeclarationOptions DeclareQueueOptions { get; set; }

    /// <summary>
    /// Indica si se debe bidear esta cola a un exchange.
    /// </summary>
    public bool BindToExchange { get; set; } = false;

    /// <summary>
    /// Nombre del exchange al cual bidear la cola (si BindToExchange es true).
    /// </summary>
    public string ExchangeName { get; set; }

    /// <summary>
    /// Routing key a usar para el bindeo (si BindToExchange es true).
    /// </summary>
    public string RoutingKey { get; set; } = "";

    /// <summary>
    /// Opciones para declarar el exchange (si BindToExchange es true y el exchange necesita ser declarado).
    /// </summary>
    public ExchangeDeclarationOptions DeclareExchangeOptions { get; set; }

    /// <summary>
    /// The name of the retry policy defined in RabbitMqOptions.RetryPolicies to apply to this listener.
    /// If null or empty, no advanced retry policy will be applied (e.g., only circuit breaker and max attempts to DLQ).
    /// </summary>
    public string RetryPolicyName { get; set; }

    /// <summary>
    /// The name of the exchange to which the main work queue is bound.
    /// This is needed for the retry queues' DLX to route messages back.
    /// If the work queue is bound to the default exchange, this might be empty.
    /// </summary>
    public string MainWorkExchangeName { get; set; }

    /// <summary>
    /// The routing key used to bind the main work queue to its MainWorkExchangeName.
    /// Needed for retry queues' DLX to route messages back.
    /// </summary>
    public string MainWorkRoutingKey { get; set; }
    
    public string SubscriberHostName { get; set; }
    public string SubscriptionName { get; set; }

    public ListenerQueueConfiguration()
    {
        DeclareQueueOptions = new QueueDeclarationOptions { Durable = true, AutoDelete = false };
    }
}