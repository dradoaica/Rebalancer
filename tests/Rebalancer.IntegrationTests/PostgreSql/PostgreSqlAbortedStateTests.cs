using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rebalancer.Core;
using Rebalancer.Extensions.Diagnostics.HealthChecks;
using Rebalancer.PostgreSql;
using Xunit;

namespace Rebalancer.IntegrationTests.PostgreSql;

/// <summary>
///     Requires Docker. US-0021 for PostgreSQL against a real store: a client whose resource group does not exist
///     gets a final error. Without auto-recovery it gives up and reports Aborted (the health check Unhealthy); with
///     auto-recovery it keeps trying and is not Aborted. The unit tests (<c>AbortedStateTests</c>) use fake services.
/// </summary>
public sealed class PostgreSqlAbortedStateTests
{
    [SkippableFact]
    public async Task WithoutAutoRecovery_AClientOfAMissingGroupReportsAborted()
    {
        // the schema without any group
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        using var client = new RebalancerClient(new PostgreSqlProvider(connectionString));
        var check = new RebalancerClientHealthCheck(client);

        await client.StartAsync(
            "it-pg-missing-" + Guid.NewGuid().ToString("N"),
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (client.GetCurrentState() != ClientState.Aborted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.Equal(ClientState.Aborted, client.GetCurrentState());
        Assert.Equal(ClientState.Aborted, client.GetAssignedResources().ClientState);
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [SkippableFact]
    public async Task WithAutoRecovery_AClientOfAMissingGroupKeepsTryingAndIsNotAborted()
    {
        // the schema without any group
        await using var pg = await PostgreSqlSchema.StartAsync();
        var connectionString = pg.GetConnectionString();
        using var client = new RebalancerClient(new PostgreSqlProvider(connectionString));
        var aborts = 0;
        // ReSharper disable once AccessToModifiedClosure
        client.OnAborted += (_, _) => Interlocked.Increment(ref aborts);

        await client.StartAsync(
            "it-pg-missing-" + Guid.NewGuid().ToString("N"),
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromMilliseconds(200),
            }
        );
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref aborts) < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        Assert.True(Volatile.Read(ref aborts) >= 2, "the client did not keep trying");
        Assert.NotEqual(ClientState.Aborted, client.GetCurrentState());
        await client.StopAsync(TimeSpan.FromSeconds(10));
    }
}
