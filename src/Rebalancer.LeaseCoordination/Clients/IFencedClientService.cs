using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rebalancer.LeaseCoordination.Clients;

/// <summary>
///     Operations the built-in client services add for the coordinator (ADR-0026) and the restart path (PRB-0005).
///     Internal, so <see cref="IClientService" /> stays unchanged: a custom implementation keeps the behavior from before
///     ADR-0026, without the lease guard.
/// </summary>
internal interface IFencedClientService
{
    /// <summary>
    ///     ADR-0026: true while the lease of <paramref name="resourceGroup" /> names
    ///     <paramref name="coordinatorClientId" /> with <paramref name="fencingToken" /> (the guard of ADR-0014). Expiry is
    ///     not checked: another node must write the lease before it can act.
    /// </summary>
    Task<bool> HoldsLeaseAsync(string resourceGroup, Guid coordinatorClientId, int fencingToken);

    /// <summary><see cref="IClientService.StopActivityAsync" />, changing nothing unless the lease guard holds.</summary>
    Task<ModifyClientResult> StopActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<Client> clients
    );

    /// <summary><see cref="IClientService.StartActivityAsync" />, changing nothing unless the lease guard holds.</summary>
    Task<ModifyClientResult> StartActivityAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        List<ClientStartRequest> clientStartRequests
    );

    /// <summary>
    ///     PRB-0005: writes the record as a new registration would (Waiting, StopActivity, no resources), keeping its
    ///     fencing token, so a client that restarts after an error is included again.
    /// </summary>
    Task ResetClientAsync(Guid clientId);
}
