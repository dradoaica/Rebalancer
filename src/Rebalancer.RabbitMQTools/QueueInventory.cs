namespace Rebalancer.RabbitMQTools;

public class QueueInventory
{
    public string ConsumerGroup { get; init; }
    public string ExchangeName { get; init; }
    public string QueuePrefix { get; init; }
    public int QueueCount { get; init; }
    public int LeaseExpirySeconds { get; init; }
}
