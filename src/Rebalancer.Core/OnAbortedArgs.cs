namespace Rebalancer.Core;

using System;

/// <summary>
///     EventArgs that describe the cause of an aborted event
/// </summary>
public sealed class OnAbortedArgs : EventArgs
{
    public OnAbortedArgs(string abortReason, Exception exception)
    {
        this.AbortReason = abortReason;
        this.Exception = exception;
    }

    /// <summary>
    ///     The reason for aborting the client
    /// </summary>
    public string AbortReason { get; }

    /// <summary>
    ///     The exception caused that caused event, if one exists
    /// </summary>
    public Exception Exception { get; }
}
