using Npgsql;
using System;

namespace Rebalancer.PostgreSql.Connections;

/// <summary>
///     Which PostgreSQL failures a retry can compensate (ADR-0030, TM-0004): connection failures, serialization
///     failures, deadlocks, an administrator shutdown, and both ends of the bound on a stalled lease transaction: the
///     waiting nodes' lock timeout and the stalled node's own session ended by the server (TM-0004 D2).
/// </summary>
internal static class TransientErrorDetector
{
    public static bool IsTransient(Exception ex)
    {
        switch (ex)
        {
            case PostgresException pg:
                return IsTransientSqlState(pg.SqlState);
            case NpgsqlException npgsql:
                // network and timeout failures without a server error code
                return npgsql.IsTransient;
            case TimeoutException:
                return true;
            default:
                return false;
        }
    }

    internal static bool IsTransientSqlState(string sqlState)
    {
        if (sqlState == null)
        {
            return false;
        }

        // class 08: connection exception
        if (sqlState.StartsWith("08", StringComparison.Ordinal))
        {
            return true;
        }

        switch (sqlState)
        {
            case "40001": // serialization_failure
            case "40P01": // deadlock_detected
            case "57P01": // admin_shutdown
            case "55P03": // lock_not_available: lock_timeout expired while another node held the group row
            case "25P03": // idle_in_transaction_session_timeout: this node stalled holding the row; a retry re-reads it
                return true;
            default:
                return false;
        }
    }
}
