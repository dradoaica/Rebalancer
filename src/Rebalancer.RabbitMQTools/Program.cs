using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Rebalancer.RabbitMQTools;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            LogError("Invalid command");
            Environment.ExitCode = 1;
        }

        var builder = new ConfigurationBuilder().AddCommandLine(args);
        var configuration = builder.Build();
        var command = GetMandatoryArg(configuration, "Command");
        var backend = GetMandatoryArg(configuration, "Backend");
        var connection = GetMandatoryArg(configuration, "ConnString");
        RabbitConnection rabbitConn = new()
        {
            Host = GetOptionalArg(configuration, "RabbitHost", "localhost"),
            VirtualHost = GetOptionalArg(configuration, "RabbitVHost", "/"),
            Username = GetOptionalArg(configuration, "RabbitUser", "guest"),
            Password = GetOptionalArg(configuration, "RabbitPassword", "guest"),
            Port = int.Parse(GetOptionalArg(configuration, "RabbitPort", "5672")),
            ManagementPort = int.Parse(GetOptionalArg(configuration, "RabbitMgmtPort", "15672"))
        };
        QueueInventory queueInventory = new()
        {
            ConsumerGroup = GetMandatoryArg(configuration, "ConsumerGroup"),
            ExchangeName = GetMandatoryArg(configuration, "ExchangeName"),
            QueueCount = int.Parse(GetMandatoryArg(configuration, "QueueCount")),
            QueuePrefix = GetMandatoryArg(configuration, "QueuePrefix"),
            LeaseExpirySeconds = int.Parse(GetMandatoryArg(configuration, "LeaseExpirySeconds"))
        };
        if (command.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            if (backend.Equals("mssql", StringComparison.OrdinalIgnoreCase))
            {
                await DeployQueuesWithSqlBackend(connection, rabbitConn, queueInventory).ConfigureAwait(false);
            }
            else
            {
                LogWarn("Only mssql backend is supported");
            }
        }
        else
        {
            LogWarn("Only create command is supported");
        }

        return 0;
    }

    private static async Task DeployQueuesWithSqlBackend(string connection, RabbitConnection rabbitConn,
        QueueInventory queueInventory)
    {
        try
        {
            QueueManager.Initialize(connection, rabbitConn);
            QueueManager.EnsureResourceGroup(queueInventory.ConsumerGroup, queueInventory.LeaseExpirySeconds);
            LogInfo("Phase 1 - Reconcile Backend with existing RabbitMQ queues ---------");
            QueueManager.ReconcileQueuesSqlAsync(queueInventory.ConsumerGroup, queueInventory.QueuePrefix).Wait();
            LogInfo("Phase 2 - Ensure supplied queue count is deployed ---------");
            var existingQueues = QueueManager.GetQueuesAsync(queueInventory.QueuePrefix).Result;
            if (existingQueues.Count > queueInventory.QueueCount)
            {
                var queuesToRemove = existingQueues.Count - queueInventory.QueueCount;
                for (var i = 0; i < queuesToRemove; i++)
                {
                    await QueueManager.RemoveQueueSqlAsync(queueInventory.ConsumerGroup, queueInventory.QueuePrefix)
                        .ConfigureAwait(false);
                }
            }
            else if (existingQueues.Count < queueInventory.QueueCount)
            {
                var queuesToAdd = queueInventory.QueueCount - existingQueues.Count;
                for (var i = 0; i < queuesToAdd; i++)
                {
                    await QueueManager.AddQueueSqlAsync(queueInventory.ConsumerGroup, queueInventory.ExchangeName,
                        queueInventory.QueuePrefix).ConfigureAwait(false);
                }
            }

            LogInfo("Complete");
            Environment.ExitCode = 0;
        }
        catch (Exception ex)
        {
            LogError(ex.Message);
            Environment.ExitCode = 1;
        }
    }

    private static string GetMandatoryArg(IConfiguration configuration, string argName)
    {
        var value = configuration[argName];
        if (string.IsNullOrEmpty(value))
        {
            throw new Exception($"No argument {argName}");
        }

        return value;
    }

    private static string GetOptionalArg(IConfiguration configuration, string argName, string defaultValue)
    {
        var value = configuration[argName];
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        return value;
    }

    private static void LogInfo(string text)
    {
        Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: INFO  : {text}");
    }

    private static void LogWarn(string text)
    {
        Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: WARN  : {text}");
    }

    private static void LogError(string text)
    {
        Console.WriteLine($"{DateTime.Now.ToString("hh:mm:ss,fff")}: ERROR  : {text}");
    }
}
