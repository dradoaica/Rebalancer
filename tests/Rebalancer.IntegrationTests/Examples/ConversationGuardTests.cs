using Rebalancer.RabbitMq.QuorumQueuesExample;
using Testcontainers.MsSql;
using Xunit;

namespace Rebalancer.IntegrationTests.Examples;

/// <summary>
///     Requires Docker. US-0022 criterion 4: the example's per-conversation order guard on a real SQL Server. A stale
///     or duplicate turn is skipped, a gap or a blocked conversation parks the turn, a failed turn changes nothing, and
///     two consumers cannot process the same turn.
/// </summary>
public sealed class ConversationGuardTests(ConversationGuardTests.SqlServer sql)
    : IClassFixture<ConversationGuardTests.SqlServer>
{
    [SkippableFact]
    public async Task TurnsInOrder_AreProcessed_AndADuplicateIsSkipped()
    {
        var (guard, conversation) = await NewConversationAsync();
        var runs = 0;

        Assert.Equal(TurnOutcome.Processed, await guard.ProcessAsync(new Turn(conversation, 1), Work, Log));
        Assert.Equal(TurnOutcome.Processed, await guard.ProcessAsync(new Turn(conversation, 2), Work, Log));
        Assert.Equal(TurnOutcome.Duplicate, await guard.ProcessAsync(new Turn(conversation, 2), Work, Log));
        Assert.Equal(TurnOutcome.Duplicate, await guard.ProcessAsync(new Turn(conversation, 1), Work, Log));

        Assert.Equal(2, runs);
        return;

        Task Work()
        {
            runs++;
            return Task.CompletedTask;
        }
    }

    [SkippableFact]
    public async Task AGap_BlocksTheConversation_AndLaterTurnsAreParked()
    {
        var (guard, conversation) = await NewConversationAsync();
        await guard.ProcessAsync(new Turn(conversation, 1), Done, Log);

        Assert.Equal(TurnOutcome.Parked, await guard.ProcessAsync(new Turn(conversation, 3), Done, Log));
        Assert.Equal(TurnOutcome.Parked, await guard.ProcessAsync(new Turn(conversation, 2), Done, Log));
    }

    [SkippableFact]
    public async Task AFailedTurn_ChangesNothing_AndCanBeRetried()
    {
        var (guard, conversation) = await NewConversationAsync();
        await guard.ProcessAsync(new Turn(conversation, 1), Done, Log);

        Assert.Equal(
            TurnOutcome.Failed,
            await guard.ProcessAsync(new Turn(conversation, 2), () => throw new InvalidOperationException("boom"), Log)
        );
        Assert.Equal(TurnOutcome.Processed, await guard.ProcessAsync(new Turn(conversation, 2), Done, Log));
    }

    [SkippableFact]
    public async Task ABlockedConversation_ParksItsNextTurn()
    {
        var (guard, conversation) = await NewConversationAsync();
        await guard.ProcessAsync(new Turn(conversation, 1), Done, Log);

        await guard.BlockAsync(conversation); // as a dead-lettered turn does

        Assert.Equal(TurnOutcome.Parked, await guard.ProcessAsync(new Turn(conversation, 2), Done, Log));
    }

    [SkippableFact]
    public async Task TwoConsumers_CannotProcessTheSameTurn()
    {
        var (guard, conversation) = await NewConversationAsync();
        var other = new ConversationGuard(sql.ConnectionString);
        var bothInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = guard.ProcessAsync(
            new Turn(conversation, 1),
            async () => await Task.WhenAny(bothInside.Task, Task.Delay(TimeSpan.FromSeconds(2))),
            Log
        );
        await Task.Delay(200); // the first holds the conversation's row
        var second = other.ProcessAsync(
            new Turn(conversation, 1),
            () =>
            {
                bothInside.TrySetResult();
                return Task.CompletedTask;
            },
            Log
        );

        var outcomes = await Task.WhenAll(first, second);

        Assert.False(bothInside.Task.IsCompleted, "both consumers ran the turn's work");
        Assert.Equal([TurnOutcome.Processed, TurnOutcome.Duplicate,], outcomes.OrderBy(x => x));
    }

    private static Task Done() => Task.CompletedTask;

    private static void Log(string message)
    {
    }

    private async Task<(ConversationGuard Guard, string Conversation)> NewConversationAsync()
    {
        Skip.IfNot(DockerSupport.IsDockerAvailable(), "Docker is not available.");
        var guard = new ConversationGuard(await sql.GetConnectionStringAsync());
        await guard.EnsureTableAsync();
        return (guard, "c-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>One SQL Server for the class, started on first use.</summary>
    public sealed class SqlServer : IAsyncLifetime
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private MsSqlContainer? container;
        public string ConnectionString => container!.GetConnectionString();

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (container != null)
            {
                await container.DisposeAsync();
            }
        }

        public async Task<string> GetConnectionStringAsync()
        {
            await gate.WaitAsync();
            try
            {
                if (container == null)
                {
                    container = new MsSqlBuilder(DockerSupport.MsSqlImage).WithPassword("Tests_pass_123!").Build();
                    await container.StartAsync();
                }

                return container.GetConnectionString();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
