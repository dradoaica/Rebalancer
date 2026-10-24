using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rebalancer.Core;
using Rebalancer.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     ADR-0023 / US-0018 with a real provider; the unit tests use fake providers, which hid PRB-0006. A client whose
///     assignment is delayed past the pending threshold is Degraded, then Healthy once assigned, and Unhealthy after
///     StopAsync. The group must hold <see cref="Resources" />.
/// </summary>
internal static class HealthCheckScenarios
{
    public static readonly string[] Resources = ["r1", "r2",];
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(60);

    public static async Task TheCheckFollowsARealClient(string group, Func<IRebalancerProvider> createProvider)
    {
        using var client = new RebalancerClient(createProvider());
        var check = new RebalancerClientHealthCheck(client, TimeSpan.FromSeconds(2));
        await client.StartAsync(
            group,
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromSeconds(1),
                OnAssignmentDelay = TimeSpan.FromSeconds(10), // keeps the client pending past the threshold
            }
        );
        try
        {
            await WaitForAsync(check, HealthStatus.Degraded, () => true, "the pending client was never Degraded");
            await WaitForAsync(
                check,
                HealthStatus.Healthy,
                () => client.GetCurrentState() == ClientState.Assigned,
                "the assigned client was never Healthy"
            );
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(30));
        }

        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    public static async Task WaitForAsync(
        RebalancerClientHealthCheck check,
        HealthStatus status,
        Func<bool> condition,
        string failure
    )
    {
        var deadline = DateTime.UtcNow + Settle;
        while ((await check.CheckHealthAsync(new HealthCheckContext())).Status != status || !condition())
        {
            Assert.True(DateTime.UtcNow < deadline, failure);
            await Task.Delay(200);
        }
    }
}
