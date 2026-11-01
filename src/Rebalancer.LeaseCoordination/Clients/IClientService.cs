using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rebalancer.LeaseCoordination.Clients;

/// <summary>
///     The client records of the lease-based backends (ADR-0029). Every time an implementation stores or returns
///     comes from the store's clock (<see cref="Client.LastKeepAlive" />, <see cref="Client.TimeNow" />), so nodes with
///     different clocks agree on who is alive. An implementation outside this library cannot signal a missing record in a
///     way the client recovers from; the built-in services re-register such a client (ADR-0014).
/// </summary>
public interface IClientService
{
    /// <summary>Registers a client of <paramref name="resourceGroup" /> as Waiting, with no resources.</summary>
    Task CreateClientAsync(string resourceGroup, Guid clientId);

    /// <summary>
    ///     The Waiting and Active clients of the group, each with the store's current time in
    ///     <see
    ///         cref="Client.TimeNow" />
    ///     .
    /// </summary>
    Task<List<Client>> GetActiveClientsAsync(string resourceGroup);

    /// <summary>
    ///     Stamps the client's keep-alive with the store's clock and returns its record, including its granted resources
    ///     and the fencing token of that grant.
    /// </summary>
    Task<Client> KeepAliveAsync(Guid clientId);

    /// <summary>Sets the client's own status.</summary>
    Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus);

    /// <summary>
    ///     Tells the clients to stop, only where their record's fencing token is not higher than
    ///     <paramref
    ///         name="fencingToken" />
    ///     ; <see cref="ModifyClientResult.FencingTokenViolation" /> if any is.
    /// </summary>
    Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients);

    /// <summary>
    ///     Grants each client its resources with <paramref name="fencingToken" />, all or none, only where the record's
    ///     token is not higher; <see cref="ModifyClientResult.FencingTokenViolation" /> otherwise.
    /// </summary>
    Task<ModifyClientResult> StartActivityAsync(int fencingToken, List<ClientStartRequest> clientStartRequests);

    /// <summary>
    ///     ADR-0014: deletes the records of <paramref name="resourceGroup" /> whose last keep-alive is older than
    ///     <paramref name="retention" /> by the store's clock, only while <paramref name="coordinatorClientId" /> holds the
    ///     lease with <paramref name="fencingToken" />, and never the coordinator's own record. Returns the number deleted.
    ///     The default implementation deletes nothing, so custom implementations keep their 2.x behavior.
    /// </summary>
    Task<int> DeleteExpiredClientsAsync(
        string resourceGroup,
        Guid coordinatorClientId,
        int fencingToken,
        TimeSpan retention
    ) => Task.FromResult(0);

    /// <summary>
    ///     ADR-0014: removes this client's own record on a clean stop. The default implementation marks it
    ///     <see
    ///         cref="ClientStatus.Terminated" />
    ///     , as in 2.x.
    /// </summary>
    Task DeleteClientAsync(Guid clientId) => SetClientStatusAsync(clientId, ClientStatus.Terminated);
}
