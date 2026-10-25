// the usings are needed by the example projects; only Rebalancer.IntegrationTests, which also compiles this file, has implicit usings
// ReSharper disable RedundantUsingDirective

using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Threading.Tasks;

namespace Rebalancer.RabbitMq.QuorumQueuesExample;

internal enum TurnOutcome
{
    Processed,
    Duplicate,
    Parked,
    Failed,
}

/// <summary>
///     The application-level order guard every option needs (ADR-0021: no fencing token from the library). Per
///     conversation it stores the last processed turn and whether the conversation is blocked (
///     <c>Example.ConversationTurns</c>, created by the Tools <c>create</c> command). Turns run inside one transaction
///     that holds the conversation's row, so even two consumers could not process one conversation at once.
/// </summary>
/// <remarks>
///     Microsoft.Data.SqlClient comes with the Rebalancer.SqlServer project reference; the example adds no package
///     (ADR-0024).
/// </remarks>
internal sealed class ConversationGuard(string connectionString)
{
    public async Task<TurnOutcome> ProcessAsync(Turn turn, Func<Task> work, Action<string> log)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var (lastTurn, blocked) = await ReadAsync(connection, transaction, turn.ConversationId);

        if (blocked)
        {
            return TurnOutcome.Parked; // a failed turn blocks its conversation: later turns must not skip over it
        }

        if (turn.TurnNumber <= lastTurn)
        {
            return TurnOutcome.Duplicate; // redelivered after a crash or failover: already processed
        }

        if (turn.TurnNumber != lastTurn + 1)
        {
            // never process past a gap; with single active consumer this should not happen
            log(
                $"OUT OF ORDER {turn.ConversationId}: turn {turn.TurnNumber} after {lastTurn}; blocking the conversation"
            );
            await WriteAsync(connection, transaction, turn.ConversationId, lastTurn, true);
            await transaction.CommitAsync();
            return TurnOutcome.Parked;
        }

        try
        {
            await work();
        }
        catch (Exception ex)
        {
            log($"{turn.ConversationId} turn {turn.TurnNumber} failed: {ex.Message}");
            await transaction.RollbackAsync();
            return TurnOutcome.Failed;
        }

        await WriteAsync(connection, transaction, turn.ConversationId, turn.TurnNumber, false);
        await transaction.CommitAsync();
        return TurnOutcome.Processed;
    }

    /// <summary>Creates <c>Example.ConversationTurns</c> if missing (the option A tools create it too).</summary>
    public async Task EnsureTableAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            IF SCHEMA_ID('Example') IS NULL EXEC('CREATE SCHEMA Example');
            IF OBJECT_ID('Example.ConversationTurns') IS NULL
                CREATE TABLE Example.ConversationTurns (
                    ConversationId varchar(200) NOT NULL PRIMARY KEY,
                    LastTurn int NOT NULL,
                    Blocked bit NOT NULL);
            """,
            connection
        );
        await command.ExecuteNonQueryAsync();
    }

    public async Task BlockAsync(string conversationId)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var (lastTurn, _) = await ReadAsync(connection, transaction, conversationId);
        await WriteAsync(connection, transaction, conversationId, lastTurn, true);
        await transaction.CommitAsync();
    }

    // UPDLOCK, HOLDLOCK: the row (or its absence) stays locked until the transaction ends
    private static async Task<(int LastTurn, bool Blocked)> ReadAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string conversationId
    )
    {
        await using var command = new SqlCommand(
            "SELECT LastTurn, Blocked FROM Example.ConversationTurns WITH (UPDLOCK, HOLDLOCK) WHERE ConversationId = @c",
            connection,
            transaction
        );
        command.Parameters.Add("@c", SqlDbType.VarChar, 200).Value = conversationId;
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetInt32(0), reader.GetBoolean(1)) : (0, false);
    }

    private static async Task WriteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string conversationId,
        int lastTurn,
        bool blocked
    )
    {
        await using var command = new SqlCommand(
            """
            UPDATE Example.ConversationTurns SET LastTurn = @t, Blocked = @b WHERE ConversationId = @c;
            IF @@ROWCOUNT = 0 INSERT INTO Example.ConversationTurns (ConversationId, LastTurn, Blocked) VALUES (@c, @t, @b);
            """,
            connection,
            transaction
        );
        command.Parameters.Add("@c", SqlDbType.VarChar, 200).Value = conversationId;
        command.Parameters.Add("@t", SqlDbType.Int).Value = lastTurn;
        command.Parameters.Add("@b", SqlDbType.Bit).Value = blocked;
        await command.ExecuteNonQueryAsync();
    }
}

internal sealed record Turn(string ConversationId, int TurnNumber);
