using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rebalancer.Core;
using Rebalancer.Extensions.Diagnostics.HealthChecks;
using System;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers <see cref="RebalancerClientHealthCheck" /> (ADR-0023).</summary>
public static class RebalancerHealthChecksBuilderExtensions
{
    /// <summary>Adds a health check for the host's own <paramref name="client" />.</summary>
    /// <param name="pendingThreshold">How long PendingAssignment may last before Degraded; default 2 minutes.</param>
    public static IHealthChecksBuilder AddRebalancerClient(
        this IHealthChecksBuilder builder,
        string name,
        RebalancerClient client,
        TimeSpan? pendingThreshold = null
    ) => builder.AddCheck(name, new RebalancerClientHealthCheck(client, pendingThreshold));
}
