using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.IntegrationTests.ZooKeeper.Helpers;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.ResourceManagement;
using Rebalancer.ZooKeeper.Zk;
using Xunit;

namespace Rebalancer.IntegrationTests.ZooKeeper;

/// <summary>
///     ADR-0027 against a real ensemble: a client removes only the resource barriers it holds. Before ADR-0027 a
///     cancelled rebalancing deleted the barrier another client still held, letting a third client start the resource.
/// </summary>
public class BarrierOwnershipTests : IDisposable
{
    private const string Root = "/rebalancer";
    private const string Resource = "res0";
    private readonly List<ZooKeeperService> sessions = [];
    private readonly ZkHelper zkHelper = new();

    public void Dispose() =>
        Task.Run(async () =>
                {
                    foreach (var session in sessions)
                    {
                        await session.CloseSessionAsync();
                    }

                    await zkHelper.CloseAsync();
                }
            )
            .Wait();

    [Fact]
    public async Task AClientRemovesOnlyItsOwnBarrier()
    {
        var group = await PrepareGroupAsync();
        var (owner, ownerId) = await ConnectAsync(group);
        var (other, _) = await ConnectAsync(group);
        await owner.TryPutResourceBarrierAsync(Resource, CancellationToken.None, new NullRebalancerLogger());

        await other.RemoveResourceBarrierAsync(Resource);
        Assert.Equal(ownerId, await zkHelper.GetBarrierOwnerAsync(group, Resource));

        await owner.RemoveResourceBarrierAsync(Resource);
        Assert.Null(await zkHelper.GetBarrierOwnerAsync(group, Resource));
    }

    [Fact]
    public async Task ACancelledStart_LeavesTheBarrierAnotherClientHolds()
    {
        var group = await PrepareGroupAsync();
        var (owner, ownerId) = await ConnectAsync(group);
        var (waiting, waitingId) = await ConnectAsync(group);
        await owner.TryPutResourceBarrierAsync(Resource, CancellationToken.None, new NullRebalancerLogger());
        var started = false;
        var actions = new OnChangeActions();
        actions.AddOnStartAction(_ => started = true);
        var manager = new ResourceManager(
            waiting,
            new NullRebalancerLogger(),
            actions,
            RebalancingMode.ResourceBarrier
        );

        // a newer assignment table cancels the rebalancing while this client waits for the owner's barrier
        using var newerTable = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await manager.InvokeOnStartActionsAsync(
            waitingId,
            "Follower",
            [Resource,],
            newerTable.Token,
            CancellationToken.None
        );

        Assert.False(started);
        Assert.Equal(ownerId, await zkHelper.GetBarrierOwnerAsync(group, Resource));
    }

    private async Task<string> PrepareGroupAsync()
    {
        var group = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync(Root, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(group, "res", 1);
        return group;
    }

    private async Task<(ZooKeeperService Service, string ClientId)> ConnectAsync(string group)
    {
        var service = new ZooKeeperService(ZkHelper.zooKeeperHosts, ZooDefs.Ids.OPEN_ACL_UNSAFE, null, null);
        sessions.Add(service);
        Assert.True(
            await service.StartSessionAsync(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20), CancellationToken.None)
        );
        await service.InitializeResourceBarrierAsync(
            $"{Root}/{group}/clients",
            $"{Root}/{group}/resources",
            $"{Root}/{group}/epoch"
        );
        var clientPath = await service.CreateClientAsync();
        return (service, clientPath[(clientPath.LastIndexOf('/') + 1)..]);
    }
}
