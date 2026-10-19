using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.QuorumQueuesExample;

/// <summary>
///     Consumes the queues Rebalancer assigns to this node (US-0022): one channel per queue, prefetch 1 and manual
///     acknowledgement, so a queue's messages, and with them each conversation's turns, are handled one at a time and in
///     order. The queues have single active consumer on: if Rebalancer briefly assigns a queue to two nodes (after a
///     pause, ADR-0019), the broker still delivers to one of them only; the other waits as a standby consumer.
/// </summary>
internal sealed class QueueConsumers(
    IConnection connection,
    Settings settings,
    ConversationGuard guard,
    Action<string> log
)
{
    // short and growing: retries hold up the other conversations of the same queue (head-of-line, as in any ordered partition)
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, (IChannel Channel, string ConsumerTag)> active = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task StartAsync(IEnumerable<string> queues)
    {
        await gate.WaitAsync();
        try
        {
            foreach (var queue in queues)
            {
                var channel = await connection.CreateChannelAsync();
                await channel.BasicQosAsync(0, 1, false);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, queue, delivery);
                var tag = await channel.BasicConsumeAsync(queue, false, consumer);
                active[queue] = (channel, tag);
                log($"consuming {queue}");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    ///     Cancels every consumer and closes its channel. A turn still being handled is either committed and acknowledged
    ///     or, if the channel closes first, redelivered to the next owner, which skips it as a duplicate.
    /// </summary>
    public async Task StopAllAsync()
    {
        await gate.WaitAsync();
        try
        {
            foreach (var (queue, (channel, tag)) in active)
            {
                try
                {
                    await channel.BasicCancelAsync(tag);
                    await channel.CloseAsync();
                }
                catch (Exception ex)
                {
                    log($"stopping {queue}: {ex.Message}");
                }

                channel.Dispose();
                log($"stopped {queue}");
            }

            active.Clear();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task HandleAsync(IChannel channel, string queue, BasicDeliverEventArgs delivery)
    {
        var message = JsonSerializer.Deserialize<TurnMessage>(delivery.Body.Span, JsonOptions);
        var turn = new Turn(message.ConversationId, message.Turn);
        // retried here, never requeued: retrying in place keeps the conversation's order and does not depend on how the
        // broker counts requeues (in RabbitMQ 4.x basic.nack does not count toward x-delivery-limit, basic.reject does);
        // the delivery limit stays the backstop for a turn that crashes the process
        var outcome = TurnOutcome.Failed;
        var attempt = 0;
        while (outcome == TurnOutcome.Failed && attempt < settings.DeliveryLimit)
        {
            if (attempt++ > 0)
            {
                log($"{queue} {turn.ConversationId} turn {turn.TurnNumber} attempt {attempt - 1} failed, retrying");
                await Task.Delay(RetryDelay * attempt);
            }

            outcome = await guard.ProcessAsync(turn, () => WorkAsync(turn), log);
        }

        switch (outcome)
        {
            case TurnOutcome.Processed:
                log($"{queue} {turn.ConversationId} turn {turn.TurnNumber} processed");
                await channel.BasicAckAsync(delivery.DeliveryTag, false);
                break;
            case TurnOutcome.Duplicate:
                log($"{queue} {turn.ConversationId} turn {turn.TurnNumber} duplicate, skipped");
                await channel.BasicAckAsync(delivery.DeliveryTag, false);
                break;
            case TurnOutcome.Parked:
                log($"{queue} {turn.ConversationId} turn {turn.TurnNumber} parked: conversation blocked");
                await channel.BasicRejectAsync(delivery.DeliveryTag, false); // to the dead-letter queue
                break;
            case TurnOutcome.Failed:
                await guard.BlockAsync(turn.ConversationId);
                log(
                    $"{queue} {turn.ConversationId} turn {turn.TurnNumber} dead-lettered after {attempt} attempts; conversation blocked"
                );
                await channel.BasicRejectAsync(delivery.DeliveryTag, false);
                break;
        }
    }

    private async Task WorkAsync(Turn turn)
    {
        await Task.Delay(settings.WorkDuration); // stands for answering the turn
        if (turn.ConversationId == settings.FailConversation && turn.TurnNumber == settings.FailTurn)
        {
            throw new InvalidOperationException("injected failure");
        }
    }

    private sealed record TurnMessage(string ConversationId, int Turn);
}
