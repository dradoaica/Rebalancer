namespace Rebalancer.ZooKeeper.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core;
using Helpers;
using Xunit;

public class SingleClientTests : IDisposable
{
    private readonly ZkHelper zkHelper;

    public SingleClientTests() => this.zkHelper = new ZkHelper();

    public void Dispose()
    {
        if (this.zkHelper != null)
        {
            Task.Run(async () => await this.zkHelper.CloseAsync()).Wait();
        }
    }

    [Fact]
    public async Task ResourceBarrier_GivenSingleClient_ThenGetsAllResourcesAssigned()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this.GivenSingleClient_ThenGetsAllResourcesAssigned();
    }

    [Fact]
    public async Task GlobalBarrier_GivenSingleClient_ThenGetsAllResourcesAssigned()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this.GivenSingleClient_ThenGetsAllResourcesAssigned();
    }

    private async Task GivenSingleClient_ThenGetsAllResourcesAssigned()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 5);

        List<string> expectedAssignedResources = new()
        {
            "res0",
            "res1",
            "res2",
            "res3",
            "res4"
        };

        // ACT
        (var client, var testEvents) = this.CreateClient();
        await client.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT
        Assert.Equal(1, testEvents.Count);
        Assert.Equal(EventType.Assignment, testEvents[0].EventType);
        Assert.True(this.ResourcesMatch(expectedAssignedResources, testEvents[0].Resources.ToList()));

        await client.StopAsync(TimeSpan.FromSeconds(30));
    }

    private (RebalancerClient, List<TestEvent> testEvents) CreateClient()
    {
        RebalancerClient client1 = new();
        List<TestEvent> testEvents = new();
        client1.OnAssignment += (sender, args) =>
        {
            testEvents.Add(new TestEvent {EventType = EventType.Assignment, Resources = args.Resources});
        };

        client1.OnUnassignment += (sender, args) =>
        {
            testEvents.Add(new TestEvent {EventType = EventType.Unassignment});
        };

        client1.OnAborted += (sender, args) =>
        {
            testEvents.Add(new TestEvent {EventType = EventType.Error});
            Console.WriteLine($"OnAborted: {args.Exception}");
        };

        return (client1, testEvents);
    }

    private bool ResourcesMatch(List<string> expectedRes, List<string> actualRes) =>
        expectedRes.OrderBy(x => x).SequenceEqual(actualRes.OrderBy(x => x));

    private IRebalancerProvider GetResourceBarrierProvider() =>
        new ZooKeeperProvider(ZkHelper.ZooKeeperHosts,
            "/rebalancer",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            RebalancingMode.ResourceBarrier,
            new TestOutputLogger());

    private IRebalancerProvider GetGlobalBarrierProvider() =>
        new ZooKeeperProvider(ZkHelper.ZooKeeperHosts,
            "/rebalancer",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            RebalancingMode.GlobalBarrier,
            new TestOutputLogger());
}
