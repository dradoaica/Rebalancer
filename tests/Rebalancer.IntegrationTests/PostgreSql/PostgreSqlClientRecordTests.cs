using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.PostgreSql.Clients;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. ADR-0014 against a real PostgreSQL: stale records expire only under the coordinator's lease.
/// </summary>
public sealed class PostgreSqlClientRecordTests
{
    [SkippableFact]
    public async Task ExpiredRecordsAreDeleted_OnlyWhileTheCoordinatorHoldsTheLease()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var cs = pg.GetConnectionString();
        const string group = "orders";
        await PostgreSqlSchema.SeedResourceGroupAsync(cs, group, "r1");
        var coordinator = Guid.NewGuid();
        var live = Guid.NewGuid();
        var stale = Guid.NewGuid();
        await PostgreSqlSchema.ExecAsync(
            cs,
            "UPDATE rbr.resource_groups SET coordinator_id = @c, fencing_token = 3 WHERE resource_group = @g",
            ("c", coordinator),
            ("g", group)
        );
        foreach (var (id, minutesAgo) in new[]
                 {
                     (coordinator, 120), (live, 1), (stale, 60),
                 })
        {
            await PostgreSqlSchema.ExecAsync(
                cs,
                """
                INSERT INTO rbr.clients
                       (client_id, resource_group, last_keep_alive, client_status, coordinator_status, resources, fencing_token)
                VALUES (@id, @g, clock_timestamp() - make_interval(mins => @m), 1, 1, '', 1)
                """,
                ("id", id),
                ("g", group),
                ("m", minutesAgo)
            );
        }

        var service = new ClientService(cs);
        Assert.Equal(
            0,
            await service.DeleteExpiredClientsAsync(group, coordinator, 2, TimeSpan.FromMinutes(10))
        ); // stale token
        Assert.Equal(1, await service.DeleteExpiredClientsAsync(group, coordinator, 3, TimeSpan.FromMinutes(10)));

        var remaining = await PostgreSqlSchema.QueryClientIdsAsync(cs, group);
        Assert.Equal(
            new[]
            {
                coordinator, live,
            }.OrderBy(x => x),
            remaining.OrderBy(x => x)
        ); // own record is never deleted
        await service.DeleteClientAsync(live);
        Assert.Equal(
            new[]
            {
                coordinator,
            },
            await PostgreSqlSchema.QueryClientIdsAsync(cs, group)
        );
    }

    // ADR-0026 against a real PostgreSQL: coordinator writes change nothing unless the lease names that coordinator
    // with that token; PRB-0005: a reset record looks like a new registration and keeps its token
    [SkippableFact]
    public async Task CoordinatorWritesNeedTheLease_AndAResetKeepsTheFencingToken()
    {
        await using var pg = await PostgreSqlSchema.StartAsync();
        var cs = pg.GetConnectionString();
        const string group = "orders";
        await PostgreSqlSchema.SeedResourceGroupAsync(cs, group, "r1");
        var coordinator = Guid.NewGuid();
        await PostgreSqlSchema.ExecAsync(
            cs,
            "UPDATE rbr.resource_groups SET coordinator_id = @c, fencing_token = 3 WHERE resource_group = @g",
            ("c", coordinator),
            ("g", group)
        );
        var service = new ClientService(cs);
        IFencedClientService fenced = service;
        var follower = Guid.NewGuid();
        await service.CreateClientAsync(group, follower);
        var targets = (await service.GetActiveClientsAsync(group)).Where(x => x.ClientId == follower).ToList();
        List<ClientStartRequest> grant =
        [
            new()
            {
                ClientId = follower,
                AssignedResources = ["r1",],
            },
        ];

        Assert.True(await fenced.HoldsLeaseAsync(group, coordinator, 3));
        Assert.False(await fenced.HoldsLeaseAsync(group, coordinator, 2));
        Assert.False(await fenced.HoldsLeaseAsync(group, Guid.NewGuid(), 3));

        Assert.Equal(
            ModifyClientResult.FencingTokenViolation,
            await fenced.StartActivityAsync(group, coordinator, 2, grant)
        );
        Assert.Equal(CoordinatorStatus.StopActivity, (await service.KeepAliveAsync(follower)).CoordinatorStatus);
        Assert.Equal(ModifyClientResult.Ok, await fenced.StartActivityAsync(group, coordinator, 3, grant));
        var granted = await service.KeepAliveAsync(follower);
        Assert.Equal(CoordinatorStatus.ResourcesGranted, granted.CoordinatorStatus);
        Assert.Equal(["r1",], granted.AssignedResources);

        Assert.Equal(
            ModifyClientResult.FencingTokenViolation,
            await fenced.StopActivityAsync(group, Guid.NewGuid(), 3, targets)
        );
        Assert.Equal(CoordinatorStatus.ResourcesGranted, (await service.KeepAliveAsync(follower)).CoordinatorStatus);
        Assert.Equal(ModifyClientResult.Ok, await fenced.StopActivityAsync(group, coordinator, 3, targets));
        Assert.Equal(CoordinatorStatus.StopActivity, (await service.KeepAliveAsync(follower)).CoordinatorStatus);

        await fenced.StartActivityAsync(group, coordinator, 3, grant);
        await service.SetClientStatusAsync(follower, ClientStatus.Active);
        await fenced.ResetClientAsync(follower);
        var reset = (await service.GetActiveClientsAsync(group)).Single(x => x.ClientId == follower);
        Assert.Equal(ClientStatus.Waiting, reset.ClientStatus);
        Assert.Equal(CoordinatorStatus.StopActivity, reset.CoordinatorStatus);
        Assert.Equal(3, reset.FencingToken);
        Assert.Empty((await service.KeepAliveAsync(follower)).AssignedResources);
    }
}
