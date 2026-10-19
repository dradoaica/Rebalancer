using Microsoft.Extensions.DependencyInjection;
using org.apache.zookeeper;
using org.apache.zookeeper.data;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.ZooKeeper;
using System.Text;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of the ZooKeeper ACL API (ADR-0018 in 2.x, ADR-0011 from 3.0): ACL overloads work with credentials or
///     an explicit ACL.
/// </summary>
public sealed class ZooKeeperAclApiTests
{
    private const string Hosts = "localhost:1";

    [Fact]
    public void AclProviderConstructor_CanBeUsedWithRebalancerClient()
    {
        var provider = new ZooKeeperProvider(
            Hosts,
            "/rebalancer",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            RebalancingMode.GlobalBarrier,
            new NullRebalancerLogger(),
            ZooDefs.Ids.CREATOR_ALL_ACL,
            "digest",
            Encoding.UTF8.GetBytes("rebalancer:secret")
        );

        using var client = new RebalancerClient(provider);
        Assert.Equal(ClientState.NotStarted, client.GetCurrentState());
    }

    [Fact]
    public void AddRebalancerZooKeeper_WithAcl_ResolvesADistinctProviderPerClient()
    {
        var services = new ServiceCollection();
        services.AddRebalancerZooKeeper(
            Hosts,
            "/rebalancer",
            RebalancingMode.ResourceBarrier,
            new List<ACL>(ZooDefs.Ids.CREATOR_ALL_ACL),
            "digest",
            Encoding.UTF8.GetBytes("rebalancer:secret")
        );
        using var serviceProvider = services.BuildServiceProvider();

        // providers hold per-node session state, so two clients must never share one
        var first = serviceProvider.GetRequiredService<IRebalancerProvider>();
        var second = serviceProvider.GetRequiredService<IRebalancerProvider>();
        Assert.NotSame(first, second);
        Assert.NotNull(serviceProvider.GetRequiredService<RebalancerClient>());
    }

    [Fact]
    public void AddRebalancerZooKeeper_WithExplicitOpenAcl_Resolves()
    {
        // the non-deprecated replacement for an open ACL: the choice is visible in code (ADR-0011)
        var services = new ServiceCollection();
        services.AddRebalancerZooKeeper(
            Hosts,
            "/rebalancer",
            RebalancingMode.GlobalBarrier,
            ZooDefs.Ids.OPEN_ACL_UNSAFE,
            null,
            null
        );
        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetRequiredService<RebalancerClient>());
    }
}
