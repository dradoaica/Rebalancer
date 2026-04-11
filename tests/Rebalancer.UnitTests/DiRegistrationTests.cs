using Microsoft.Extensions.DependencyInjection;
using Rebalancer.Core;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.SqlServer;
using Xunit;

namespace Rebalancer.UnitTests;

public sealed class DiRegistrationTests
{
    [Fact]
    public void AddRebalancerSqlServer_Resolves_RebalancerClient_Without_Static_Providers()
    {
        var services = new ServiceCollection();
        services.AddRebalancerSqlServer("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;");
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<RebalancerClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddRebalancerProvider_Factory_Resolves_RebalancerClient_Without_Static_Providers()
    {
        var services = new ServiceCollection();
        services.AddRebalancerProvider(_ =>
            new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;")
        );
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<RebalancerClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void RebalancerClient_Constructor_With_Injected_Provider_Does_Not_Use_Static_Providers()
    {
        var backend = new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;");
        using var client = new RebalancerClient(backend);
        Assert.NotNull(client);
    }
}
