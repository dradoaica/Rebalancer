using org.apache.zookeeper;
using Rebalancer.Core;
using Rebalancer.Core.Logging;
using Rebalancer.IntegrationTests.ZooKeeper.Helpers;
using Rebalancer.ZooKeeper;
using System.Text;
using Xunit;

namespace Rebalancer.IntegrationTests.ZooKeeper;

/// <summary>
///     Requires the ZooKeeper cluster from <see cref="ZkHelper" />. Contract of ADR 0006: with digest auth and the
///     default CREATOR_ALL_ACL, assignment still works, sessions without the credentials cannot read the znodes, and
///     sessions with them can.
/// </summary>
public class AclTests : IDisposable
{
    private const string OpenZnodeWarning = "is writable by anyone";
    private static readonly byte[] Credentials = Encoding.UTF8.GetBytes("rebalancer:secret");
    private readonly ZkHelper zkHelper = new();

    public void Dispose() => Task.Run(async () => await zkHelper.CloseAsync()).Wait();

    [Theory]
    [InlineData(RebalancingMode.GlobalBarrier)]
    [InlineData(RebalancingMode.ResourceBarrier)]
    public async Task GivenDigestAuth_ThenClientIsAssigned_AndUnauthenticatedSessionsAreLockedOut(RebalancingMode mode)
    {
        var groupName = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(groupName, "res", 3);

        var provider = new ZooKeeperProvider(
            ZkHelper.zooKeeperHosts,
            "/rebalancer",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(1),
            mode,
            new TestOutputLogger(),
            null, // CREATOR_ALL_ACL, because an auth scheme is set
            "digest",
            Credentials
        );
        using var client = new RebalancerClient(provider);
        await client.StartAsync(
            groupName,
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (client.GetCurrentState() != ClientState.Assigned && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }

            Assert.Equal(ClientState.Assigned, client.GetCurrentState());
            Assert.Equal(3, client.GetAssignedResources().Resources.Count);

            // the provider created the clients znode, so it carries the ACL: a session without credentials is refused
            await Assert.ThrowsAsync<KeeperException.NoAuthException>(() =>
                zkHelper.GetChildrenAsync($"{groupName}/clients")
            );

            // the same credentials grant access, and the ACL is a digest ACL
            zkHelper.AddAuthInfo("digest", Credentials);
            Assert.Single(await zkHelper.GetChildrenAsync($"{groupName}/clients"));
            var acl = await zkHelper.GetAclAsync($"{groupName}/clients");
            Assert.NotEmpty(acl);
            Assert.All(acl, x => Assert.Equal("digest", x.getId().getScheme()));
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(30));
        }
    }

    // ADR 0010: with credentials configured, operator-created znodes that are still open are reported once each
    [Theory]
    [InlineData(RebalancingMode.GlobalBarrier)]
    [InlineData(RebalancingMode.ResourceBarrier)]
    public async Task GivenDigestAuth_AndOpenOperatorZnodes_ThenEachOpenZnodeIsReported(RebalancingMode mode)
    {
        var groupName = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(groupName, "res", 3); // OPEN_ACL_UNSAFE, as operators often do

        var warnings = await RunUntilAssignedAsync(groupName, mode, Credentials);

        Assert.Single(warnings, x => x.Contains($"/rebalancer/{groupName} ") && x.Contains(OpenZnodeWarning));
        Assert.Single(warnings, x => x.Contains($"/rebalancer/{groupName}/resources ") && x.Contains(OpenZnodeWarning));
    }

    [Fact]
    public async Task GivenDigestAuth_AndSecuredOperatorZnodes_ThenNothingIsReported()
    {
        var groupName = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        zkHelper.AddAuthInfo("digest", Credentials);
        await zkHelper.PrepareResourceGroupAsync(groupName, "res", 3, ZooDefs.Ids.CREATOR_ALL_ACL);

        var warnings = await RunUntilAssignedAsync(groupName, RebalancingMode.GlobalBarrier, Credentials);

        Assert.DoesNotContain(warnings, x => x.Contains(groupName));
    }

    [Fact]
    public async Task GivenNoAuth_ThenOpenZnodesAreNotChecked()
    {
        var groupName = Guid.NewGuid().ToString();
        await zkHelper.InitializeAsync("/rebalancer", TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
        await zkHelper.PrepareResourceGroupAsync(groupName, "res", 3);

        var warnings = await RunUntilAssignedAsync(groupName, RebalancingMode.GlobalBarrier, null);

        Assert.DoesNotContain(warnings, x => x.Contains(OpenZnodeWarning));
    }

    private static async Task<List<string>> RunUntilAssignedAsync(
        string groupName,
        RebalancingMode mode,
        byte[]? credentials
    )
    {
        var logger = new WarningRecorder();
        var provider = new ZooKeeperProvider(
            ZkHelper.zooKeeperHosts,
            "/rebalancer",
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(1),
            mode,
            logger,
            credentials == null ? ZooDefs.Ids.OPEN_ACL_UNSAFE : null, // 3.0: no credentials needs an explicit ACL
            credentials == null ? null : "digest",
            credentials
        );
        using var client = new RebalancerClient(provider);
        await client.StartAsync(
            groupName,
            new ClientOptions
            {
                AutoRecoveryOnError = false,
            }
        );
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (client.GetCurrentState() != ClientState.Assigned && DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }

            Assert.Equal(ClientState.Assigned, client.GetCurrentState());
            return logger.Warnings;
        }
        finally
        {
            await client.StopAsync(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class WarningRecorder : IRebalancerLogger
    {
        private readonly TestOutputLogger output = new();

        public List<string> Warnings { get; } = [];

        public void SetMinimumLevel(LogLevel logLevel) => output.SetMinimumLevel(logLevel);
        public void Debug(string clientId, string text) => output.Debug(clientId, text);
        public void Info(string clientId, string text) => output.Info(clientId, text);

        public void Warn(string clientId, string text)
        {
            lock (Warnings)
            {
                Warnings.Add(text);
            }

            output.Warn(clientId, text);
        }

        public void Error(string clientId, string text) => output.Error(clientId, text);
        public void Error(string clientId, Exception ex) => output.Error(clientId, ex);
        public void Error(string clientId, string text, Exception ex) => output.Error(clientId, text, ex);
    }
}
