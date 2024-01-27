using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace Rebalanser.RabbitMQTools;

public class MessageManager
{
    private static HttpClient Client;
    private static RabbitConnection RabbitConn;

    public static void Initialize(RabbitConnection rabbitConnection)
    {
        Client.BaseAddress = new Uri($"http://{rabbitConnection.Host}:{rabbitConnection.ManagementPort}/api/");
        Client = new HttpClient();
        var byteArray = Encoding.ASCII.GetBytes($"{rabbitConnection.Username}:{rabbitConnection.Password}");
        Client.DefaultRequestHeaders.Authorization
            = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        RabbitConn = rabbitConnection;
    }

    public static async Task SendMessagesViaHttpAsync(string exchange, string routingKeyPrefix, int count,
        string vhost = "%2f")
    {
        if (vhost == "/")
        {
            vhost = "%2f";
        }

        for (var i = 0; i < count; i++)
        {
            var routingKey = routingKeyPrefix + i;
            StringContent content =
                new(
                    "{\"properties\":{},\"routing_key\":\"" + routingKey + "\",\"payload\":\"" + routingKey +
                    "\",\"payload_encoding\":\"string\"}", Encoding.UTF8, "application/json");
            var response = await Client.PostAsync($"exchanges/{vhost}/{exchange}/publish", content);
        }
    }

    public static void SendMessagesViaClient(string exchange, string routingKeyPrefix, int count)
    {
        ConnectionFactory factory = new()
        {
            HostName = RabbitConn.Host,
            Port = RabbitConn.Port,
            VirtualHost = RabbitConn.VirtualHost,
            UserName = RabbitConn.Username,
            Password = RabbitConn.Password
        };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        for (var i = 0; i < count; i++)
        {
            var routingKey = routingKeyPrefix + i;
            var message = routingKeyPrefix + i;
            var body = Encoding.UTF8.GetBytes(message);
            channel.BasicPublish(exchange,
                routingKey,
                null,
                body);
        }
    }
}
