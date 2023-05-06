namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

using System;

public class RandomConfig
{
    public RandomConfig()
    {
        this.SessionTimeout = TimeSpan.FromSeconds(20);
        this.ConnectTimeout = TimeSpan.FromSeconds(20);
        this.MinimumRebalancingInterval = TimeSpan.FromSeconds(20);
        this.StartUpClientInterval = TimeSpan.Zero;
        this.OnStartEventHandlerTime = TimeSpan.Zero;
        this.OnStopEventHandlerTime = TimeSpan.Zero;
        this.RandomiseEventHandlerTimes = false;
        this.OnAssignmentDelay = TimeSpan.Zero;
    }

    public TimeSpan SessionTimeout { get; set; }
    public TimeSpan ConnectTimeout { get; set; }
    public TimeSpan MinimumRebalancingInterval { get; set; }
    public TimeSpan StartUpClientInterval { get; set; }
    public int ClientCount { get; set; }
    public int ResourceCount { get; set; }
    public TimeSpan TestDuration { get; set; }
    public CheckType CheckType { get; set; }
    public bool RandomiseInterval { get; set; }
    public TimeSpan MaxInterval { get; set; }
    public int ConditionalCheckInterval { get; set; }
    public TimeSpan ConditionalCheckWaitPeriod { get; set; }
    public TimeSpan OnStartEventHandlerTime { get; set; }
    public TimeSpan OnStopEventHandlerTime { get; set; }
    public TimeSpan OnAssignmentDelay { get; set; }
    public bool RandomiseEventHandlerTimes { get; set; }
}
