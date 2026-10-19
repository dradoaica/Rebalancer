using System;

namespace Rebalancer.Core;

/// <summary>
///     EventArgs that describe the error behind <see cref="RebalancerClient.OnAborted" />; the client may recover from it
///     (see the remarks there).
/// </summary>
public sealed class OnAbortedArgs : EventArgs
{
    public OnAbortedArgs(string abortReason, Exception exception)
    {
        AbortReason = abortReason;
        Exception = exception;
    }

    /// <summary>The reason for aborting the client</summary>
    public string AbortReason { get; }

    /// <summary>The exception that caused the event; null when the provider has none (e.g. "Unknown shutdown reason").</summary>
    public Exception Exception { get; }
}
