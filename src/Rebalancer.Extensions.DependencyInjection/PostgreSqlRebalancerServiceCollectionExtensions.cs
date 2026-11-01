using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.LeaseCoordination.Leases;
using Rebalancer.LeaseCoordination.Resources;
using Rebalancer.PostgreSql;
using System;

namespace Rebalancer.Extensions.DependencyInjection;

public static class PostgreSqlRebalancerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a transient <see cref="PostgreSqlProvider" /> as <see cref="IRebalancerProvider" /> and
    ///     <see cref="RebalancerClient" /> (ADR-0030).
    /// </summary>
    public static IServiceCollection AddRebalancerPostgreSql(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddTransient<IRebalancerProvider>(_ => new PostgreSqlProvider(connectionString));
        services.AddTransient<RebalancerClient>();
        return services;
    }

    /// <summary>
    ///     Registers <see cref="PostgreSqlProvider" /> with optional custom services (same semantics as the provider
    ///     constructor).
    /// </summary>
    public static IServiceCollection AddRebalancerPostgreSql(
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

        services.AddTransient<IRebalancerProvider>(_ => new PostgreSqlProvider(
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
