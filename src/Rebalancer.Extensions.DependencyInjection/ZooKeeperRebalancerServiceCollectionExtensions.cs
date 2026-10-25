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
    ///     <see cref="RebalancerClient" />. Each provider gets its own <see cref="ZooKeeperService" />. Credentials or an
    ///     explicit ACL are required (ADR-0011); for a caller-supplied service use <c>AddRebalancerProvider</c> with
    ///     <c>ZooKeeperProvider(IZooKeeperService, ...)</c> (ADR-0012).
    /// </summary>
    /// <exception cref="ArgumentException">Neither <paramref name="acl" /> nor <paramref name="authScheme" /> is given.</exception>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="zooKeeperHosts">The ZooKeeper connection string.</param>
    /// <param name="zooKeeperRootPath">The znode under which the resource groups are kept.</param>
    /// <param name="rebalancingMode">How resources change hands during a rebalance.</param>
    /// <param name="acl">ACL for every znode created; see <see cref="ZooKeeperService" /> for the defaults when null.</param>
    /// <param name="authScheme">Auth scheme added to every session, e.g. "digest", or null for none.</param>
    /// <param name="authData">Auth data for <paramref name="authScheme" />, e.g. "user:password" as UTF-8.</param>
    /// <param name="sessionTimeout">The ZooKeeper session timeout; default 15 seconds.</param>
    /// <param name="connectTimeout">How long to wait for a connection; default 10 seconds.</param>
    /// <param name="minimumRebalancingInterval">The minimum time between two rebalances; default 1 second.</param>
    /// <param name="logger">The logger, or null for none.</param>
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
    )
    {
        // fail at registration, not at the first resolution
        if (acl == null && authScheme == null)
        {
            throw new ArgumentException(
                "Either credentials (authScheme and authData) or an explicit ACL are required; pass " +
                "ZooDefs.Ids.OPEN_ACL_UNSAFE without credentials to keep znodes open deliberately (ADR-0011).",
                nameof(acl)
            );
        }

        return Register(
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
    }

    // internal for the unit tests, which pass a fake service to check the defaults (US-0007 criterion 3)
    internal static IServiceCollection Register(
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
