namespace Rebalancer.ZooKeeper.Tests.RandomisedTests.TestComponents;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Core;
using Core.Logging;

public class TestClient
{
    public static int ClientNumber;

    private readonly TimeSpan onStartTime;
    private readonly TimeSpan onStopTime;
    private readonly Random rand;
    private readonly bool randomiseTimes;

    public TestClient(ResourceMonitor resourceMonitor,
        string resourceGroup,
        ClientOptions clientOptions,
        TimeSpan onStartTime,
        TimeSpan onStopTime,
        bool randomiseTimes)
    {
        this.ResourceGroup = resourceGroup;
        this.ClientOptions = clientOptions;
        this.Monitor = resourceMonitor;
        this.Resources = new List<string>();

        this.onStartTime = onStartTime;
        this.onStopTime = onStopTime;
        this.randomiseTimes = randomiseTimes;
        this.rand = new Random(Guid.NewGuid().GetHashCode());
    }

    public string Id { get; set; }
    public RebalancerClient Client { get; set; }
    public IList<string> Resources { get; set; }
    public bool Started { get; set; }
    public string ResourceGroup { get; set; }
    public ClientOptions ClientOptions { get; set; }
    public ResourceMonitor Monitor { get; set; }

    public async Task StartAsync(IRebalancerLogger logger)
    {
        this.CreateNewClient(logger);
        await this.Client.StartAsync(this.ResourceGroup, this.ClientOptions);
        this.Started = true;
    }

    public async Task StopAsync()
    {
        await this.Client.StopAsync(TimeSpan.FromSeconds(30));
        this.Started = false;
    }

    public async Task PerformActionAsync(IRebalancerLogger logger)
    {
        if (this.Started)
        {
            logger.Info("TEST RUNNER", "Stopping client");
            this.Monitor.RegisterRemoveClient(this.Id);
            await this.StopAsync();
            logger.Info("TEST RUNNER", "Stopped client");
        }
        else
        {
            logger.Info("TEST RUNNER", "Starting client");
            await this.StartAsync(logger);
            logger.Info("TEST RUNNER", "Started client");
        }
    }

    private void CreateNewClient(IRebalancerLogger logger)
    {
        this.Id = $"Client{ClientNumber}";
        ClientNumber++;
        this.Monitor.RegisterAddClient(this.Id);
        this.Client = new RebalancerClient();
        this.Client.OnAssignment += (sender, args) =>
        {
            this.Resources = args.Resources;
            foreach (var resource in args.Resources)
            {
                this.Monitor.ClaimResource(resource, this.Id);
            }

            if (this.onStartTime > TimeSpan.Zero)
            {
                if (this.randomiseTimes)
                {
                    var waitTime = this.onStartTime.TotalMilliseconds * this.rand.NextDouble();
                    Thread.Sleep((int)waitTime);
                }
                else
                {
                    Thread.Sleep(this.onStartTime);
                }
            }
        };

        this.Client.OnUnassignment += (sender, args) =>
        {
            foreach (var resource in this.Resources)
            {
                this.Monitor.ReleaseResource(resource, this.Id);
            }

            this.Resources.Clear();

            if (this.onStopTime > TimeSpan.Zero)
            {
                if (this.randomiseTimes)
                {
                    var waitTime = this.onStopTime.TotalMilliseconds * this.rand.NextDouble();
                    Thread.Sleep((int)waitTime);
                }
                else
                {
                    Thread.Sleep(this.onStopTime);
                }
            }
        };

        this.Client.OnAborted += (sender, args) =>
        {
            logger.Info("CLIENT", $"CLIENT ABORTED: {args.AbortReason}");
        };
    }
}
