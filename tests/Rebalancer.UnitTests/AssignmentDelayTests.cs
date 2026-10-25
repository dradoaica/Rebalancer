using Rebalancer.Core;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Roles;
using Rebalancer.SqlServer.Store;
using System.Diagnostics;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     PRB-0007: <see cref="ClientOptions.OnAssignmentDelay" /> delays OnAssignment after a grant, as on ZooKeeper;
///     it must not delay the keep-alives, or a delay longer than the keep-alive expiry makes a live node look dead. Redis
///     shares the role loop; see the integration tests (<c>*AssignmentDelayTests</c>).
/// </summary>
public sealed class AssignmentDelayTests
{
    private static readonly TimeSpan KeepAliveExpiry = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(4);

    [Fact]
    public async Task KeepAlives_AreNotDelayed_ByTheAssignmentDelay()
    {
        var (provider, clients) = SqlServerFollowerFakes.Create(KeepAliveExpiry, Heartbeat, TimeSpan.FromSeconds(1));
        using var client = new RebalancerClient(provider);
        await client.StartAsync(
            "orders",
            new ClientOptions
            {
                OnAssignmentDelay = Delay,
            }
        );

        await Task.Delay(TimeSpan.FromSeconds(10));
        await client.StopAsync(TimeSpan.FromSeconds(10));

        var keepAlives = clients.KeepAlives;
        Assert.True(keepAlives.Count >= 2, $"only {keepAlives.Count} keep-alive(s) in 10 s");
        var longestGap = keepAlives.Zip(keepAlives.Skip(1), (a, b) => b - a).Max();
        Assert.True(
            longestGap < KeepAliveExpiry,
            $"keep-alives {longestGap.TotalSeconds:F1} s apart, longer than the keep-alive expiry of " +
            $"{KeepAliveExpiry.TotalSeconds} s: the coordinator would count this live node as dead"
        );
    }

    [Fact]
    public async Task OnAssignment_IsRaisedNoEarlierThanTheDelayAfterTheGrant()
    {
        var (provider, clients) = SqlServerFollowerFakes.Create(KeepAliveExpiry, Heartbeat, TimeSpan.FromSeconds(1));
        using var client = new RebalancerClient(provider);
        TimeSpan? assignedAt = null;
        client.OnAssignment += (_, _) => assignedAt ??= clients.Elapsed;
        var started = Stopwatch.StartNew();
        await client.StartAsync(
            "orders",
            new ClientOptions
            {
                OnAssignmentDelay = Delay,
            }
        );

        await SqlServerFollowerFakes.WaitUntilAsync(
            () => assignedAt != null,
            $"OnAssignment was not raised within {started.Elapsed.TotalSeconds:F0} s",
            TimeSpan.FromSeconds(15)
        );
        await client.StopAsync(TimeSpan.FromSeconds(10));

        // the grant is visible from GrantAfter on; the 100 ms margin absorbs timer resolution
        var afterGrant = assignedAt!.Value - clients.GrantAfter;
        Assert.True(
            afterGrant >= Delay - TimeSpan.FromMilliseconds(100),
            $"OnAssignment {afterGrant.TotalSeconds:F1} s after the grant, before the delay of {Delay.TotalSeconds} s"
        );
    }

    [Fact]
    public async Task AStopDuringTheDelay_CancelsTheFollowersStart()
    {
        var (provider, _) = SqlServerFollowerFakes.Create(
            KeepAliveExpiry,
            Heartbeat,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)
        );
        using var client = new RebalancerClient(provider);
        var assignments = 0;
        // ReSharper disable once AccessToModifiedClosure
        client.OnAssignment += (_, _) => Interlocked.Increment(ref assignments);
        await client.StartAsync(
            "orders",
            new ClientOptions
            {
                OnAssignmentDelay = Delay,
            }
        );

        await Task.Delay(Delay + TimeSpan.FromSeconds(2));
        await client.StopAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, Volatile.Read(ref assignments));
    }

    [Fact]
    public async Task TheCoordinatorsOwnShare_StartsOnATickAfterTheDelay_AndItsRecordStaysWaitingUntilThen()
    {
        var (clients, store, coordinator, actions, events, self) =
            await SqlServerCoordinatorLeaseTests.SingleCoordinatorAsync();

        await coordinator.ExecuteCoordinatorRoleAsync(self, DelayedEvent(), actions, CancellationToken.None);
        await coordinator.ExecuteCoordinatorRoleAsync(self, DelayedEvent(), actions, CancellationToken.None);

        Assert.Equal(["stop",], events);
        Assert.Equal(ClientStatus.Waiting, clients.Get(self).ClientStatus);
        Assert.Equal(CoordinatorStatus.ResourcesGranted, clients.Get(self).CoordinatorStatus);
        Assert.Equal(AssignmentStatus.AssignmentInProgress, store.GetResources().AssignmentStatus);

        await Task.Delay(Delay);
        await coordinator.ExecuteCoordinatorRoleAsync(self, DelayedEvent(), actions, CancellationToken.None);

        Assert.Equal(["stop", "start:r1,r2",], events);
        Assert.Equal(ClientStatus.Active, clients.Get(self).ClientStatus);
        Assert.Equal(AssignmentStatus.ResourcesAssigned, store.GetResources().AssignmentStatus);
    }

    [Fact]
    public async Task AStepDownDuringTheDelay_CancelsTheCoordinatorsStart()
    {
        var (clients, _, coordinator, actions, events, self) =
            await SqlServerCoordinatorLeaseTests.SingleCoordinatorAsync();
        await coordinator.ExecuteCoordinatorRoleAsync(self, DelayedEvent(), actions, CancellationToken.None);

        clients.Lease = (Guid.NewGuid(), 2); // another node takes the lease during the delay
        await coordinator.ExecuteCoordinatorRoleAsync(self, DelayedEvent(), actions, CancellationToken.None);
        clients.Lease = (self, 1); // a tick of the old tenure must not start the old grant either
        await Task.Delay(Delay);
        await coordinator.ExecuteCoordinatorRoleAsync(self, DelayedEvent(), actions, CancellationToken.None);

        Assert.DoesNotContain(events, x => x.StartsWith("start"));
    }

    private static ClientEvent DelayedEvent()
    {
        var clientEvent = SqlServerCoordinatorLeaseTests.Event(1);
        clientEvent.OnAssignmentDelay = Delay;
        return clientEvent;
    }
}
