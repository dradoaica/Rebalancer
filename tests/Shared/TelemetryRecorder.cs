using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Rebalancer.Core;

namespace Rebalancer.Tests.Shared;

/// <summary>
///     Collects Rebalancer measurements and activities (ADR-0022) for one resource group. Listeners are process-wide and
///     tests run in parallel, so everything is filtered by the <c>rebalancer.group</c> tag.
/// </summary>
internal sealed class TelemetryRecorder : IDisposable
{
    private readonly ActivityListener activityListener;
    private readonly string group;
    private readonly MeterListener meterListener;

    public TelemetryRecorder(string group)
    {
        this.group = group;
        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == RebalancerTelemetry.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        meterListener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i.Name, v, tags));
        meterListener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i.Name, v, tags));
        meterListener.Start();

        activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RebalancerTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("rebalancer.group"), group))
                {
                    Activities.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(activityListener);
    }

    public ConcurrentBag<Measurement> Measurements { get; } = new();
    public ConcurrentBag<Activity> Activities { get; } = new();

    public void Dispose()
    {
        meterListener.Dispose();
        activityListener.Dispose();
    }

    public double Sum(string instrument, string? outcome = null) =>
        Measurements.Where(m => m.Instrument == instrument && (outcome == null || Equals(m.Tags.GetValueOrDefault("rebalancer.outcome"), outcome)))
            .Sum(m => m.Value);

    /// <summary>Every tag value of every measurement and activity, to check that no resource name leaks.</summary>
    public IEnumerable<string?> AllTagValues() =>
        Measurements.SelectMany(m => m.Tags.Values.Select(v => v?.ToString()))
            .Concat(Activities.SelectMany(a => a.TagObjects.Select(t => t.Value?.ToString())));

    private void Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        if (Equals(copy.GetValueOrDefault("rebalancer.group"), group))
        {
            Measurements.Add(new Measurement(instrument, value, copy));
        }
    }

    internal sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, object?> Tags);
}
