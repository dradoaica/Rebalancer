using Microsoft.Extensions.DependencyInjection;
using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.Zk;
using System.Reflection;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of 3.0: the members deprecated in 2.2 are gone (ADR-0011, ADR-0013); every remaining ZooKeeper entry
///     point requires credentials or an explicit OPEN_ACL_UNSAFE (ADR-0011); a caller-supplied ZooKeeper service has its
///     own constructor (ADR-0012).
/// </summary>
public sealed class DeprecationContractTests
{
    private const string Hosts = "localhost:1";

    [Fact]
    public void StaticProviderRegistry_IsRemoved()
    {
        Assert.Null(typeof(RebalancerClient).Assembly.GetType("Rebalancer.Core.Providers"));
        Assert.Null(typeof(RebalancerClient).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(RebalancerClient).GetConstructor([typeof(IRebalancerProvider),]));
    }

    [Fact]
    public void ZooKeeperEntryPointsWithoutCredentials_AreRemoved()
    {
        Assert.Null(typeof(ZooKeeperService).GetConstructor([typeof(string),]));
        Assert.DoesNotContain(
            typeof(ZooKeeperProvider).GetConstructors(),
            // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
            x => x.GetParameters().Length == 8 && x.GetParameters()[7].ParameterType == typeof(IZooKeeperService)
        );
        Assert.DoesNotContain(
            typeof(ZooKeeperRebalancerServiceCollectionExtensions).GetMethods(
                BindingFlags.Public | BindingFlags.Static
            ),
            // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
            x => x.GetParameters().Any(p => p.ParameterType == typeof(IZooKeeperService))
        );
    }

    [Fact]
    public void NoObsoleteMembersRemain()
    {
        var obsolete = new[]
            {
                typeof(RebalancerClient), typeof(ZooKeeperProvider), typeof(ZooKeeperService),
            }.SelectMany(t => t.GetMembers().Append(t))
            .Concat(typeof(ZooKeeperRebalancerServiceCollectionExtensions).GetMethods())
            .Where(x => x.GetCustomAttribute<ObsoleteAttribute>() != null)
            .Select(x => x.ToString());
        Assert.Empty(obsolete);
    }

    [Fact]
    public void ZooKeeperService_WithoutCredentialsOrAcl_Throws() =>
        Assert.Throws<ArgumentException>(() => new ZooKeeperService(Hosts, null, null, null));

    [Fact]
    public void ZooKeeperService_WithExplicitOpenAcl_OrWithCredentials_IsAccepted()
    {
        _ = new ZooKeeperService(Hosts, ZooDefs.Ids.OPEN_ACL_UNSAFE, null, null);
        _ = new ZooKeeperService(Hosts, null, "digest", "rebalancer:secret"u8.ToArray());
    }

    [Fact]
    public void ZooKeeperProvider_WithoutCredentialsOrAcl_Throws() =>
        Assert.Throws<ArgumentException>(() => new ZooKeeperProvider(
                Hosts,
                "/rebalancer",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(1),
                RebalancingMode.GlobalBarrier,
                new NullRebalancerLogger(),
                null,
                null,
                null
            )
        );

    [Fact]
    public void AddRebalancerZooKeeper_WithoutCredentialsOrAcl_FailsAtRegistration() =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRebalancerZooKeeper(
                Hosts,
                "/rebalancer",
                RebalancingMode.GlobalBarrier,
                null,
                null,
                null
            )
        );

    [Fact]
    public void ServiceConstructor_RejectsANullService() =>
        Assert.Throws<ArgumentNullException>(() => new ZooKeeperProvider(
                null!,
                "/rebalancer",
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(1),
                RebalancingMode.GlobalBarrier,
                new NullRebalancerLogger()
            )
        );

    [Fact]
    public void ServiceConstructor_CanBeUsedWithRebalancerClient()
    {
        var provider = new ZooKeeperProvider(
            new ZooKeeperService(Hosts, ZooDefs.Ids.OPEN_ACL_UNSAFE, null, null),
            "/rebalancer",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            RebalancingMode.ResourceBarrier,
            new NullRebalancerLogger()
        );

        using var client = new RebalancerClient(provider);
        Assert.Equal(ClientState.NotStarted, client.GetCurrentState());
    }
}
