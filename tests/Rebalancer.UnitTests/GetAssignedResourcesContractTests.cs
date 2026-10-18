using Rebalancer.Core;
using Rebalancer.Redis;
using Rebalancer.SqlServer;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract (ADR 0007): while no assignment exists, GetAssignedResources() returns at once with PendingAssignment
///     and no resources, for every backend, instead of blocking until an assignment arrives.
/// </summary>
public sealed class GetAssignedResourcesContractTests
{
    public static TheoryData<string> Backends => new()
    {
        "SqlServer",
        "Redis",
    };

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task WhilePending_ReturnsImmediately_WithNoResources(string backend)
    {
        var provider = CreateUnconnectedProvider(backend);
        try
        {
            // run on a worker thread so a blocking implementation fails the test instead of hanging it
            var call = Task.Run(provider.GetAssignedResources);
            var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(call, finished);
            var assigned = await call;
            Assert.Equal(ClientState.PendingAssignment, assigned.ClientState);
            Assert.Empty(assigned.Resources);
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    // neither constructor needs a reachable server: SQL Server connects lazily, Redis is told not to abort
    private static IRebalancerProvider CreateUnconnectedProvider(string backend) =>
        backend == "Redis" ? new RedisProvider("localhost:1,abortConnect=false,connectTimeout=100")
            : new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;");
}
