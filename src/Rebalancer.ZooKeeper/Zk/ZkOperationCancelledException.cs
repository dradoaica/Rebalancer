namespace Rebalancer.ZooKeeper.Zk;

using System;

public class ZkOperationCancelledException : Exception
{
    public ZkOperationCancelledException(string message)
        : base(message)
    {
    }
}
