using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using System;

namespace Rebalancer.Extensions.DependencyInjection;

/// <summary>
///     Registers <see cref="RebalancerClient" /> and custom <see cref="IRebalancerProvider" /> factories (ADR-0005,
///     ADR-0013).
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <see cref="RebalancerClient" /> as transient. You must also register
    ///     <see cref="IRebalancerProvider" />.
    /// </summary>
    public static IServiceCollection AddRebalancerClient(this IServiceCollection services)
    {
        services.AddTransient<RebalancerClient>();
        return services;
    }

    /// <summary>Registers <see cref="IRebalancerProvider" /> from a factory and <see cref="RebalancerClient" /> as transient.</summary>
    public static IServiceCollection AddRebalancerProvider(
        this IServiceCollection services,
        Func<IServiceProvider, IRebalancerProvider> factory
    )
    {
        services.AddTransient(factory);
        services.AddTransient<RebalancerClient>();
        return services;
    }
}
