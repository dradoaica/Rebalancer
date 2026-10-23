using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Rebalancer.IntegrationTests.ZooKeeper.Helpers;

/// <summary>
///     The three ZooKeeper servers of the README's test ensemble (containers rbr-it-zk1..3 on localhost:2181..2183),
///     controlled with the docker CLI.
/// </summary>
internal static class ZkEnsemble
{
    public static readonly string[] Containers = ["rbr-it-zk1", "rbr-it-zk2", "rbr-it-zk3",];
    public static readonly int[] Ports = [2181, 2182, 2183,];

    public static bool ContainersRunning()
    {
        try
        {
            var (exitCode, output) = RunDocker("ps --format {{.Names}}").GetAwaiter().GetResult();
            return exitCode == 0 && Containers.All(output.Split('\n').Select(x => x.Trim()).Contains);
        }
        catch (Exception)
        {
            return false; // no docker CLI
        }
    }

    public static Task PauseAsync(params string[] containers) => DockerAsync("pause", containers);

    public static Task UnpauseAsync(params string[] containers) => DockerAsync("unpause", containers);

    /// <summary>The container of the current leader, by asking each server ("srvr").</summary>
    public static async Task<string> LeaderAsync()
    {
        for (var i = 0; i < Ports.Length; i++)
        {
            if ((await ModeAsync(Ports[i])).Contains("leader", StringComparison.Ordinal))
            {
                return Containers[i];
            }
        }

        throw new InvalidOperationException("The ensemble has no leader");
    }

    /// <summary>Waits until every server is serving again (after an unpause).</summary>
    public static async Task WaitUntilServingAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var modes = await Task.WhenAll(Ports.Select(ModeAsync));
            if (modes.All(m => m.Contains("leader", StringComparison.Ordinal) ||
                               m.Contains("follower", StringComparison.Ordinal)
                ))
            {
                return;
            }

            Assert.True(DateTime.UtcNow < deadline, "the ensemble did not serve again: " + string.Join(", ", modes));
            await Task.Delay(500);
        }
    }

    private static async Task<string> ModeAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync("localhost", port, timeout.Token);
            var stream = client.GetStream();
            await stream.WriteAsync("srvr"u8.ToArray(), timeout.Token);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var output = await reader.ReadToEndAsync(timeout.Token);
            return output.Split('\n').FirstOrDefault(x => x.StartsWith("Mode:", StringComparison.Ordinal)) ?? "none";
        }
        catch (Exception)
        {
            return "unreachable";
        }
    }

    private static async Task DockerAsync(string command, IEnumerable<string> containers)
    {
        var (exitCode, output) = await RunDocker($"{command} {string.Join(" ", containers)}");
        Assert.True(exitCode == 0, $"docker {command} failed: {output}");
    }

    private static async Task<(int ExitCode, string Output)> RunDocker(string arguments)
    {
        using var process = Process.Start(
            new ProcessStartInfo("docker", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }
        )!;
        var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
