using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.LeaseCoordination.Resources;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Leases;
using Rebalancer.Redis.Resources;
using Rebalancer.Redis.Utils;
using StackExchange.Redis;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.Redis;

/// <summary>
///     Redis backend. Each instance opens its own Redis connection and closes it on <see cref="Dispose" />;
///     <see cref="RebalancerClient" /> disposes the provider it was given, so use one provider instance per client (ADR
///     0006). The coordination itself (leases, coordinator and follower roles) is shared with the SQL Server backend in
///     Rebalancer.LeaseCoordination (ADR-0029).
/// </summary>
public class RedisProvider : IRebalancerProvider, IDisposable
{
    private readonly ConnectionMultiplexer connection;
    private readonly LeaseCoordinatedProvider provider;

    public RedisProvider(
        string connectionString,
        IRebalancerLogger logger = null,
        ILeaseService leaseService = null,
        IResourceService resourceService = null,
        IClientService clientService = null
    )
    {
        connection = ConnectionMultiplexer.Connect(connectionString);
        var cache = connection.GetDatabase();
        logger ??= new NullRebalancerLogger();
        provider = new LeaseCoordinatedProvider(
            "redis",
            logger,
            leaseService ?? new LeaseService(cache),
            resourceService ?? new ResourceService(cache),
            clientService ?? new ClientService(cache),
            (resourceGroup, clientId) =>
                PerGroupKeyMigration.MoveToPerGroupKeysAsync(cache, resourceGroup, logger, clientId)
        );
    }

    /// <summary>Closes the Redis connection owned by this provider.</summary>
    public void Dispose() => connection.Dispose();

    public Task StartAsync(
        string resourceGroup,
        OnChangeActions onChangeActions,
        CancellationToken parentToken,
        ClientOptions clientOptions
    ) => provider.StartAsync(resourceGroup, onChangeActions, parentToken, clientOptions);

    public Task RecreateClientAsync() => provider.RecreateClientAsync();

    public Task WaitForCompletionAsync() => provider.WaitForCompletionAsync();

    /// <summary>
    ///     Returns at once (ADR 0007): while assignment is pending the state is PendingAssignment and no resources are
    ///     returned. To wait for an assignment use <see cref="GetAssignedResources(CancellationToken)" />.
    /// </summary>
    public AssignedResources GetAssignedResources() => provider.GetAssignedResources();

    public ClientState GetState() => provider.GetState();

    public AssignedResources GetAssignedResources(CancellationToken token) => provider.GetAssignedResources(token);
}
