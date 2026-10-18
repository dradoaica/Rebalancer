using Microsoft.Extensions.DependencyInjection;
using org.apache.zookeeper;
using org.apache.zookeeper.data;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.Zk;
using System.Text;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>Contract of ADR 0006 option B: ACLs are opt-in through new overloads; existing signatures are unchanged.</summary>
public sealed class ZooKeeperAclApiTests
{
    private const string Hosts = "localhost:1";

    [Fact]
    public void PreAclConstructors_StillExist_ForBinaryCompatibility()
    {
        Assert.NotNull(typeof(ZooKeeperService).GetConstructor([typeof(string),]));
        Assert.NotNull(
            typeof(ZooKeeperProvider).GetConstructor(
                [
                    typeof(string), typeof(string), typeof(TimeSpan), typeof(TimeSpan), typeof(TimeSpan),
                    typeof(RebalancingMode), typeof(IRebalancerLogger), typeof(IZooKeeperService),
                ]
            )
        );
        Assert.NotNull(
            typeof(ZooKeeperRebalancerServiceCollectionExtensions).GetMethod(
                nameof(ZooKeeperRebalancerServiceCollectionExtensions.AddRebalancerZooKeeper),
                [
                    typeof(IServiceCollection), typeof(string), typeof(string), typeof(RebalancingMode),
                    typeof(TimeSpan?), typeof(TimeSpan?), typeof(TimeSpan?), typeof(IRebalancerLogger),
                    typeof(IZooKeeperService),
                ]
            )
        );
    }

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
    public void AddRebalancerZooKeeper_WithoutAcl_StillResolves()
    {
        var services = new ServiceCollection();
        services.AddRebalancerZooKeeper(Hosts, "/rebalancer", RebalancingMode.GlobalBarrier);
        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetRequiredService<RebalancerClient>());
    }
}
