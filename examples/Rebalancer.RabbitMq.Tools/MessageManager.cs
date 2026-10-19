using RabbitMQ.Client;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.Tools;

/// <summary>Publishes test conversations for the ordered consumer group example (US-0022).</summary>
public static class MessageManager
{
    /// <summary>
    ///     Publishes <paramref name="turns" /> turns of each of <paramref name="conversations" /> conversations, turn by
    ///     turn, with the conversation ID as routing key, so the consistent-hash exchange keeps a conversation in one queue.
    ///     Waits for each publisher confirm: with <c>x-overflow: reject-publish</c> a full queue rejects a message.
    /// </summary>
    public static async Task PublishConversationsAsync(
        RabbitConnection rabbitConn,
        string exchange,
        int conversations,
        int turns
    )
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
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
        };
        for (var turn = 1; turn <= turns; turn++)
        {
            for (var c = 1; c <= conversations; c++)
            {
                var conversationId = $"conversation-{c}";
                var body = JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        conversationId,
                        turn,
                    }
                );
                await channel.BasicPublishAsync(exchange, conversationId, false, properties, body);
            }
        }

        Console.WriteLine($"Published {conversations} conversations x {turns} turns to {exchange}");
    }
}
