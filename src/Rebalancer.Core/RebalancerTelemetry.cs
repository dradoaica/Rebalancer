using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Rebalancer.Core;

/// <summary>
///     Metrics and traces of Rebalancer (ADR-0022). Collect them with OpenTelemetry's <c>AddMeter(MeterName)</c> and
///     <c>AddSource(ActivitySourceName)</c>. Instrument, activity and tag names are a public contract: they change only
///     with a superseding decision record and a major version. Resource names are never emitted.
/// </summary>
public static class RebalancerTelemetry
{
    /// <summary>Name of the <see cref="Meter" /> that carries every Rebalancer instrument.</summary>
    public const string MeterName = "Rebalancer";

    /// <summary>Name of the <see cref="System.Diagnostics.ActivitySource" /> of Rebalancer's activities.</summary>
    public const string ActivitySourceName = "Rebalancer";

    internal const string BackendTag = "rebalancer.backend";
    internal const string GroupTag = "rebalancer.group";
    internal const string OutcomeTag = "rebalancer.outcome";
    internal const string FencingTokenTag = "rebalancer.fencing_token";
    internal const string EpochTag = "rebalancer.epoch";

    private static readonly string Version = typeof(RebalancerTelemetry).Assembly.GetName().Version?.ToString();
    private static readonly Meter Meter = new(MeterName, Version);
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, Version);

    private static readonly Counter<long> Rebalances = Meter.CreateCounter<long>(
        "rebalancer.rebalances",
        "{rebalance}",
        "Rebalances run by this node as coordinator"
    );

    private static readonly Counter<long> Elections = Meter.CreateCounter<long>(
        "rebalancer.coordinator.elections",
        "{election}",
        "Times this node became coordinator"
    );

    private static readonly Counter<long> Assignments = Meter.CreateCounter<long>(
        "rebalancer.assignments",
        "{event}",
        "OnAssignment raised on this node"
    );

    private static readonly Counter<long> Unassignments = Meter.CreateCounter<long>(
        "rebalancer.unassignments",
        "{event}",
        "OnUnassignment raised on this node"
    );

    private static readonly Histogram<double> UnassignedDuration = Meter.CreateHistogram<double>(
        "rebalancer.unassigned.duration",
        "s",
        "Time from an unassignment to the next assignment on this node"
    );

    /// <summary>Starts the activity of one rebalance on the coordinator; null when nobody listens.</summary>
    internal static Activity StartRebalance(string backend, string group, string tokenTag, long token) =>
        // the activity name is part of the telemetry contract (ADR-0022), not the caller's member name
        // ReSharper disable once ExplicitCallerInfoArgument
        ActivitySource.StartActivity("rebalancer.rebalance")
            ?.SetTag(BackendTag, backend)
            .SetTag(GroupTag, group)
            .SetTag(tokenTag, token);

    internal static void RecordRebalance(string backend, string group, bool completed)
    {
        var tags = Tags(backend, group);
        tags.Add(OutcomeTag, completed ? "completed" : "aborted");
        Rebalances.Add(1, tags);
    }

    internal static void RecordElection(string backend, string group) => Elections.Add(1, Tags(backend, group));

    internal static void RecordAssignment(string backend, string group) => Assignments.Add(1, Tags(backend, group));

    internal static void RecordUnassignment(string backend, string group) => Unassignments.Add(1, Tags(backend, group));

    internal static void RecordUnassignedDuration(string backend, string group, TimeSpan duration) =>
        UnassignedDuration.Record(duration.TotalSeconds, Tags(backend, group));

    /// <summary>
    ///     The <c>rebalancer.backend</c> value of a provider. Matched by type name because the core cannot reference the
    ///     backends; any other provider is <c>custom</c>, so the tag stays bounded.
    /// </summary>
    internal static string BackendOf(IRebalancerProvider provider) =>
        provider.GetType().FullName switch
        {
            "Rebalancer.SqlServer.SqlServerProvider" => "sqlserver",
            "Rebalancer.Redis.RedisProvider" => "redis",
            "Rebalancer.ZooKeeper.ZooKeeperProvider" => "zookeeper",
            _ => "custom",
        };

    private static TagList Tags(string backend, string group) =>
        new()
        {
            {
                BackendTag, backend
            },
            {
                GroupTag, group
            },
        };
}
