using Microsoft.Extensions.DependencyInjection;
using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.SqlServer;
using Rebalancer.ZooKeeper;
using Xunit;

namespace Rebalancer.UnitTests;

public sealed class DiRegistrationTests
{
    // US-0015 AC3: every registration gives each client its own provider (providers hold per-node state, ADR-0016)
    public static TheoryData<string> Registrations => new()
    {
        "SqlServer",
        "Redis",
        "ZooKeeper",
        "Factory",
    };

    [Fact]
    public void AddRebalancerSqlServer_Resolves_RebalancerClient_Without_Static_Providers()
    {
        var services = new ServiceCollection();
        services.AddRebalancerSqlServer("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;");
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<RebalancerClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddRebalancerProvider_Factory_Resolves_RebalancerClient_Without_Static_Providers()
    {
        var services = new ServiceCollection();
        services.AddRebalancerProvider(_ =>
            new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;")
        );
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<RebalancerClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void RebalancerClient_Constructor_With_Injected_Provider_Does_Not_Use_Static_Providers()
    {
        var backend = new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;");
        using var client = new RebalancerClient(backend);
        Assert.NotNull(client);
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void EachResolvedClient_GetsItsOwnProvider(string registration)
    {
        var services = new ServiceCollection();
        _ = registration switch
        {
            "SqlServer" => services.AddRebalancerSqlServer(
                "Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;"
            ),
            "Redis" => services.AddRebalancerRedis("localhost:1,abortConnect=false,connectTimeout=100"),
            "ZooKeeper" => services.AddRebalancerZooKeeper(
                "localhost:1",
                "/rebalancer",
                RebalancingMode.GlobalBarrier,
                ZooDefs.Ids.OPEN_ACL_UNSAFE,
                null,
                null
            ),
            _ => services.AddRebalancerProvider(_ =>
                new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;")
            ),
        };
        using var serviceProvider = services.BuildServiceProvider();

        var first = serviceProvider.GetRequiredService<IRebalancerProvider>();
        var second = serviceProvider.GetRequiredService<IRebalancerProvider>();
        Assert.NotSame(first, second);
        Assert.NotSame(
            serviceProvider.GetRequiredService<RebalancerClient>(),
            serviceProvider.GetRequiredService<RebalancerClient>()
        );
    }
}
