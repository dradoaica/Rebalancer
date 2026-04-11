using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.SqlServer;
using Rebalancer.SqlServer.Clients;
using Rebalancer.SqlServer.Leases;
using Rebalancer.SqlServer.Resources;
using System;

namespace Rebalancer.Extensions.DependencyInjection;

public static class SqlServerRebalancerServiceCollectionExtensions
{
    /// <summary>
    ///     Registers a transient <see cref="SqlServerProvider" /> as <see cref="IRebalancerProvider" /> and
    ///     <see cref="RebalancerClient" />.
    /// </summary>
    public static IServiceCollection AddRebalancerSqlServer(this IServiceCollection services, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddTransient<IRebalancerProvider>(_ => new SqlServerProvider(connectionString));
        services.AddTransient<RebalancerClient>();
        return services;
    }

    /// <summary>
    ///     Registers <see cref="SqlServerProvider" /> with optional custom services (same semantics as the provider
    ///     constructor).
    /// </summary>
    public static IServiceCollection AddRebalancerSqlServer(
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

        services.AddTransient<IRebalancerProvider>(_ => new SqlServerProvider(
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
