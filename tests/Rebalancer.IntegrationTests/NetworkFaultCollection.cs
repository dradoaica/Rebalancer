using Xunit;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     The SQL Server, PostgreSQL and Redis network-fault tests run alone, after the parallel tests: they check that
///     a degraded group still converges within a fixed time, which other tests' load on the machine can push it past
///     (CR-0014).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NetworkFaultCollection
{
    public const string Name = "Network faults, run alone";
}
