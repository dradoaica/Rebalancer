namespace Rebalancer.PostgreSql.Connections;

/// <summary>
///     TM-0004 D2: a node stalled inside a transaction that holds rows of a group is ended by the server after half
///     the group's lease expiry (<c>idle_in_transaction_session_timeout</c>), and waiting nodes give up after the same
///     time (<c>lock_timeout</c>, SQLSTATE 55P03, retried as transient). Set per transaction, so it needs no role setting.
/// </summary>
internal static class StallBound
{
    /// <summary>The first statement of such a transaction; takes the parameter <c>@ResourceGroup</c>.</summary>
    public const string SetTimeouts = @"SELECT set_config('lock_timeout', timeout_ms, true),
       set_config('idle_in_transaction_session_timeout', timeout_ms, true)
  FROM (SELECT COALESCE((SELECT lease_expiry_seconds * 500
                           FROM rbr.resource_groups
                          WHERE resource_group = @ResourceGroup), 30000)::text AS timeout_ms) AS t;";
}
