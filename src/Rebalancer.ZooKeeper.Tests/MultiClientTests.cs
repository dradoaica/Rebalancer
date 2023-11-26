namespace Rebalancer.ZooKeeper.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core;
using Helpers;
using Xunit;

public class MultiClientTests : IDisposable
{
    private readonly ZkHelper zkHelper;

    public MultiClientTests() => this.zkHelper = new ZkHelper();

    public void Dispose()
    {
        if (this.zkHelper != null)
        {
            Task.Run(async () => await this.zkHelper.CloseAsync()).Wait();
        }
    }


    [Fact]
    public async Task ResourceBarrier_GivenSixResourcesAndThreeClients_ThenEachClientGetsTwoResources()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this.GivenSixResourcesAndThreeClients_ThenEachClientGetsTwoResources();
    }

    [Fact]
    public async Task GlobalBarrier_GivenSixResourcesAndThreeClients_ThenEachClientGetsTwoResources()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this.GivenSixResourcesAndThreeClients_ThenEachClientGetsTwoResources();
    }

    private async Task GivenSixResourcesAndThreeClients_ThenEachClientGetsTwoResources()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 6);

        // ACT
        var (client1, testEvents1) = this.CreateClient();
        var (client2, testEvents2) = this.CreateClient();
        var (client3, testEvents3) = this.CreateClient();

        await client1.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});
        await client2.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});
        await client3.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT
        // check client 1
        this.AssertAssigned(testEvents1, 2);
        this.AssertAssigned(testEvents2, 2);
        this.AssertAssigned(testEvents3, 2);

        await client1.StopAsync();
        await client2.StopAsync();
        await client3.StopAsync();
    }

    [Fact]
    public async Task
        ResourceBarrier_GivenMultipleResourcesAndThreeClientsAtStartWithClientsStoppingOneByOne_ThenResourceAssignmentsReassignedAccordingly()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this
            .GivenMultipleResourcesAndThreeClientsAtStartWithClientsStoppingOneByOne_ThenResourceAssignmentsReassignedAccordingly();
    }

    [Fact]
    public async Task
        GlobalBarrier_GivenMultipleResourcesAndThreeClientsAtStartWithClientsStoppingOneByOne_ThenResourceAssignmentsReassignedAccordingly()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this
            .GivenMultipleResourcesAndThreeClientsAtStartWithClientsStoppingOneByOne_ThenResourceAssignmentsReassignedAccordingly();
    }

    private async Task
        GivenMultipleResourcesAndThreeClientsAtStartWithClientsStoppingOneByOne_ThenResourceAssignmentsReassignedAccordingly()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 6);

        // ACT - start up three clients
        var (client1, testEvents1) = this.CreateClient();
        var (client2, testEvents2) = this.CreateClient();
        var (client3, testEvents3) = this.CreateClient();

        await client1.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});
        await client2.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});
        await client3.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT all clients have been assigned equal resources
        this.AssertAssigned(testEvents1, 2);
        this.AssertAssigned(testEvents2, 2);
        this.AssertAssigned(testEvents3, 2);

        // ACT - stop one client
        await client1.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT - stopped client has had resources unassigned and remaining two
        // have been equally assigned the 6 resources between them
        this.AssertUnassignedOnly(testEvents1);
        this.AssertUnassignedThenAssigned(testEvents2, 3);
        this.AssertUnassignedThenAssigned(testEvents3, 3);

        // ACT - stop one client
        await client2.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT - stopped client has had resources unassigned and remaining one client
        // have been assigned all 6 resources between them
        this.AssertNoEvents(testEvents1);
        this.AssertUnassignedOnly(testEvents2);
        this.AssertUnassignedThenAssigned(testEvents3, 6);

        // clean up
        await client3.StopAsync();
    }

    [Fact]
    public async Task
        ResourceBarrier_GivenMultipleResourcesAndOneClientAtStartWithNewClientsStartingOneByOne_ThenResourceAssignmentsReassignedAccordingly()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this
            .GivenMultipleResourcesAndOneClientAtStartWithNewClientsStartingOneByOne_ThenResourceAssignmentsReassignedAccordingly();
    }

    [Fact]
    public async Task
        GlobalBarrier_GivenMultipleResourcesAndOneClientAtStartWithNewClientsStartingOneByOne_ThenResourceAssignmentsReassignedAccordingly()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this
            .GivenMultipleResourcesAndOneClientAtStartWithNewClientsStartingOneByOne_ThenResourceAssignmentsReassignedAccordingly();
    }

    private async Task
        GivenMultipleResourcesAndOneClientAtStartWithNewClientsStartingOneByOne_ThenResourceAssignmentsReassignedAccordingly()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 6);

        // ACT - create three clients and start one
        var (client1, testEvents1) = this.CreateClient();
        var (client2, testEvents2) = this.CreateClient();
        var (client3, testEvents3) = this.CreateClient();

        await client1.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT - client 1 that has started has been assigned all resources
        // but that clients 2 and 3 that have not started has been assigned nothing
        this.AssertAssigned(testEvents1, 6);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // ACT - start one client
        await client2.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT - client 1 and 2 that have started have been equally assigned resources
        // but that client 3 that has not started has been assigned nothing
        this.AssertUnassignedThenAssigned(testEvents1, 3);
        this.AssertAssigned(testEvents2, 3);
        this.AssertNoEvents(testEvents3);

        // ACT - start one client
        await client3.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(15));

        // ASSERT - all clients have been equally assigned the resources
        this.AssertUnassignedThenAssigned(testEvents1, 2);
        this.AssertUnassignedThenAssigned(testEvents2, 2);
        this.AssertAssigned(testEvents3, 2);

        // clean up
        await client1.StopAsync();
        await client2.StopAsync();
        await client3.StopAsync();
    }

    [Fact]
    public async Task ResourceBarrier_GivenOneResourceAndOneClient_ThenAdditionalClientsDoNotTriggerRebalancing()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this.GivenOneResourceAndOneClient_ThenAdditionalClientsDoNotTriggerRebalancing();
    }

    [Fact]
    public async Task GlobalBarrier_GivenOneResourceAndOneClient_ThenAdditionalClientsDoNotTriggerRebalancing()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this.GivenOneResourceAndOneClient_ThenAdditionalClientsDoNotTriggerRebalancing();
    }

    private async Task GivenOneResourceAndOneClient_ThenAdditionalClientsDoNotTriggerRebalancing()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 1);

        // ACT - create three clients and start one
        var (client1, testEvents1) = this.CreateClient();
        var (client2, testEvents2) = this.CreateClient();
        var (client3, testEvents3) = this.CreateClient();

        await client1.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - client 1 that has started has been assigned all resources
        // but that clients 2 and 3 that have not started has been assigned nothing
        this.AssertAssigned(testEvents1, 1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // ACT - start one client
        await client2.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - no rebalancing occurs
        this.AssertNoEvents(testEvents1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // ACT - start one client
        await client3.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - no rebalancing occurs
        this.AssertNoEvents(testEvents1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // ACT - stop one client
        await client2.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - no rebalancing occurs
        this.AssertNoEvents(testEvents1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // clean up
        await client1.StopAsync();
        await client3.StopAsync();
    }

    [Fact]
    public async Task
        ResourceBarrier_GivenTwoResourcesAndThreeClients_ThenRemovingUnassignedClientDoesNotTriggerRebalancing()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this.GivenTwoResourcesAndThreeClients_ThenRemovingUnassignedClientDoesNotTriggerRebalancing();
    }

    [Fact]
    public async Task
        GlobalBarrier_GivenTwoResourcesAndThreeClients_ThenRemovingUnassignedClientDoesNotTriggerRebalancing()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this.GivenTwoResourcesAndThreeClients_ThenRemovingUnassignedClientDoesNotTriggerRebalancing();
    }

    private async Task GivenTwoResourcesAndThreeClients_ThenRemovingUnassignedClientDoesNotTriggerRebalancing()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 2);

        // ACT - create three clients and start one
        var (client1, testEvents1) = this.CreateClient();
        var (client2, testEvents2) = this.CreateClient();
        var (client3, testEvents3) = this.CreateClient();

        await client1.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});
        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - client 1 has started has each been assigned both resources
        // but that clients 2 and 3 that have not started have been assigned nothing
        this.AssertAssigned(testEvents1, 2);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        await client2.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - client 1 and 2 have started have each been assigned a resource
        // but that clients 3 that has not started has been assigned nothing
        this.AssertUnassignedThenAssigned(testEvents1, 1);
        this.AssertAssigned(testEvents2, 1);
        this.AssertNoEvents(testEvents3);

        // ACT - start client 3
        await client3.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - no rebalancing occurs
        this.AssertNoEvents(testEvents1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // ACT - remove client3
        await client3.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - no rebalancing occurs
        this.AssertNoEvents(testEvents1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // clean up
        await client1.StopAsync();
        await client2.StopAsync();
    }

    [Fact]
    public async Task
        ResourceBarrier_GivenTwoResourcesAndThreeClients_ThenRemovingAssignedClientDoesTriggerRebalancing()
    {
        Providers.Register(this.GetResourceBarrierProvider);
        await this.GivenTwoResourcesAndThreeClients_ThenRemovingAssignedClientDoesTriggerRebalancing();
    }

    [Fact]
    public async Task
        GlobalBarrier_GivenTwoResourcesAndThreeClients_ThenRemovingAssignedClientDoesTriggerRebalancing()
    {
        Providers.Register(this.GetGlobalBarrierProvider);
        await this.GivenTwoResourcesAndThreeClients_ThenRemovingAssignedClientDoesTriggerRebalancing();
    }

    private async Task GivenTwoResourcesAndThreeClients_ThenRemovingAssignedClientDoesTriggerRebalancing()
    {
        // ARRANGE
        var groupName = Guid.NewGuid().ToString();
        await this.zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await this.zkHelper.PrepareResourceGroupAsync(groupName, "res", 2);

        // ACT - create three clients and start one
        var (client1, testEvents1) = this.CreateClient();
        var (client2, testEvents2) = this.CreateClient();
        var (client3, testEvents3) = this.CreateClient();

        await client1.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});
        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - client 1 has started has each been assigned both resources
        // but that clients 2 and 3 that have not started have been assigned nothing
        this.AssertAssigned(testEvents1, 2);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        await client2.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - client 1 and 2 have started have each been assigned a resource
        // but that clients 3 that has not started has been assigned nothing
        this.AssertUnassignedThenAssigned(testEvents1, 1);
        this.AssertAssigned(testEvents2, 1);
        this.AssertNoEvents(testEvents3);

        // ACT - start client 3
        await client3.StartAsync(groupName, new ClientOptions {AutoRecoveryOnError = false});

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - no rebalancing occurs
        this.AssertNoEvents(testEvents1);
        this.AssertNoEvents(testEvents2);
        this.AssertNoEvents(testEvents3);

        // ACT - remove client2
        await client2.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(10));

        // ASSERT - rebalancing occurs
        this.AssertUnassignedThenAssigned(testEvents1, 1);
        this.AssertUnassignedOnly(testEvents2);
        this.AssertAssigned(testEvents3, 1);

        // clean up
        await client1.StopAsync();
        await client3.StopAsync();
    }

    private void AssertAssigned(List<TestEvent> testEvents, int count)
    {
        Assert.Equal(1, testEvents.Count);
        Assert.Equal(EventType.Assignment, testEvents[0].EventType);
        Assert.Equal(count, testEvents[0].Resources.Count);
        testEvents.Clear();
    }

    private void AssertUnassignedThenAssigned(List<TestEvent> testEvents, int count)
    {
        Assert.Equal(2, testEvents.Count);
        Assert.Equal(EventType.Unassignment, testEvents[0].EventType);
        Assert.Equal(EventType.Assignment, testEvents[1].EventType);
        Assert.Equal(count, testEvents[1].Resources.Count);
        testEvents.Clear();
    }

    private void AssertUnassignedOnly(List<TestEvent> testEvents)
    {
        Assert.Equal(1, testEvents.Count);
        Assert.Equal(EventType.Unassignment, testEvents[0].EventType);
        testEvents.Clear();
    }

    private void AssertNoEvents(List<TestEvent> testEvents)
    {
        Assert.Equal(0, testEvents.Count);
        testEvents.Clear();
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
            Console.WriteLine($"OnAborted: {args.AbortReason} {args.Exception}");
        };

        return (client1, testEvents);
    }

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
