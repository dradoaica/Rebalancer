using Rebalancer.Core;
using System;

namespace Rebalancer.LeaseCoordination.Clients;

/// <summary>
///     The client's record does not exist, e.g. because it expired (ADR-0014). A custom
///     <see cref="IClientService" /> throws it from <see cref="IClientService.KeepAliveAsync" />; the client then
///     registers again (ADR-0034).
/// </summary>
public sealed class ClientNotFoundException : RebalancerException
{
    public ClientNotFoundException(Guid clientId)
        : base($"No client exists with id {clientId}")
    {
    }
}
