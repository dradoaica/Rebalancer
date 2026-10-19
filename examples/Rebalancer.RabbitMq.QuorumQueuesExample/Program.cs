using RabbitMQ.Client;
using Rebalancer.Core;
using Rebalancer.SqlServer;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.QuorumQueuesExample;

/// <summary>
///     An ordered consumer group on RabbitMQ (US-0022, "option A"): a consistent-hash exchange spreads conversations
///     over quorum queues with single active consumer and dead-lettering; Rebalancer spreads the queues over the nodes; a
///     turn-number guard in SQL Server keeps each conversation in order. See README.md.
/// </summary>
internal static class Program
{
    public static async Task<int> Main()
    {
        var settings = Settings.FromEnvironment();

        void Log(string text)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{settings.Node}] {text}");
        }

        var factory = new ConnectionFactory
        {
            HostName = settings.RabbitHost,
            Port = settings.RabbitPort,
            UserName = settings.RabbitUser,
            Password = settings.RabbitPassword,
            ClientProvidedName = $"rebalancer-example-{settings.Node}",
        };
        await using var connection = await factory.CreateConnectionAsync();
        var consumers = new QueueConsumers(
            connection,
            settings,
            new ConversationGuard(settings.SqlConnectionString),
            Log
        );

        using var client = new RebalancerClient(new SqlServerProvider(settings.SqlConnectionString));
        // Rebalancer invokes these one after the other; waiting here keeps "stop old queues" before "start new ones"
        client.OnAssignment += (_, args) =>
        {
            Log($"assigned: {string.Join(", ", args.Resources)}");
            consumers.StartAsync(args.Resources).GetAwaiter().GetResult();
        };
        client.OnUnassignment += (_, _) =>
        {
            Log("unassigned");
            consumers.StopAllAsync().GetAwaiter().GetResult();
        };
        // with AutoRecoveryOnError the client restarts after an error and is assigned again, so this only logs
        client.OnAborted += (_, args) => Log($"error: {args.AbortReason} {args.Exception?.Message}");

        // Ctrl+C and SIGTERM (docker stop, Kubernetes) stop the node cleanly: cancel the default termination, so the
        // Rebalancer client stops and the consumers finish their current turn before the process exits
        var stop = new TaskCompletionSource();

        void OnSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            stop.TrySetResult();
        }

        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

        await client.StartAsync(
            settings.ConsumerGroup,
            new ClientOptions
            {
                AutoRecoveryOnError = true,
                RestartDelay = TimeSpan.FromSeconds(5),
            }
        );
        Log($"started in group {settings.ConsumerGroup}; Ctrl+C to stop");
        await stop.Task;

        await client.StopAsync(TimeSpan.FromSeconds(30));
        await consumers.StopAllAsync();
        Log("stopped");
        return 0;
    }
}
