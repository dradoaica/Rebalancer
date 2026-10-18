using Microsoft.Extensions.DependencyInjection;
using org.apache.zookeeper.data;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.Zk;
using System;
using System.Collections.Generic;

namespace Rebalancer.Extensions.DependencyInjection;

public static class ZooKeeperRebalancerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a transient <see cref="ZooKeeperProvider" /> as <see cref="IRebalancerProvider" /> and
    ///     <see cref="RebalancerClient" />.
    /// </summary>
    /// <param name="zooKeeperService">
    ///     A single instance used by <b>every</b> provider this registration resolves. A service
    ///     holds one node's session, so only pass one when the application resolves a single client; otherwise register a
    ///     factory with <see cref="ServiceCollectionExtensions.AddRebalancerProvider" />, e.g.
    ///     <c>AddRebalancerProvider(_ =&gt; new ZooKeeperProvider(..., new MyZooKeeperService()))</c>, or use the ACL
    ///     overload, which creates a service per provider.
    /// </param>
    [Obsolete(
        "Deprecated by ADR-0011 and removed in 3.0. Use the overload with acl/authScheme/authData (pass "
        + "ZooDefs.Ids.OPEN_ACL_UNSAFE to keep an open ACL deliberately), or AddRebalancerProvider with "
        + "ZooKeeperProvider(IZooKeeperService, ...) for a caller-supplied service (ADR-0012)."
    )]
    public static IServiceCollection AddRebalancerZooKeeper(
        this IServiceCollection services,
        string zooKeeperHosts,
        string zooKeeperRootPath,
        RebalancingMode rebalancingMode,
        TimeSpan? sessionTimeout = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? minimumRebalancingInterval = null,
        IRebalancerLogger? logger = null,
        IZooKeeperService? zooKeeperService = null
    ) => Register(
        services,
        zooKeeperHosts,
        zooKeeperRootPath,
        rebalancingMode,
        sessionTimeout,
        connectTimeout,
        minimumRebalancingInterval,
        logger,
        () => zooKeeperService ?? new ZooKeeperService(zooKeeperHosts)
    );

    /// <summary>
    ///     Like the main overload, but every resolved <see cref="ZooKeeperProvider" /> secures the znodes it creates (ADR
    ///     0006). Each provider gets its own <see cref="ZooKeeperService" />.
    /// </summary>
    /// <param name="acl">ACL for every znode created; see <see cref="ZooKeeperService" /> for the defaults when null.</param>
    /// <param name="authScheme">Auth scheme added to every session, e.g. "digest", or null for none.</param>
    /// <param name="authData">Auth data for <paramref name="authScheme" />, e.g. "user:password" as UTF-8.</param>
    public static IServiceCollection AddRebalancerZooKeeper(
        this IServiceCollection services,
        string zooKeeperHosts,
        string zooKeeperRootPath,
        RebalancingMode rebalancingMode,
        List<ACL>? acl,
        string? authScheme,
        byte[]? authData,
        TimeSpan? sessionTimeout = null,
        TimeSpan? connectTimeout = null,
        TimeSpan? minimumRebalancingInterval = null,
        IRebalancerLogger? logger = null
    ) => Register(
        services,
        zooKeeperHosts,
        zooKeeperRootPath,
        rebalancingMode,
        sessionTimeout,
        connectTimeout,
        minimumRebalancingInterval,
        logger,
        () => new ZooKeeperService(zooKeeperHosts, acl, authScheme, authData)
    );

    private static IServiceCollection Register(
        IServiceCollection services,
        string zooKeeperHosts,
        string zooKeeperRootPath,
        RebalancingMode rebalancingMode,
        TimeSpan? sessionTimeout,
        TimeSpan? connectTimeout,
        TimeSpan? minimumRebalancingInterval,
        IRebalancerLogger? logger,
        Func<IZooKeeperService> zooKeeperServiceFactory
    )
    {
        if (string.IsNullOrWhiteSpace(zooKeeperHosts))
        {
            throw new ArgumentException("ZooKeeper hosts are required.", nameof(zooKeeperHosts));
        }

        if (string.IsNullOrWhiteSpace(zooKeeperRootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(zooKeeperRootPath));
        }

        var log = logger ?? new NullRebalancerLogger();
        var session = sessionTimeout ?? TimeSpan.FromSeconds(15);
        var connect = connectTimeout ?? TimeSpan.FromSeconds(10);
        var minRebalance = minimumRebalancingInterval ?? TimeSpan.FromSeconds(1);

        services.AddTransient<IRebalancerProvider>(_ => new ZooKeeperProvider(
                zooKeeperServiceFactory(),
                zooKeeperRootPath,
                session,
                connect,
                minRebalance,
                rebalancingMode,
                log
            )
        );
        services.AddTransient<RebalancerClient>();
        return services;
    }
}
