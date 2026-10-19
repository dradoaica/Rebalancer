namespace Rebalancer.IntegrationTests;

/// <summary>
///     Freezes one node's coordination: while paused, every call it makes to the store (lease, client and resource
///     services) waits, as it would during a GC pause or VM freeze. Calls already in flight complete. The node's own
///     threads and resource handlers keep running, which a real process pause would also freeze; the tests therefore
///     assert on what the node does after it resumes, not on what it reports while paused.
/// </summary>
internal sealed class NodePause
{
    private readonly object sync = new();
    private TaskCompletionSource running = Completed();
    private bool resumed;

    public bool HasResumed
    {
        get
        {
            lock (sync)
            {
                return resumed;
            }
        }
    }

    /// <summary>
    ///     Whether the coordinator status in the first keep-alive this node sent after <see cref="Resume" /> was "stop";
    ///     null until that keep-alive happened.
    /// </summary>
    public bool? FirstKeepAliveAfterResumeWasStop { get; private set; }

    public void Pause()
    {
        lock (sync)
        {
            running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Resume()
    {
        lock (sync)
        {
            resumed = true;
            running.TrySetResult();
        }
    }

    /// <summary>Waits while paused; returns true when the call goes ahead after <see cref="Resume" />.</summary>
    public async Task<bool> WaitWhilePausedAsync()
    {
        Task gate;
        lock (sync)
        {
            gate = running.Task;
        }

        await gate;
        return HasResumed;
    }

    /// <param name="afterResume">The value <see cref="WaitWhilePausedAsync" /> returned for this keep-alive.</param>
    /// <param name="isStop">Whether the record the keep-alive returned says "stop".</param>
    public void RecordKeepAlive(bool afterResume, bool isStop)
    {
        lock (sync)
        {
            if (afterResume && FirstKeepAliveAfterResumeWasStop == null)
            {
                FirstKeepAliveAfterResumeWasStop = isStop;
            }
        }
    }

    private static TaskCompletionSource Completed()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }
}
