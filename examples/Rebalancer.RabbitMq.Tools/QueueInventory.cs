namespace Rebalancer.RabbitMq.Tools;

public class QueueInventory
{
    public string ConsumerGroup { get; init; }
    public string ExchangeName { get; init; }
    public string QueuePrefix { get; init; }
    public int QueueCount { get; init; }
    public int LeaseExpirySeconds { get; init; }
    public int HeartbeatSeconds { get; init; }

    /// <summary>Deliveries of one message before RabbitMQ dead-letters it (<c>x-delivery-limit</c>).</summary>
    public int DeliveryLimit { get; init; }

    /// <summary>Dead-letter exchange and queue of the group: failed and parked messages end here (US-0022).</summary>
    public string DeadLetterExchange => ExchangeName + ".dlx";

    public string DeadLetterQueue => ExchangeName + ".dlq";
}
