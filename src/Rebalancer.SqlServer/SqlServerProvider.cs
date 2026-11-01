using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.LeaseCoordination.Resources;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Leases;
using Rebalancer.SqlServer.Resources;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.SqlServer;

/// <summary>
///     SQL Server backend. The coordination itself (leases, coordinator and follower roles) is shared with the
///     PostgreSQL and Redis backends in Rebalancer.LeaseCoordination (ADR-0029).
/// </summary>
public class SqlServerProvider : IRebalancerProvider
{
    private readonly LeaseCoordinatedProvider provider;

    public SqlServerProvider(
        string connectionString,
        IRebalancerLogger logger = null,
        ILeaseService leaseService = null,
        IResourceService resourceService = null,
        IClientService clientService = null
    )
    {
        logger ??= new NullRebalancerLogger();
        provider = new LeaseCoordinatedProvider(
            "sqlserver",
            logger,
            leaseService ?? new LeaseService(connectionString, logger),
            resourceService ?? new ResourceService(connectionString),
            clientService ?? new ClientService(connectionString)
        );
    }

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
