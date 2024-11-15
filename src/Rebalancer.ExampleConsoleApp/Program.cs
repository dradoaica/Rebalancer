﻿using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Rebalancer.Core;
using Rebalancer.SqlServer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.ExampleWithSqlServerBackend;

internal static class Program
{
    private static List<ClientTask> clientTasks;

    public static async Task<int> Main(string[] args)
    {
        await RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static async Task RunAsync()
    {
        Providers.Register(
            () => new SqlServerProvider("Server=(local);Database=RabbitMqScaling;Trusted_Connection=true;"));
        clientTasks = [];
        using RebalancerClient context = new();
        context.OnAssignment += (sender, args) =>
        {
            var queues = context.GetAssignedResources();
            foreach (var queue in queues.Resources)
            {
                StartConsuming(queue);
            }
        };
        context.OnUnassignment += (sender, args) =>
        {
            LogInfo("Consumer subscription cancelled");
            StopAllConsumption();
        };
        context.OnAborted += (sender, args) =>
        {
            LogInfo($"Error: {args.AbortReason}, Exception: {args.Exception.Message}");
        };
        await context.StartAsync(
            "NotificationsGroup",
            new ClientOptions { AutoRecoveryOnError = true, RestartDelay = TimeSpan.FromSeconds(30) });
        LogInfo("Press enter to shutdown");
        while (!Console.KeyAvailable)
        {
            Thread.Sleep(100);
        }

        StopAllConsumption();
        Task.WaitAll(clientTasks.Select(x => x.Client).ToArray());
    }

    private static void StartConsuming(string queueName)
    {
        LogInfo("Subscription started for queue: " + queueName);
        CancellationTokenSource cts = new();

        var task = Task.Run(
            async () =>
            {
                try
                {
                    ConnectionFactory factory = new() { HostName = "localhost" };
                    await using var connection = await factory.CreateConnectionAsync(cts.Token);
                    await using var channel = await connection.CreateChannelAsync(cancellationToken: cts.Token);
                    AsyncEventingBasicConsumer consumer = new(channel);
                    consumer.ReceivedAsync += (_, ea) =>
                    {
                        var body = ea.Body.ToArray();
                        var message = Encoding.UTF8.GetString(body);
                        LogInfo($"{queueName} Received {message}");
                        return Task.CompletedTask;
                    };
                    await channel.BasicConsumeAsync(queueName, true, consumer, cts.Token);
                    while (!cts.Token.IsCancellationRequested)
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    LogError(ex.ToString());
                }

                if (cts.Token.IsCancellationRequested)
                {
                    LogInfo("Cancellation signal received for " + queueName);
                }
                else
                {
                    LogInfo("Consumer stopped for " + queueName);
                }
            });
        clientTasks.Add(new ClientTask { Cts = cts, Client = task });
    }

    private static void StopAllConsumption()
    {
        foreach (var ct in clientTasks)
        {
            ct.Cts.Cancel();
        }
    }

    private static void LogInfo(string text) =>
        Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: INFO  : {text}");

    private static void LogError(string text) =>
        Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR  : {text}");
}
