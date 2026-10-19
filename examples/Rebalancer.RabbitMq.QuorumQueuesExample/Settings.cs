using System;

namespace Rebalancer.RabbitMq.QuorumQueuesExample;

/// <summary>Example settings from environment variables, so no connection string lives in source (US-0022).</summary>
internal sealed class Settings
{
    public string Node { get; private init; }
    public string SqlConnectionString { get; private init; }
    public string RabbitHost { get; private init; }
    public int RabbitPort { get; private init; }
    public string RabbitUser { get; private init; }
    public string RabbitPassword { get; private init; }
    public string ConsumerGroup { get; private init; }

    /// <summary>
    ///     Attempts of one turn before it is dead-lettered and its conversation blocked; use the queues'
    ///     <c>x-delivery-limit</c> (Tools <c>DeliveryLimit</c>), which stays the backstop for crash redeliveries.
    /// </summary>
    public int DeliveryLimit { get; private init; }

    public TimeSpan WorkDuration { get; private init; }

    // fault injection: this turn of this conversation always fails, to show retries, dead-lettering and blocking
    public string FailConversation { get; private init; }
    public int FailTurn { get; private init; }

    public static Settings FromEnvironment() =>
        new()
        {
            Node = Get("EXAMPLE_NODE", Environment.MachineName),
            SqlConnectionString =
                Get(
                    "EXAMPLE_SQL",
                    "Server=localhost;Database=RabbitMqScaling;User Id=sa;Password=<set EXAMPLE_SQL>;TrustServerCertificate=true"
                ),
            RabbitHost = Get("EXAMPLE_RABBIT_HOST", "localhost"),
            RabbitPort = int.Parse(Get("EXAMPLE_RABBIT_PORT", "5672")),
            RabbitUser = Get("EXAMPLE_RABBIT_USER", "guest"),
            RabbitPassword = Get("EXAMPLE_RABBIT_PASSWORD", "guest"),
            ConsumerGroup = Get("EXAMPLE_GROUP", "NotificationsGroup"),
            DeliveryLimit = int.Parse(Get("EXAMPLE_DELIVERY_LIMIT", "5")),
            WorkDuration = TimeSpan.FromMilliseconds(int.Parse(Get("EXAMPLE_WORK_MS", "50"))),
            FailConversation = Get("EXAMPLE_FAIL_CONVERSATION", ""),
            FailTurn = int.Parse(Get("EXAMPLE_FAIL_TURN", "0")),
        };

    private static string Get(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }
}
