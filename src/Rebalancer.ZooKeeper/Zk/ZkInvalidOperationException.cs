namespace Rebalancer.ZooKeeper.Zk;

using System;

public class ZkInvalidOperationException : Exception
{
    public ZkInvalidOperationException(string message)
        : base(message)
    {
    }

    public ZkInvalidOperationException(string message, Exception ex)
        : base(message, ex)
    {
    }
}
