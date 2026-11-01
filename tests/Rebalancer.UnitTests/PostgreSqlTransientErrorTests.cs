using Npgsql;
using Rebalancer.PostgreSql.Connections;
using Xunit;

namespace Rebalancer.UnitTests;

/// <summary>US-0026 criterion 6 and TM-0004 D2: which PostgreSQL failures the backend retries.</summary>
public sealed class PostgreSqlTransientErrorTests
{
    [Theory]
    [InlineData("08000")] // connection_exception
    [InlineData("08001")] // sqlclient_unable_to_establish_sqlconnection
    [InlineData("08003")] // connection_does_not_exist
    [InlineData("08004")] // sqlserver_rejected_establishment_of_sqlconnection
    [InlineData("08006")] // connection_failure
    [InlineData("08P01")] // protocol_violation
    [InlineData("40001")] // serialization_failure
    [InlineData("40P01")] // deadlock_detected
    [InlineData("57P01")] // admin_shutdown
    [InlineData("55P03")] // lock_not_available: the lock_timeout bound on a stalled lease transaction
    [InlineData("25P03")] // idle_in_transaction_session_timeout: the stalled node itself, after the server ended it
    public void RetryableSqlStates_AreTransient(string sqlState) =>
        Assert.True(TransientErrorDetector.IsTransient(ServerError(sqlState)));

    [Theory]
    [InlineData("23505")] // unique_violation
    [InlineData("42P01")] // undefined_table: a missing schema is not retried
    [InlineData("42501")] // insufficient_privilege
    public void OtherSqlStates_AreNotTransient(string sqlState) =>
        Assert.False(TransientErrorDetector.IsTransient(ServerError(sqlState)));

    // a network failure without a server error code: Npgsql marks it transient by its inner exception
    [Fact]
    public void ANetworkFailure_IsTransient() =>
        Assert.True(TransientErrorDetector.IsTransient(new NpgsqlException("read failed", new IOException("reset"))));

    [Fact]
    public void ATimeout_IsTransient() => Assert.True(TransientErrorDetector.IsTransient(new TimeoutException()));

    [Fact]
    public void AnUnrelatedException_IsNotTransient() =>
        Assert.False(TransientErrorDetector.IsTransient(new InvalidOperationException()));

    private static PostgresException ServerError(string sqlState) =>
        new("message", "ERROR", "ERROR", sqlState);
}
