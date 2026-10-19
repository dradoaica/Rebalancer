using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.Tools;

public static class QueueManager
{
    private static string connStr;
    private static HttpClient client;

    public static void Initialize(string connectionString, RabbitConnection rabbitConnection)
    {
        connStr = connectionString;
        client = new HttpClient
        {
            BaseAddress = new Uri($"http://{rabbitConnection.Host}:{rabbitConnection.ManagementPort}/api/"),
        };
        var byteArray = Encoding.ASCII.GetBytes($"{rabbitConnection.Username}:{rabbitConnection.Password}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
    }

    public static void EnsureResourceGroup(string resourceGroup, int leaseExpirySeconds, int heartbeatSeconds)
    {
        var rgExists = false;
        using SqlConnection conn = new(connStr);
        conn.Open();
        var command = conn.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RBR.ResourceGroups WHERE ResourceGroup = @ResourceGroup";
        command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
        var count = (int)command.ExecuteScalar();
        rgExists = count == 1;
        if (!rgExists)
        {
            command.Parameters.Clear();
            command.CommandText =
                "INSERT INTO RBR.ResourceGroups(ResourceGroup, FencingToken, LeaseExpirySeconds, HeartbeatSeconds) VALUES(@ResourceGroup, 1, @LeaseExpirySeconds, @HeartbeatSeconds)";
            command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            command.Parameters.Add("LeaseExpirySeconds", SqlDbType.Int).Value = leaseExpirySeconds;
            command.Parameters.Add("HeartbeatSeconds", SqlDbType.Int).Value = heartbeatSeconds;
            command.ExecuteNonQuery();
            Console.WriteLine("Created consumer group");
        }
        else
        {
            command.Parameters.Clear();
            command.CommandText =
                "UPDATE RBR.ResourceGroups SET LeaseExpirySeconds = @LeaseExpirySeconds, HeartbeatSeconds = @HeartbeatSeconds WHERE ResourceGroup = @ResourceGroup";
            command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            command.Parameters.Add("LeaseExpirySeconds", SqlDbType.Int).Value = leaseExpirySeconds;
            command.Parameters.Add("HeartbeatSeconds", SqlDbType.Int).Value = heartbeatSeconds;
            command.ExecuteNonQuery();
            Console.WriteLine("Consumer group exists");
        }
    }

    public static async Task ReconcileQueuesSqlAsync(string resourceGroup, string queuePrefix)
    {
        var rabbitQueues = await GetQueuesFromRabbitMqAsync(queuePrefix);
        Console.WriteLine($"{rabbitQueues.Count} matching queues in RabbitMQ");
        var sqlQueues = GetQueuesFromSqlServer(resourceGroup);
        Console.WriteLine($"{sqlQueues.Count} matching registered queues in SQL Server backend");
        foreach (var queue in rabbitQueues)
        {
            if (sqlQueues.All(x => x != queue))
            {
                InsertQueueSql(resourceGroup, queue);
                Console.WriteLine("Added queue to backend");
            }
        }

        foreach (var queue in sqlQueues)
        {
            if (rabbitQueues.All(x => x != queue))
            {
                DeleteQueueSql(resourceGroup, queue);
                Console.WriteLine("Removed queue from backend");
            }
        }
    }

    /// <summary>
    ///     The conversation table of the example consumer: the last processed turn per conversation, and whether the
    ///     conversation is blocked after a failed turn (US-0022).
    /// </summary>
    public static void EnsureConversationTable()
    {
        using SqlConnection conn = new(connStr);
        conn.Open();
        var command = conn.CreateCommand();
        command.CommandText = """
                              IF SCHEMA_ID('Example') IS NULL EXEC('CREATE SCHEMA Example');
                              IF OBJECT_ID('Example.ConversationTurns') IS NULL
                                  CREATE TABLE Example.ConversationTurns (
                                      ConversationId varchar(200) NOT NULL PRIMARY KEY,
                                      LastTurn int NOT NULL,
                                      Blocked bit NOT NULL);
                              """;
        command.ExecuteNonQuery();
    }

    public static void Unblock(string conversationId)
    {
        using SqlConnection conn = new(connStr);
        conn.Open();
        var command = conn.CreateCommand();
        command.CommandText = "UPDATE Example.ConversationTurns SET Blocked = 0 WHERE ConversationId = @ConversationId";
        command.Parameters.Add("ConversationId", SqlDbType.VarChar, 200).Value = conversationId;
        Console.WriteLine(command.ExecuteNonQuery() == 1 ? $"Unblocked {conversationId}" : $"{conversationId} not found");
    }

    /// <summary>The fanout dead-letter exchange and its quorum queue; failed and parked messages end here.</summary>
    public static async Task EnsureDeadLetteringAsync(QueueInventory inventory, string vhost = "%2f")
    {
        await PutAsync($"exchanges/{vhost}/{inventory.DeadLetterExchange}",
            "{\"type\":\"fanout\",\"durable\":true,\"auto_delete\":false,\"internal\":false,\"arguments\":{}}");
        await PutAsync($"queues/{vhost}/{inventory.DeadLetterQueue}",
            "{\"durable\":true,\"arguments\":{\"x-queue-type\":\"quorum\"}}");
        await PostAsync($"bindings/{vhost}/e/{inventory.DeadLetterExchange}/q/{inventory.DeadLetterQueue}",
            "{\"routing_key\":\"\",\"arguments\":{}}");
    }

    public static async Task AddQueueSqlAsync(string resourceGroup, QueueInventory inventory)
    {
        var queuePrefix = inventory.QueuePrefix;
        var lastQueue = await GetMaxQueueAsync(queuePrefix);
        if (lastQueue == null)
        {
            var queueName = queuePrefix + "_0001";
            await PutQueueRabbitMqAsync(inventory, queueName);
            InsertQueueSql(resourceGroup, queueName);
            Console.WriteLine($"Queue {queueName} added to consumer group {resourceGroup}");
        }
        else
        {
            var qNumber = lastQueue[(lastQueue.IndexOf("_") + 1)..];
            var nextNumber = int.Parse(qNumber) + 1;
            var queueName = queuePrefix + "_" + nextNumber.ToString().PadLeft(4, '0');
            await PutQueueRabbitMqAsync(inventory, queueName);
            InsertQueueSql(resourceGroup, queueName);
            Console.WriteLine($"Queue {queueName} added to consumer group {resourceGroup}");
        }
    }

    public static async Task RemoveQueueSqlAsync(string resourceGroup, string queuePrefix)
    {
        var lastQueue = await GetMaxQueueAsync(queuePrefix);
        if (lastQueue != null)
        {
            await DeleteQueueRabbitMqAsync(lastQueue);
            DeleteQueueSql(resourceGroup, lastQueue);
            Console.WriteLine($"Queue {lastQueue} removed from consumer group {resourceGroup}");
        }
    }

    public static async Task<List<string>> GetQueuesAsync(string queuePrefix) =>
        await GetQueuesFromRabbitMqAsync(queuePrefix);

    private static async Task<string> GetMaxQueueAsync(string queuePrefix)
    {
        var queuesRabbit = await GetQueuesFromRabbitMqAsync(queuePrefix);

        return queuesRabbit.MaxBy(x => x);
    }

    // "{prefix}_" so the group's dead-letter queue ("{exchange}.dlq") never counts as a partition queue
    private static async Task<List<string>> GetQueuesFromRabbitMqAsync(string queuePrefix)
    {
        var response = await client.GetAsync("queues");
        response.EnsureSuccessStatusCode();
        using var queues = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return queues.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .Where(x => x.StartsWith(queuePrefix + "_", StringComparison.Ordinal))
            .ToList();
    }

    private static List<string> GetQueuesFromSqlServer(string resourceGroup)
    {
        List<string> queues = [];
        using SqlConnection conn = new(connStr);
        conn.Open();
        var command = conn.CreateCommand();
        command.CommandText = "SELECT ResourceName FROM RBR.Resources WHERE ResourceGroup = @ResourceGroup";
        command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            queues.Add(reader.GetString(0));
        }

        return queues;
    }

