using System.Net;
using System.Net.Sockets;

namespace Rebalancer.IntegrationTests;

/// <summary>
///     A TCP proxy in front of one server, for network faults between one node and its store. It forwards both ways, and
///     can
///     <list type="bullet">
///         <item>
///             partition the link (<see cref="Partition" />): connections stay open but no byte passes either way, and
///             new ones are accepted and held too, as in a real partition where packets are dropped silently; each side
///             notices only through its own timeouts;
///         </item>
///         <item>
///             add latency (<see cref="Degrade" />): every chunk waits a base delay plus jitter, and some chunks wait
///             much longer, as TCP retransmissions after packet loss do. Order is kept within each direction.
///         </item>
///     </list>
/// </summary>
internal sealed class TcpFaultProxy : IAsyncDisposable
{
    private readonly List<TcpClient> connections = [];
    private readonly CancellationTokenSource cts = new();
    private readonly TcpListener listener;
    private readonly Random random = new();
    private readonly object sync = new();
    private readonly string upstreamHost;
    private readonly int upstreamPort;
    private TimeSpan baseDelay;
    private TimeSpan jitter;
    private volatile bool partitioned;
    private TimeSpan spikeDelay;
    private double spikeProbability;

    public TcpFaultProxy(string upstreamHost, int upstreamPort)
    {
        this.upstreamHost = upstreamHost;
        this.upstreamPort = upstreamPort;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _ = AcceptLoopAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public string Endpoint => $"127.0.0.1:{Port}";

    public async ValueTask DisposeAsync()
    {
        await cts.CancelAsync();
        listener.Stop();
        lock (sync)
        {
            foreach (var connection in connections)
            {
                connection.Dispose();
            }

            connections.Clear();
        }

        cts.Dispose();
    }

    /// <summary>Drops every byte in both directions until <see cref="Heal" />.</summary>
    public void Partition() => partitioned = true;

    public void Heal() => partitioned = false;

    /// <summary>
    ///     Each chunk waits <paramref name="delay" /> ± <paramref name="delayJitter" />; with probability
    ///     <paramref name="lossProbability" /> it waits <paramref name="retransmissionDelay" /> more.
    /// </summary>
    public void Degrade(TimeSpan delay, TimeSpan delayJitter, double lossProbability, TimeSpan retransmissionDelay)
    {
        lock (sync)
        {
            baseDelay = delay;
            jitter = delayJitter;
            spikeProbability = lossProbability;
            spikeDelay = retransmissionDelay;
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            TcpClient downstream;
            try
            {
                downstream = await listener.AcceptTcpClientAsync(cts.Token);
            }
            catch (Exception)
            {
                return; // stopped
            }

            _ = ConnectAsync(downstream);
        }
    }

    private async Task ConnectAsync(TcpClient downstream)
    {
        var upstream = new TcpClient();
        lock (sync)
        {
            connections.Add(downstream);
            connections.Add(upstream);
        }

        try
        {
            await upstream.ConnectAsync(upstreamHost, upstreamPort, cts.Token);
            await Task.WhenAny(
                PumpAsync(downstream.GetStream(), upstream.GetStream()),
                PumpAsync(upstream.GetStream(), downstream.GetStream())
            );
        }
        catch (Exception)
        {
            // the connection ends; the node sees a reset, as with a real server that went away
        }
        finally
        {
            downstream.Dispose();
            upstream.Dispose();
            lock (sync)
            {
                connections.Remove(downstream);
                connections.Remove(upstream);
            }
        }
    }

    private async Task PumpAsync(NetworkStream from, NetworkStream to)
    {
        var buffer = new byte[16 * 1024];
        while (!cts.IsCancellationRequested)
        {
            var read = await from.ReadAsync(buffer, cts.Token);
            if (read == 0)
            {
                return;
            }

            var delay = NextDelay();
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cts.Token);
            }

            // nothing passes while partitioned; as with TCP retransmission, held data arrives once the link heals
            // (the stream is never corrupted), unless a side gave up on the connection first
            while (partitioned)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cts.Token);
            }

            await to.WriteAsync(buffer.AsMemory(0, read), cts.Token);
        }
    }

    private TimeSpan NextDelay()
    {
        lock (sync)
        {
            var delay = baseDelay +
                        TimeSpan.FromMilliseconds(jitter.TotalMilliseconds * ((random.NextDouble() * 2) - 1));
            if (random.NextDouble() < spikeProbability)
            {
                delay += spikeDelay;
            }

            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
    }
}
