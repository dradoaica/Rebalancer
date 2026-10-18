using Rebalancer.Core;
using System;

namespace Rebalancer.Redis.Clients;

/// <summary>The client's record does not exist, e.g. because it expired (ADR-0014).</summary>
internal sealed class ClientNotFoundException : RebalancerException
{
    public ClientNotFoundException(Guid clientId)
        : base($"No client exists with id {clientId}")
    {
    }
}
