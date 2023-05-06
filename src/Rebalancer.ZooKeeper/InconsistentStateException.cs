namespace Rebalancer.ZooKeeper;

using System;

public class InconsistentStateException : Exception
{
    public InconsistentStateException(string message)
        : base(message)
    {
    }

    public InconsistentStateException(string message, Exception ex)
        : base(message, ex)
    {
    }
}
