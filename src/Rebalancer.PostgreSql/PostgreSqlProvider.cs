using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.LeaseCoordination.Resources;
using Rebalancer.PostgreSql.Clients;
using Rebalancer.PostgreSql.Leases;
using Rebalancer.PostgreSql.Resources;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.PostgreSql;

/// <summary>
///     PostgreSQL backend (ADR-0030): coordination state in the <c>rbr</c> schema (<c>db/postgresql/</c>). The
///     coordination itself (leases, coordinator and follower roles) is shared with the SQL Server and Redis backends in
///     Rebalancer.LeaseCoordination (ADR-0029).
/// </summary>
public class PostgreSqlProvider : IRebalancerProvider
{
    private readonly LeaseCoordinatedProvider provider;

    public PostgreSqlProvider(
        string connectionString,
        IRebalancerLogger logger = null,
        ILeaseService leaseService = null,
        IResourceService resourceService = null,
        IClientService clientService = null
    )
    {
        logger ??= new NullRebalancerLogger();
        provider = new LeaseCoordinatedProvider(
            "postgresql",
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
