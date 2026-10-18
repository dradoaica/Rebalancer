using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.Zk;
using System.Reflection;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of the 2.2.0 deprecations: ADR-0011 (ZooKeeper entry points without credentials), ADR-0013 (static
///     provider registry), and ADR-0012 (a non-deprecated constructor for a caller-supplied ZooKeeper service). Every
///     deprecated member must have a non-deprecated replacement, and nothing else may be deprecated.
/// </summary>
public sealed class DeprecationContractTests
{
    private static readonly Type[] ServiceConstructor =
    [
        typeof(IZooKeeperService), typeof(string), typeof(TimeSpan), typeof(TimeSpan), typeof(TimeSpan),
        typeof(RebalancingMode), typeof(IRebalancerLogger),
    ];

    private static readonly Type[] HostsConstructor =
    [
        typeof(string), typeof(string), typeof(TimeSpan), typeof(TimeSpan), typeof(TimeSpan), typeof(RebalancingMode),
        typeof(IRebalancerLogger), typeof(IZooKeeperService),
    ];

    public static TheoryData<string> Deprecated => new()
    {
        "ZooKeeperService(string)",
        "ZooKeeperProvider(hosts, ..., IZooKeeperService)",
        "AddRebalancerZooKeeper(..., IZooKeeperService)",
        "Providers",
        "RebalancerClient()",
    };

    public static TheoryData<string> Replacements => new()
    {
        "ZooKeeperService(hosts, acl, authScheme, authData)",
        "ZooKeeperProvider(IZooKeeperService, ...)",
        "ZooKeeperProvider(hosts, ..., acl, authScheme, authData)",
        "AddRebalancerZooKeeper(..., acl, authScheme, authData, ...)",
        "RebalancerClient(IRebalancerProvider)",
    };

    [Theory]
    [MemberData(nameof(Deprecated))]
    public void DeprecatedMember_IsObsolete_AndNamesItsReplacement(string member)
    {
        var obsolete = Find(member).GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete.IsError); // a warning in 2.x; removal is the 3.0 step
        Assert.Contains("ADR-001", obsolete.Message);
    }

    [Theory]
    [MemberData(nameof(Replacements))]
    public void Replacement_IsNotObsolete(string member) =>
        Assert.Null(Find(member).GetCustomAttribute<ObsoleteAttribute>());

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
            new ZooKeeperService("localhost:1", null, null, null),
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

    private static MemberInfo Find(string member) =>
        member switch
        {
            "ZooKeeperService(string)" => (MemberInfo?)typeof(ZooKeeperService).GetConstructor([typeof(string)]),
            "ZooKeeperService(hosts, acl, authScheme, authData)" => typeof(ZooKeeperService).GetConstructor(
                [typeof(string), typeof(List<org.apache.zookeeper.data.ACL>), typeof(string), typeof(byte[])]
            ),
            "ZooKeeperProvider(hosts, ..., IZooKeeperService)" => typeof(ZooKeeperProvider).GetConstructor(HostsConstructor),
            "ZooKeeperProvider(IZooKeeperService, ...)" => typeof(ZooKeeperProvider).GetConstructor(ServiceConstructor),
            "ZooKeeperProvider(hosts, ..., acl, authScheme, authData)" => typeof(ZooKeeperProvider).GetConstructor(
                [
                    typeof(string), typeof(string), typeof(TimeSpan), typeof(TimeSpan), typeof(TimeSpan),
                    typeof(RebalancingMode), typeof(IRebalancerLogger), typeof(List<org.apache.zookeeper.data.ACL>),
                    typeof(string), typeof(byte[]),
                ]
            ),
            "AddRebalancerZooKeeper(..., IZooKeeperService)" => AddRebalancerZooKeeper(typeof(IZooKeeperService)),
            "AddRebalancerZooKeeper(..., acl, authScheme, authData, ...)" =>
                AddRebalancerZooKeeper(typeof(List<org.apache.zookeeper.data.ACL>)),
#pragma warning disable CS0618 // the deprecated type itself is the subject of this check (ADR-0013)
            "Providers" => typeof(Providers),
#pragma warning restore CS0618
            "RebalancerClient()" => typeof(RebalancerClient).GetConstructor(Type.EmptyTypes),
            "RebalancerClient(IRebalancerProvider)" => typeof(RebalancerClient).GetConstructor([typeof(IRebalancerProvider)]),
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        } ?? throw new InvalidOperationException($"{member} does not exist");

    // the two public overloads differ by whether they take a service or an ACL list
    private static MethodInfo? AddRebalancerZooKeeper(Type distinguishingParameter) =>
        typeof(ZooKeeperRebalancerServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(x => x.Name == nameof(ZooKeeperRebalancerServiceCollectionExtensions.AddRebalancerZooKeeper) &&
                                  x.GetParameters().Any(p => p.ParameterType == distinguishingParameter)
            );
}
