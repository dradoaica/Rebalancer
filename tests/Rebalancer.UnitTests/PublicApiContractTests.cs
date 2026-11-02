using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.Redis;
using Rebalancer.SqlServer;
using System.Reflection;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of 6.0 (ADR-0034): the types that no public signature used, deprecated in 5.2 (warning RBR0034), are
///     internal; <see cref="ClientNotFoundException" /> stays public, so a custom <see cref="IClientService" /> can
///     report a missing record and the client registers again.
/// </summary>
public sealed class PublicApiContractTests
{
    private static readonly Type[] RemovedFromPublicApi =
    [
        typeof(IClientService).Assembly.GetType("Rebalancer.LeaseCoordination.Roles.ClientEvent")!,
        typeof(IClientService).Assembly.GetType("Rebalancer.LeaseCoordination.Roles.CoordinatorToken")!,
        typeof(IClientService).Assembly.GetType("Rebalancer.LeaseCoordination.Roles.EventType")!,
        typeof(IClientService).Assembly.GetType("Rebalancer.LeaseCoordination.Store.AssignmentStatus")!,
        typeof(RedisProvider).Assembly.GetType("Rebalancer.Redis.ResourceGroup")!,
        typeof(RedisProvider).Assembly.GetType("Rebalancer.Redis.Resources.Resource")!,
        typeof(RedisProvider).Assembly.GetType("Rebalancer.Redis.Utils.Constants")!,
        typeof(SqlServerProvider).Assembly.GetType("Rebalancer.SqlServer.Connections.ConnectionHelper")!,
    ];

    public static TheoryData<Type> RemovedFromPublicApiTypes => new(RemovedFromPublicApi);

    [Theory]
    [MemberData(nameof(RemovedFromPublicApiTypes))]
    public void TheTypesDeprecatedIn52_AreInternal(Type type)
    {
        Assert.False(type.IsPublic);
        Assert.Null(type.GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void ClientNotFoundException_IsPublic_AndConstructibleByACustomClientService()
    {
        Assert.True(typeof(ClientNotFoundException).IsPublic);
        Assert.NotNull(typeof(ClientNotFoundException).GetConstructor([typeof(Guid),]));
        Assert.Null(typeof(ClientNotFoundException).GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void NoPublicTypeOfTheLeaseBasedPackages_IsDeprecated()
    {
        var deprecated = new[]
            {
                typeof(IClientService).Assembly, typeof(RedisProvider).Assembly, typeof(SqlServerProvider).Assembly,
            }
            .SelectMany(x => x.GetExportedTypes())
            .Where(x => x.GetCustomAttribute<ObsoleteAttribute>() != null);
        Assert.Empty(deprecated);
    }
}
