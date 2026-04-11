using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.Zk;
using System;

namespace Rebalancer.Extensions.DependencyInjection;

public static class ZooKeeperRebalancerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a transient <see cref="ZooKeeperProvider" /> as <see cref="IRebalancerProvider" /> and
    ///     <see cref="RebalancerClient" />.
    /// </summary>
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
                zooKeeperHosts,
                zooKeeperRootPath,
                session,
                connect,
                minRebalance,
                rebalancingMode,
                log,
                zooKeeperService
            )
        );
        services.AddTransient<RebalancerClient>();
        return services;
    }
}
