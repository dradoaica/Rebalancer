﻿using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMQTools;

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

    public static void EnsureResourceGroup(string resourceGroup, int leaseExpirySeconds)
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
                "INSERT INTO RBR.ResourceGroups(ResourceGroup, FencingToken, LeaseExpirySeconds) VALUES(@ResourceGroup, 1, @LeaseExpirySeconds)";
            command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            command.Parameters.Add("LeaseExpirySeconds", SqlDbType.Int).Value = leaseExpirySeconds;
            command.ExecuteNonQuery();
            Console.WriteLine("Created consumer group");
        }
        else
        {
            command.Parameters.Clear();
            command.CommandText =
                "UPDATE RBR.ResourceGroups SET LeaseExpirySeconds = @LeaseExpirySeconds WHERE ResourceGroup = @ResourceGroup";
            command.Parameters.Add("ResourceGroup", SqlDbType.VarChar, 100).Value = resourceGroup;
            command.Parameters.Add("LeaseExpirySeconds", SqlDbType.Int).Value = leaseExpirySeconds;
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

    public static async Task AddQueueSqlAsync(string resourceGroup, string exchangeName, string queuePrefix)
    {
        var lastQueue = await GetMaxQueueAsync(queuePrefix);
        if (lastQueue == null)
        {
            var queueName = queuePrefix + "_0001";
            await PutQueueRabbitMqAsync(exchangeName, queueName);
            InsertQueueSql(resourceGroup, queueName);
            Console.WriteLine($"Queue {queueName} added to consumer group {resourceGroup}");
        }
        else
        {
            var qNumber = lastQueue[(lastQueue.IndexOf("_") + 1)..];
            var nextNumber = int.Parse(qNumber) + 1;
            var queueName = queuePrefix + "_" + nextNumber.ToString().PadLeft(4, '0');
            await PutQueueRabbitMqAsync(exchangeName, queueName);
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

    private static async Task<List<string>> GetQueuesFromRabbitMqAsync(string queuePrefix)
    {
        var response = await client.GetAsync("queues");
        var json = await response.Content.ReadAsStringAsync();
        var queues = JArray.Parse(json);
        var queueNames = queues.Select(x => x["name"].Value<string>()).Where(x => x.StartsWith(queuePrefix)).ToList();
        return queueNames;
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

    private static async Task PutQueueRabbitMqAsync(string exchange, string queueName, string vhost = "%2f")
    {
        if (vhost == "/")
        {
            vhost = "%2f";
        }

        StringContent createExchangeContent = new(
            "{\"type\":\"x-consistent-hash\",\"auto_delete\":false,\"durable\":true,\"internal\":false,\"arguments\":{}}",
            Encoding.UTF8,
            "application/json");
        var createExchangeResponse = await client.PutAsync($"exchanges/{vhost}/{exchange}", createExchangeContent);
        if (!createExchangeResponse.IsSuccessStatusCode)
        {
            throw new Exception("Failed to create exchange");
        }

        StringContent createQueueContent = new("{ \"durable\":true}", Encoding.UTF8, "application/json");
        var createQueueResponse = await client.PutAsync($"queues/{vhost}/{queueName}", createQueueContent);
        if (!createQueueResponse.IsSuccessStatusCode)
        {
            throw new Exception("Failed to create queue");
        }

        StringContent createBindingsContent = new(
            "{\"routing_key\":\"10\",\"arguments\":{}}",
            Encoding.UTF8,
            "application/json");
        var createBindingsResponse = await client.PostAsync(
            $"bindings/{vhost}/e/{exchange}/q/{queueName}",
            createBindingsContent);
        if (!createBindingsResponse.IsSuccessStatusCode)
        {
            throw new Exception("Failed to create exchange to queue bindings");
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