    // a quorum queue per partition (US-0022): the broker allows one active consumer (single active consumer), counts
    // deliveries, and dead-letters a message after DeliveryLimit deliveries, at least once
    private static async Task PutQueueRabbitMqAsync(QueueInventory inventory, string queueName, string vhost = "%2f")
    {
        await PutAsync($"exchanges/{vhost}/{inventory.ExchangeName}",
            "{\"type\":\"x-consistent-hash\",\"auto_delete\":false,\"durable\":true,\"internal\":false,\"arguments\":{}}");
        await PutAsync($"queues/{vhost}/{queueName}", JsonSerializer.Serialize(new
        {
            durable = true,
            arguments = new Dictionary<string, object>
            {
                ["x-queue-type"] = "quorum",
                ["x-single-active-consumer"] = true,
                ["x-delivery-limit"] = inventory.DeliveryLimit,
                ["x-dead-letter-exchange"] = inventory.DeadLetterExchange,
                ["x-dead-letter-strategy"] = "at-least-once",
                ["x-overflow"] = "reject-publish", // required by at-least-once dead-lettering
            },
        }));
        // equal weights: the consistent-hash exchange spreads routing keys (conversation IDs) evenly
        await PostAsync($"bindings/{vhost}/e/{inventory.ExchangeName}/q/{queueName}",
            "{\"routing_key\":\"10\",\"arguments\":{}}");
    }

    private static async Task PutAsync(string path, string json)
    {
        var response = await client.PutAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"PUT {path} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static async Task PostAsync(string path, string json)
    {
        var response = await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"POST {path} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }

    private static void InsertQueueSql(string resourceGroup, string queueName)
    {
        using SqlConnection conn = new(connStr);
        conn.Open();
        var command = conn.CreateCommand();
        command.CommandText =
            "INSERT INTO RBR.Resources(ResourceGroup, ResourceName) VALUES(@ResourceGroup, @ResourceName)";
        command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
        command.Parameters.Add("ResourceName", SqlDbType.VarChar, 1000).Value = queueName;
        command.ExecuteNonQuery();
    }

    private static async Task DeleteQueueRabbitMqAsync(string queueName, string vhost = "%2f")
    {
        if (vhost == "/")
        {
            vhost = "%2f";
        }

        var response = await client.DeleteAsync($"queues/{vhost}/{queueName}");
    }

    private static void DeleteQueueSql(string resourceGroup, string queueName)
    {
        using SqlConnection conn = new(connStr);
        conn.Open();
        var command = conn.CreateCommand();
        command.CommandText =
            "DELETE FROM RBR.Resources WHERE ResourceGroup = @ResourceGroup AND ResourceName = @ResourceName";
        command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
        command.Parameters.Add("ResourceName", SqlDbType.VarChar, 1000).Value = queueName;
        command.ExecuteNonQuery();
    }
}
