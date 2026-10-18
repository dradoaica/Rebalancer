using System;

namespace Rebalancer.Core;

/// <summary>Options that control how the client behaves</summary>
public class ClientOptions
{
    /// <summary>If an internal error happens, whether or not to try to recover from it.</summary>
    public bool AutoRecoveryOnError { get; set; }

    /// <summary>The time delay to use when recovering from an error</summary>
    public TimeSpan RestartDelay { get; set; }

    /// <summary>
    ///     Adds a delay before invoking the OnAssignment event handler. This can be useful when it is hard to guarantee
    ///     that a previous assignment has been fully unassigned. An example is RabbitMQ. In the case of a network partition
    ///     and the client has been unable to explicitly close the connection. The broker will not detect that the connection
    ///     is broken until the keep alive timeout has been reached, which is 60 seconds by default. Rebalancing may be
    ///     triggered after only 20 seconds. In this case the broker holds onto the in-flight messages of the dead connection,
    ///     while sending new messages to the new connection. Once the original connection has finally been detected as broken,
    ///     the in-slight messages are redelivered to the new connection, making message delivery out-of-order. This problem
    ///     can be avoided by adding an OnAssignmentDelay that is longer than the keep alive timeout.
    /// </summary>
    public TimeSpan OnAssignmentDelay { get; set; }

    /// <summary>
    ///     How long a client record may go without a keep-alive before the coordinator deletes it (ADR-0014). A node whose
    ///     record was deleted stops its local activity and registers again. When null, 10 times the keep-alive expiry
    ///     period of the resource group is used.
    /// </summary>
    public TimeSpan? ClientRecordRetention { get; set; }

    /// <summary>The effective retention: <see cref="ClientRecordRetention" />, or 10 keep-alive expiry periods.</summary>
    public static TimeSpan GetClientRecordRetention(ClientOptions clientOptions, TimeSpan keepAliveExpiryPeriod) =>
        clientOptions?.ClientRecordRetention ?? TimeSpan.FromTicks(keepAliveExpiryPeriod.Ticks * 10);
}
