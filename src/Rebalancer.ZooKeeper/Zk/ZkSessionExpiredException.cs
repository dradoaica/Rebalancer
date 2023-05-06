namespace Rebalancer.ZooKeeper.Zk;

using System;

public class ZkSessionExpiredException : Exception
{
    public ZkSessionExpiredException(string message)
        : base(message)
    {
    }

    public ZkSessionExpiredException(string message, Exception ex)
        : base(message, ex)
    {
    }
}
