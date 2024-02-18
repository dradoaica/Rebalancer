using System;

namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

public class RandomConfig
{
    public TimeSpan SessionTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan MinimumRebalancingInterval { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan StartUpClientInterval { get; init; } = TimeSpan.Zero;
    public int ClientCount { get; init; }
    public int ResourceCount { get; init; }
    public TimeSpan TestDuration { get; init; }
    public CheckType CheckType { get; init; }
    public bool RandomiseInterval { get; init; }
    public TimeSpan MaxInterval { get; init; }
    public int ConditionalCheckInterval { get; init; }
    public TimeSpan ConditionalCheckWaitPeriod { get; init; }
    public TimeSpan OnStartEventHandlerTime { get; init; } = TimeSpan.Zero;
    public TimeSpan OnStopEventHandlerTime { get; init; } = TimeSpan.Zero;
    public TimeSpan OnAssignmentDelay { get; init; } = TimeSpan.Zero;
    public bool RandomiseEventHandlerTimes { get; init; } = false;
}
