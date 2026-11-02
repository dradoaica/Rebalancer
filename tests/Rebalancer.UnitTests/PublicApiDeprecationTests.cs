using Rebalancer.LeaseCoordination.Clients;
using Rebalancer.Redis;
using Rebalancer.SqlServer;
using System.Reflection;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>
///     Contract of 5.2 (ADR-0034): the public types that no public signature uses are deprecated with diagnostic
///     RBR0034, to become internal in 6.0; <see cref="ClientNotFoundException" /> is public, so a custom
///     <see cref="IClientService" /> can report a missing record and the client registers again.
/// </summary>
public sealed class PublicApiDeprecationTests
{
    private static readonly Type[] Deprecated =
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

    public static TheoryData<Type> DeprecatedTypes => new(Deprecated);

    [Theory]
    [MemberData(nameof(DeprecatedTypes))]
    public void UnusedPublicTypes_AreDeprecatedFor60(Type type)
    {
        Assert.True(type.IsPublic);
        var obsolete = type.GetCustomAttribute<ObsoleteAttribute>();
        Assert.NotNull(obsolete);
        Assert.Equal("RBR0034", obsolete.DiagnosticId);
        Assert.False(obsolete.IsError);
        Assert.Contains("6.0", obsolete.Message);
    }

    [Fact]
    public void ClientNotFoundException_IsPublic_AndConstructibleByACustomClientService()
    {
        Assert.True(typeof(ClientNotFoundException).IsPublic);
        Assert.NotNull(typeof(ClientNotFoundException).GetConstructor([typeof(Guid),]));
        Assert.Null(typeof(ClientNotFoundException).GetCustomAttribute<ObsoleteAttribute>());
    }

    [Fact]
    public void NoOtherPublicTypeOfTheLeaseBasedPackages_IsDeprecated()
    {
        var deprecated = Deprecated.ToHashSet();
        var others = new[]
            {
                typeof(IClientService).Assembly, typeof(RedisProvider).Assembly, typeof(SqlServerProvider).Assembly,
            }
            .SelectMany(x => x.GetExportedTypes())
            .Where(x => !deprecated.Contains(x) && x.GetCustomAttribute<ObsoleteAttribute>() != null);
        Assert.Empty(others);
    }
}
