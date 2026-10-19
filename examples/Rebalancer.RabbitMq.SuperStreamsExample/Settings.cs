using System;

namespace Rebalancer.RabbitMq.SuperStreamsExample;

/// <summary>Example settings from environment variables (US-0023).</summary>
internal sealed class Settings
{
    public string Node { get; private init; }
    public string SqlConnectionString { get; private init; }
    public string StreamHost { get; private init; }
    public int StreamPort { get; private init; }
    public string User { get; private init; }
    public string Password { get; private init; }
    public string SuperStream { get; private init; }
    public int Partitions { get; private init; }

    /// <summary>Consumer group name: one active consumer per partition among nodes with the same reference.</summary>
    public string Reference { get; private init; }

    public string ParkingStream => SuperStream + "-parking";
    public int Attempts { get; private init; }
    public TimeSpan WorkDuration { get; private init; }
    public string FailConversation { get; private init; }
    public int FailTurn { get; private init; }

    public static Settings FromEnvironment() =>
        new()
        {
            Node = Get("EXAMPLE_NODE", Environment.MachineName),
            SqlConnectionString = Get("EXAMPLE_SQL",
                "Server=localhost;Database=RabbitMqScaling;User Id=sa;Password=<set EXAMPLE_SQL>;TrustServerCertificate=true"),
            StreamHost = Get("EXAMPLE_STREAM_HOST", "localhost"),
            StreamPort = int.Parse(Get("EXAMPLE_STREAM_PORT", "5552")),
            User = Get("EXAMPLE_RABBIT_USER", "guest"),
            Password = Get("EXAMPLE_RABBIT_PASSWORD", "guest"),
            SuperStream = Get("EXAMPLE_SUPER_STREAM", "chat"),
            Partitions = int.Parse(Get("EXAMPLE_PARTITIONS", "4")),
            Reference = Get("EXAMPLE_REFERENCE", "chat-consumers"),
            Attempts = int.Parse(Get("EXAMPLE_ATTEMPTS", "3")),
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
