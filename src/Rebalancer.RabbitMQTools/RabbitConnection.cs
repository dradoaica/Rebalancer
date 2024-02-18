namespace Rebalancer.RabbitMQTools;

public class RabbitConnection
{
    public string Host { get; init; }
    public int ManagementPort { get; init; }
    public int Port { get; init; }
    public string VirtualHost { get; init; }
    public string Username { get; init; }
    public string Password { get; init; }
}
