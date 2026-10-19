using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rebalancer.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.Extensions.Diagnostics.HealthChecks;

/// <summary>
///     Reports whether a <see cref="RebalancerClient" /> takes part in its resource group (ADR-0023). Pass the host's own,
///     started client: the dependency injection extensions register clients as transient, so the check cannot resolve it.
///     The check reads state only and never calls the backend.
/// </summary>
/// <remarks>
///     <c>Unhealthy</c> after <see cref="RebalancerClient.OnAborted" /> until the client is assigned again (SQL Server and
///     Redis raise it also for errors they recover from), and in the states Aborted, Stopped, NotStarted and NoProvider. <c>Degraded</c>
///     while PendingAssignment lasts longer than the threshold. <c>Healthy</c> otherwise.
/// </remarks>
public sealed class RebalancerClientHealthCheck : IHealthCheck
{
    /// <summary>Default time a client may wait for an assignment before it is reported Degraded.</summary>
    public static readonly TimeSpan DefaultPendingThreshold = TimeSpan.FromMinutes(2);

    private readonly RebalancerClient client;
    private readonly TimeSpan pendingThreshold;
    private readonly Func<DateTime> utcNow;
    private int aborted;
    private long pendingSinceTicks; // 0 while not known to be pending

    public RebalancerClientHealthCheck(RebalancerClient client, TimeSpan? pendingThreshold = null)
        : this(client, pendingThreshold ?? DefaultPendingThreshold, () => DateTime.UtcNow)
    {
    }

    internal RebalancerClientHealthCheck(RebalancerClient client, TimeSpan pendingThreshold, Func<DateTime> utcNow)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.pendingThreshold = pendingThreshold;
        this.utcNow = utcNow;
        client.OnAborted += (_, _) => Interlocked.Exchange(ref aborted, 1);
        client.OnUnassignment += (_, _) => Interlocked.CompareExchange(ref pendingSinceTicks, utcNow().Ticks, 0);
        client.OnAssignment += (_, _) =>
        {
            Interlocked.Exchange(ref pendingSinceTicks, 0);
            // SQL Server and Redis raise OnAborted also for errors they recover from; a new assignment means the client
            // takes part again, so the abort no longer applies (a final abort reports ClientState.Aborted instead)
            Interlocked.Exchange(ref aborted, 0);
        };
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref aborted) == 1)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Rebalancer client aborted"));
        }

        var state = client.GetCurrentState();
        switch (state)
        {
            case ClientState.Aborted:
            case ClientState.Stopped:
            case ClientState.NotStarted:
            case ClientState.NoProvider:
                return Task.FromResult(HealthCheckResult.Unhealthy($"Rebalancer client state: {state}"));
            case ClientState.PendingAssignment:
                // waiting since the last unassignment, or since this check first saw the client waiting
                Interlocked.CompareExchange(ref pendingSinceTicks, utcNow().Ticks, 0);
                var waiting = utcNow() - new DateTime(Interlocked.Read(ref pendingSinceTicks), DateTimeKind.Utc);
                return Task.FromResult(
                    waiting > pendingThreshold
                        ? HealthCheckResult.Degraded($"Rebalancer client waiting for an assignment for {waiting.TotalSeconds:F0} s")
                        : HealthCheckResult.Healthy($"Rebalancer client state: {state}")
                );
            default:
                Interlocked.Exchange(ref pendingSinceTicks, 0);
                return Task.FromResult(HealthCheckResult.Healthy($"Rebalancer client state: {state}"));
        }
    }
}
