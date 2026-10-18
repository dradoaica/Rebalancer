using System;

namespace Rebalancer.Core;

/// <summary>Registers functions that create Rebalancer providers</summary>
public class Providers
{
    private static Func<IRebalancerProvider> GetRebalancerProvider;

    /// <summary>
    ///     Registers the process-wide provider factory used by <see cref="RebalancerClient()" />. Registering the same
    ///     factory again is a no-op.
    /// </summary>
    /// <exception cref="ProviderException">
    ///     A different factory is already registered (ADR 0009). The first registration stays
    ///     in effect; to use several providers in one process, pass each one to
    ///     <see cref="RebalancerClient(IRebalancerProvider)" /> or use the dependency injection extensions.
    /// </exception>
    public static void Register(Func<IRebalancerProvider> getRebalancerProvider)
    {
        if (GetRebalancerProvider == null)
        {
            GetRebalancerProvider = getRebalancerProvider;
        }
        else if (!GetRebalancerProvider.Equals(getRebalancerProvider))
        {
            throw new ProviderException(
                "A different provider factory is already registered and stays in effect. To use several providers in " +
                "one process, create clients with RebalancerClient(IRebalancerProvider) or the dependency injection " +
                "extensions instead of Providers.Register."
            );
        }
    }

    /// <summary>Clears the registration; only for tests, as the registry is process-wide.</summary>
    internal static void ResetForTests() => GetRebalancerProvider = null;

    /// <summary>Called from inside the RebalancerClient</summary>
    /// <returns>An IRebalancerProvider implementation</returns>
    /// <exception cref="ProviderException"></exception>
    public static IRebalancerProvider GetProvider()
    {
        if (GetRebalancerProvider == null)
        {
            throw new ProviderException("No provider registered!");
        }

        return GetRebalancerProvider();
    }
}
