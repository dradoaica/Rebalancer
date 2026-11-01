using Rebalancer.Core;
using Rebalancer.PostgreSql;
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
        "PostgreSql",
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

    // US-0026 criterion 2: the lease-based providers expose the same public members, with the same signatures
    [Fact]
    public void TheLeaseBasedProviders_HaveTheSamePublicMembers()
    {
        static string[] Members(Type type) =>
            type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.DeclaredOnly)
                .Select(m => m.ToString()!.Replace(type.Name, "Provider"))
                .Where(m => m != "Void Dispose()")
                .OrderBy(m => m)
                .ToArray();

        Assert.Equal(Members(typeof(SqlServerProvider)), Members(typeof(PostgreSqlProvider)));
        Assert.Equal(Members(typeof(SqlServerProvider)), Members(typeof(RedisProvider)));
    }

    // no constructor needs a reachable server: SQL Server and PostgreSQL connect lazily, Redis is told not to abort
    private static IRebalancerProvider CreateUnconnectedProvider(string backend) =>
        backend switch
        {
            "Redis" => new RedisProvider("localhost:1,abortConnect=false,connectTimeout=100"),
            "PostgreSql" => new PostgreSqlProvider("Host=127.0.0.1;Port=1;Timeout=1"),
            _ => new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;"),
        };
}
