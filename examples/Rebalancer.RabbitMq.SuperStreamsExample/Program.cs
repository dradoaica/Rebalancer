using RabbitMQ.Stream.Client;
using RabbitMQ.Stream.Client.AMQP;
using RabbitMQ.Stream.Client.Reliable;
using Rebalancer.RabbitMq.ExampleWithSqlServerBackend;
using System;
using System.Buffers;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.SuperStreamsExample;

/// <summary>
///     The same ordered consumer group as the option A example, with RabbitMQ super streams and no Rebalancer (US-0023,
///     ADR-0024): a super stream with N partitions routed by conversation ID; consumers of one reference share the
///     partitions, one active consumer per partition (single active consumer); offsets are stored on the server.
///     Commands: <c>create</c>, <c>publish [conversations] [turns]</c>, <c>consume</c>. See README.md.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> Main(string[] args)
    {
        var settings = Settings.FromEnvironment();
        void Log(string text) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{settings.Node}] {text}");

        var endpoint = new IPEndPoint((await Dns.GetHostAddressesAsync(settings.StreamHost)).First(), settings.StreamPort);
        var system = await StreamSystem.Create(new StreamSystemConfig
        {
            UserName = settings.User,
            Password = settings.Password,
            Endpoints = [endpoint],
            // single node behind a published port (e.g. Docker): connect to this address whatever the broker advertises
            AddressResolver = new AddressResolver(endpoint),
            ClientProvidedName = $"super-streams-example-{settings.Node}",
        });
        try
        {
            switch (args.FirstOrDefault() ?? "consume")
            {
                case "create":
                    await CreateAsync(system, settings, Log);
                    break;
                case "publish":
                    await PublishAsync(system, settings,
                        args.Length > 1 ? int.Parse(args[1]) : 20, args.Length > 2 ? int.Parse(args[2]) : 10, Log);
                    break;
                default:
                    await ConsumeAsync(system, settings, Log);
                    break;
            }
        }
        finally
        {
            await system.Close();
        }

        return 0;
    }

    private static async Task CreateAsync(StreamSystem system, Settings settings, Action<string> log)
    {
        if (!await system.SuperStreamExists(settings.SuperStream))
        {
            await system.CreateSuperStream(new PartitionsSuperStreamSpec(settings.SuperStream, settings.Partitions));
        }

        if (!await system.StreamExists(settings.ParkingStream))
        {
            await system.CreateStream(new StreamSpec(settings.ParkingStream));
        }

        await new ConversationGuard(settings.SqlConnectionString).EnsureTableAsync();
        log($"super stream {settings.SuperStream} ({settings.Partitions} partitions), {settings.ParkingStream} and the conversation table are in place");
    }

    // turn by turn, routed by conversation ID, so a conversation stays in one partition; waits for every confirmation
    private static async Task PublishAsync(StreamSystem system, Settings settings, int conversations, int turns,
        Action<string> log)
    {
        var confirmed = 0;
        var failed = 0;
        var producer = await Producer.Create(new ProducerConfig(system, settings.SuperStream)
        {
            SuperStreamConfig = new SuperStreamConfig { Routing = message => (string)message.ApplicationProperties["conversation"], },
            ConfirmationHandler = confirmation =>
            {
                if (confirmation.Status == ConfirmationStatus.Confirmed)
                {
                    Interlocked.Add(ref confirmed, confirmation.Messages.Count);
                }
                else
                {
                    Interlocked.Add(ref failed, confirmation.Messages.Count);
                }

                return Task.CompletedTask;
            },
        });
        for (var turn = 1; turn <= turns; turn++)
        {
            for (var c = 1; c <= conversations; c++)
            {
                var conversationId = $"conversation-{c}";
                await producer.Send(new Message(JsonSerializer.SerializeToUtf8Bytes(new { conversationId, turn, }))
                {
                    ApplicationProperties = new ApplicationProperties { { "conversation", conversationId }, },
                });
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref confirmed) + Volatile.Read(ref failed) < conversations * turns && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        await producer.Close();
        log($"published {conversations} conversations x {turns} turns: {confirmed} confirmed, {failed} failed");
    }

    private static async Task ConsumeAsync(StreamSystem system, Settings settings, Action<string> log)
    {
        var guard = new ConversationGuard(settings.SqlConnectionString);
        var parking = await Producer.Create(new ProducerConfig(system, settings.ParkingStream));
        var consumer = await Consumer.Create(new ConsumerConfig(system, settings.SuperStream)
        {
            IsSuperStream = true,
            IsSingleActiveConsumer = true,
            Reference = settings.Reference,
            OffsetSpec = new OffsetTypeFirst(),
            // when this node becomes a partition's active consumer, continue after the last offset stored for it
            ConsumerUpdateListener = async (reference, stream, isActive) =>
            {
                var stored = await system.TryQueryOffset(reference, stream);
                log($"{stream} {(isActive ? "active" : "standby")}, resuming after offset {stored?.ToString() ?? "(none)"}");
                return stored.HasValue ? new OffsetTypeOffset(stored.Value + 1) : new OffsetTypeFirst();
            },
            MessageHandler = async (stream, rawConsumer, context, message) =>
            {
                await HandleAsync(stream, message, guard, parking, settings, log);
                // stored after the turn is handled: at least once; the turn guard skips what a failover redelivers
                await rawConsumer.StoreOffset(context.Offset);
            },
        });
        log($"consuming {settings.SuperStream} as {settings.Reference}; Ctrl+C to stop");

        var stop = new TaskCompletionSource();
        void OnSignal(PosixSignalContext context)
        {
            context.Cancel = true;
            stop.TrySetResult();
        }

        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
        await stop.Task;

        await consumer.Close();
        await parking.Close();
        log("stopped");
    }

    private static async Task HandleAsync(string stream, Message message, ConversationGuard guard, Producer parking,
        Settings settings, Action<string> log)
    {
        var body = message.Data.Contents.ToArray();
        var turnMessage = JsonSerializer.Deserialize<TurnMessage>(body, JsonOptions);
        var turn = new Turn(turnMessage.ConversationId, turnMessage.Turn);

        // streams have no dead-lettering or requeue: retry in place, then park the turn and block its conversation
        var outcome = TurnOutcome.Failed;
        var attempt = 0;
        while (outcome == TurnOutcome.Failed && attempt < settings.Attempts)
        {
            if (attempt++ > 0)
            {
                log($"{stream} {turn.ConversationId} turn {turn.TurnNumber} attempt {attempt - 1} failed, retrying");
                await Task.Delay(TimeSpan.FromMilliseconds(200) * attempt);
            }

            outcome = await guard.ProcessAsync(turn, () => WorkAsync(turn, settings), log);
        }

        switch (outcome)
        {
            case TurnOutcome.Processed:
                log($"{stream} {turn.ConversationId} turn {turn.TurnNumber} processed");
                break;
            case TurnOutcome.Duplicate:
                log($"{stream} {turn.ConversationId} turn {turn.TurnNumber} duplicate, skipped");
                break;
            case TurnOutcome.Parked:
                await parking.Send(new Message(body));
                log($"{stream} {turn.ConversationId} turn {turn.TurnNumber} parked: conversation blocked");
                break;
            case TurnOutcome.Failed:
                await guard.BlockAsync(turn.ConversationId);
                await parking.Send(new Message(body));
                log($"{stream} {turn.ConversationId} turn {turn.TurnNumber} parked after {attempt} attempts; conversation blocked");
                break;
        }
    }

    private static async Task WorkAsync(Turn turn, Settings settings)
    {
        await Task.Delay(settings.WorkDuration); // stands for answering the turn
        if (turn.ConversationId == settings.FailConversation && turn.TurnNumber == settings.FailTurn)
        {
            throw new InvalidOperationException("injected failure");
        }
    }

    private sealed record TurnMessage(string ConversationId, int Turn);
}
