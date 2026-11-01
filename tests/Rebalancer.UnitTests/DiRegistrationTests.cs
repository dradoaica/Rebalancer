using Microsoft.Extensions.DependencyInjection;
using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.Extensions.DependencyInjection;
using Rebalancer.SqlServer;
using Rebalancer.ZooKeeper;
using Rebalancer.ZooKeeper.Zk;
using System.Reflection;
using Xunit;

namespace Rebalancer.UnitTests;

public sealed class DiRegistrationTests
{
    // US-0015 AC3: every registration gives each client its own provider (providers hold per-node state, ADR-0016)
    public static TheoryData<string> Registrations => new()
    {
        "SqlServer",
        "PostgreSql",
        "Redis",
        "ZooKeeper",
        "Factory",
    };

    [Fact]
    public void AddRebalancerSqlServer_Resolves_RebalancerClient_Without_Static_Providers()
    {
        var services = new ServiceCollection();
        services.AddRebalancerSqlServer("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;");
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<RebalancerClient>();
        Assert.NotNull(client);
    }

    // US-0026 criterion 10
    [Fact]
    public void AddRebalancerPostgreSql_Resolves_RebalancerClient_Without_Static_Providers()
    {
        var services = new ServiceCollection();
        services.AddRebalancerPostgreSql("Host=127.0.0.1;Port=1;Timeout=1");
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

    [Theory]
    [MemberData(nameof(Registrations))]
    public void EachResolvedClient_GetsItsOwnProvider(string registration)
    {
        var services = new ServiceCollection();
        _ = registration switch
        {
            "SqlServer" => services.AddRebalancerSqlServer(
                "Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;"
            ),
            "PostgreSql" => services.AddRebalancerPostgreSql("Host=127.0.0.1;Port=1;Timeout=1"),
            "Redis" => services.AddRebalancerRedis("localhost:1,abortConnect=false,connectTimeout=100"),
            "ZooKeeper" => services.AddRebalancerZooKeeper(
                "localhost:1",
                "/rebalancer",
                RebalancingMode.GlobalBarrier,
                ZooDefs.Ids.OPEN_ACL_UNSAFE,
                null,
                null
            ),
            _ => services.AddRebalancerProvider(_ =>
                new SqlServerProvider("Server=tcp:127.0.0.1,1;Connect Timeout=1;Trust Server Certificate=true;")
            ),
        };
        using var serviceProvider = services.BuildServiceProvider();

        var first = serviceProvider.GetRequiredService<IRebalancerProvider>();
        var second = serviceProvider.GetRequiredService<IRebalancerProvider>();
        Assert.NotSame(first, second);
        Assert.NotSame(
            serviceProvider.GetRequiredService<RebalancerClient>(),
            serviceProvider.GetRequiredService<RebalancerClient>()
        );
    }

    // US-0007 criterion 3: without arguments, the session and connect timeouts are 15 s and 10 s. The third default
    // (minimumRebalancingInterval, 1 s) never reaches the ZooKeeper service, so this test cannot observe it.
    [Fact]
    public async Task AddRebalancerZooKeeper_DefaultsTheSessionAndConnectTimeouts()
    {
        var (service, recorder) = SessionRecorder.Create();
        var services = new ServiceCollection();
        ZooKeeperRebalancerServiceCollectionExtensions.Register(
            services,
            "localhost:1",
            "/rebalancer",
            RebalancingMode.ResourceBarrier,
            null,
            null,
            null,
            null,
            () => service
        );
        await using var serviceProvider = services.BuildServiceProvider();
        using var client = serviceProvider.GetRequiredService<RebalancerClient>();

        await client.StartAsync("group", new ClientOptions());
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (recorder.SessionTimeout == null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await client.StopAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(15), recorder.SessionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), recorder.ConnectTimeout);
    }

    // stands in for IZooKeeperService: records the first StartSessionAsync and refuses the session
    public class SessionRecorder : DispatchProxy
    {
        public TimeSpan? ConnectTimeout { get; private set; }
        public TimeSpan? SessionTimeout { get; private set; }

        public static (IZooKeeperService Service, SessionRecorder Recorder) Create()
        {
            var service = Create<IZooKeeperService, SessionRecorder>();
            // the proxy type DispatchProxy generates at run time derives from SessionRecorder
            // ReSharper disable once SuspiciousTypeConversion.Global
            return (service, (SessionRecorder)service);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IZooKeeperService.StartSessionAsync) && SessionTimeout == null)
            {
                SessionTimeout = (TimeSpan)args![0]!;
                ConnectTimeout = (TimeSpan)args[1]!;
            }

            var returnType = targetMethod.ReturnType;
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            // Task<T>: the default value, e.g. false for StartSessionAsync (no session)
            var result = returnType.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(result)
                .Invoke(null, [result.IsValueType ? Activator.CreateInstance(result) : null,]);
        }
    }
}
