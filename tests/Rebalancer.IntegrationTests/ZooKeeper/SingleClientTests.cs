using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.IntegrationTests.ZooKeeper.Helpers;
using Rebalancer.ZooKeeper;
using Xunit;

namespace Rebalancer.IntegrationTests.ZooKeeper;

public class SingleClientTests : IDisposable
{
    private readonly ZkHelper zkHelper = new();

    // each test sets the provider it needs; clients get it explicitly (see PRB-0002)
    private Func<IRebalancerProvider> providerFactory = null!;

    public void Dispose()
    {
        if (zkHelper != null)
        {
            Task.Run(async () => await zkHelper.CloseAsync()).Wait();
        }
    }

    [Fact]
    public async Task ResourceBarrier_GivenSingleClient_ThenGetsAllResourcesAssigned()
    {
        providerFactory = GetResourceBarrierProvider;
        await GivenSingleClient_ThenGetsAllResourcesAssigned();
    }

    [Fact]
    public async Task GlobalBarrier_GivenSingleClient_ThenGetsAllResourcesAssigned()
    {
        providerFactory = GetGlobalBarrierProvider;
        await GivenSingleClient_ThenGetsAllResourcesAssigned();
    }

    [Theory]
    [InlineData(RebalancingMode.GlobalBarrier)]
    [InlineData(RebalancingMode.ResourceBarrier)]
    public async Task GivenNoLogger_ThenGetsAllResourcesAssigned(RebalancingMode mode)
    {
        // like the SQL Server and Redis providers, a null logger must fall back to a no-op logger
        providerFactory = () => new ZooKeeperProvider(
            ZkHelper.zooKeeperHosts,
            "/rebalancer",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(5),
            mode,
            null,
            ZooDefs.Ids.OPEN_ACL_UNSAFE, // open ACL on purpose (ADR-0011): AclTests covers ACLs
            null,
            null
        );
        await GivenSingleClient_ThenGetsAllResourcesAssigned();
    }

    // PRB-0006: a client stopped with StopAsync reports Stopped, not NotStarted
    [Theory]
    [InlineData(RebalancingMode.GlobalBarrier)]
    [InlineData(RebalancingMode.ResourceBarrier)]
    public async Task GivenSingleClient_WhenStopped_ThenReportsStopped(RebalancingMode mode)
    {
        providerFactory = mode == RebalancingMode.GlobalBarrier ? GetGlobalBarrierProvider : GetResourceBarrierProvider;
        var groupName = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(groupName, "res", 2);
        var (client, _) = CreateClient();
        await client.StartAsync(
            groupName,
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (client.GetCurrentState() != ClientState.Assigned && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(ClientState.Assigned, client.GetCurrentState());

        await client.StopAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(ClientState.Stopped, client.GetCurrentState());
        Assert.Equal(ClientState.Stopped, client.GetAssignedResources().ClientState);
    }

    private async Task GivenSingleClient_ThenGetsAllResourcesAssigned()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(groupName, "res", 5);

        List<string> expectedAssignedResources = new()
        {
            "res0",
            "res1",
            "res2",
            "res3",
            "res4",
        };

        // ACT
        var (client, testEvents) = CreateClient();
        await client.StartAsync(
            groupName,
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT
        Assert.Single(testEvents);
        Assert.Equal(EventType.Assignment, testEvents[0].EventType);
        Assert.True(ResourcesMatch(expectedAssignedResources, testEvents[0].Resources.ToList()));

        await client.StopAsync(TimeSpan.FromSeconds(30));
    }

    private (RebalancerClient, List<TestEvent> testEvents) CreateClient()
    {
        RebalancerClient client1 = new(providerFactory());
        List<TestEvent> testEvents = new();
        client1.OnAssignment += (sender, args) =>
        {
            testEvents.Add(
                new TestEvent
                {
                    EventType = EventType.Assignment,
                    Resources = args.Resources,
                }
            );
        };

        client1.OnUnassignment += (sender, args) =>
        {
            testEvents.Add(
                new TestEvent
                {
                    EventType = EventType.Unassignment,
                }
            );
        };

        client1.OnAborted += (sender, args) =>
        {
            testEvents.Add(
                new TestEvent
                {
                    EventType = EventType.Error,
                }
            );
            Console.WriteLine($"OnAborted: {args.Exception}");
        };

        return (client1, testEvents);
    }

    private bool ResourcesMatch(List<string> expectedRes, List<string> actualRes) =>
        expectedRes.OrderBy(x => x).SequenceEqual(actualRes.OrderBy(x => x));

    private IRebalancerProvider GetResourceBarrierProvider() => new ZooKeeperProvider(
        ZkHelper.zooKeeperHosts,
        "/rebalancer",
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(5),
        RebalancingMode.ResourceBarrier,
        new TestOutputLogger(),
        ZooDefs.Ids.OPEN_ACL_UNSAFE, // open ACL on purpose (ADR-0011): AclTests covers ACLs
        null,
        null
    );

    private IRebalancerProvider GetGlobalBarrierProvider() => new ZooKeeperProvider(
        ZkHelper.zooKeeperHosts,
        "/rebalancer",
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(5),
        RebalancingMode.GlobalBarrier,
        new TestOutputLogger(),
        ZooDefs.Ids.OPEN_ACL_UNSAFE, // open ACL on purpose (ADR-0011): AclTests covers ACLs
        null,
        null
    );
}
