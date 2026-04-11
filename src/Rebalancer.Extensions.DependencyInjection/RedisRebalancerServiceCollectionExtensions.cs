using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.Redis;
using Rebalancer.Redis.Clients;
using Rebalancer.Redis.Leases;
using Rebalancer.Redis.Resources;
using System;

namespace Rebalancer.Extensions.DependencyInjection;

public static class RedisRebalancerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a transient <see cref="RedisProvider" /> as <see cref="IRebalancerProvider" /> and
    ///     <see cref="RebalancerClient" />.
    /// </summary>
    public static IServiceCollection AddRebalancerRedis(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddTransient<IRebalancerProvider>(_ => new RedisProvider(connectionString));
        services.AddTransient<RebalancerClient>();
        return services;
    }

    /// <summary>
    ///     Registers <see cref="RedisProvider" /> with optional custom services (same semantics as the provider
    ///     constructor).
    /// </summary>
    public static IServiceCollection AddRebalancerRedis(
        this IServiceCollection services,
        string connectionString,
        IRebalancerLogger? logger,
        ILeaseService? leaseService = null,
        IResourceService? resourceService = null,
        IClientService? clientService = null
    )
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddTransient<IRebalancerProvider>(_ => new RedisProvider(
                connectionString,
                logger,
                leaseService,
                resourceService,
                clientService
            )
        );
        services.AddTransient<RebalancerClient>();
        return services;
    }
}
