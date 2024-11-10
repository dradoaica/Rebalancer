using RabbitMQ.Client;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMQTools;

public class MessageManager
{
    private static HttpClient client;
    private static RabbitConnection rabbitConn;

    public static void Initialize(RabbitConnection rabbitConnection)
    {
        client.BaseAddress = new Uri($"http://{rabbitConnection.Host}:{rabbitConnection.ManagementPort}/api/");
        client = new HttpClient();
        var byteArray = Encoding.ASCII.GetBytes($"{rabbitConnection.Username}:{rabbitConnection.Password}");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        rabbitConn = rabbitConnection;
    }

    public static async Task SendMessagesViaHttpAsync(
        string exchange,
        string routingKeyPrefix,
        int count,
        string vhost = "%2f")
    {
        if (vhost == "/")
        {
            vhost = "%2f";
        }

        for (var i = 0; i < count; i++)
        {
            var routingKey = routingKeyPrefix + i;
            StringContent content = new(
                "{\"properties\":{},\"routing_key\":\"" +
                routingKey +
                "\",\"payload\":\"" +
                routingKey +
                "\",\"payload_encoding\":\"string\"}",
                Encoding.UTF8,
                "application/json");
            var response = await client.PostAsync($"exchanges/{vhost}/{exchange}/publish", content);
        }
    }

    public static async Task SendMessagesViaClient(string exchange, string routingKeyPrefix, int count)
    {
        ConnectionFactory factory = new()
        {
            HostName = rabbitConn.Host,
            Port = rabbitConn.Port,
            VirtualHost = rabbitConn.VirtualHost,
            UserName = rabbitConn.Username,
            Password = rabbitConn.Password,
        };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        for (var i = 0; i < count; i++)
        {
            var routingKey = routingKeyPrefix + i;
            var message = routingKeyPrefix + i;
            var body = Encoding.UTF8.GetBytes(message);
            await channel.BasicPublishAsync(exchange, routingKey, body);
        }
    }
}
