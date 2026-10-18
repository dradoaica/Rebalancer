using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rebalancer.Redis.Clients;

public interface IClientService
{
    Task CreateClientAsync(string resourceGroup, Guid clientId);
    Task<List<Client>> GetActiveClientsAsync(string resourceGroup);
    Task<Client> KeepAliveAsync(Guid clientId);
    Task SetClientStatusAsync(Guid clientId, ClientStatus clientStatus);
    Task<ModifyClientResult> StopActivityAsync(int fencingToken, List<Client> clients);
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
    ///     <see cref="ClientStatus.Terminated" />, as in 2.x.
    /// </summary>
    Task DeleteClientAsync(Guid clientId) => SetClientStatusAsync(clientId, ClientStatus.Terminated);
}
